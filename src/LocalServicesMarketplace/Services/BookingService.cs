using System.Data;
using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Services;

/// <summary>
/// Booking workflow: Pending -> Confirmed -> Completed, with Rejected / Cancelled branches.
///
/// Concurrency protection, from the inside out:
///   1. Filtered unique index UX_Bookings_ActiveSlot: one active booking per time slot (database guarantee).
///   2. dbo.CreateBooking stored procedure: validates and inserts inside a transaction holding
///      UPDLOCK/HOLDLOCK (serializable range lock) on the slot and its bookings.
///   3. Reschedule runs in a SERIALIZABLE EF Core transaction.
///   4. Status changes use the Booking.RowVersion optimistic concurrency token.
/// </summary>
public class BookingService(
    ApplicationDbContext db,
    INotificationService notifications,
    IActivityLogger activity,
    ITimeZoneService tz,
    IOptions<MarketplaceOptions> options,
    ILogger<BookingService> logger) : IBookingService
{
    private readonly MarketplaceOptions _options = options.Value;

    private static readonly BookingStatus[] ActiveStatuses = [BookingStatus.Pending, BookingStatus.Confirmed];

    // ------------------------------------------------------------------ availability

    public async Task<IReadOnlyList<SlotDayVm>> GetAvailableSlotsAsync(int providerId, int days = 14, int? excludeBookingId = null, CancellationToken ct = default)
    {
        var earliest = tz.UtcNow.AddMinutes(_options.MinBookingLeadMinutes);
        var (_, horizon) = tz.LocalDayRangeUtc(tz.LocalToday.AddDays(days - 1));

        var slots = await db.TimeSlots.AsNoTracking()
            .Where(s => s.ProviderId == providerId && s.IsActive && s.StartUtc >= earliest && s.StartUtc < horizon)
            .Where(s => !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled
                                             && b.Id != excludeBookingId))
            .OrderBy(s => s.StartUtc)
            .Select(s => new SlotVm { Id = s.Id, StartUtc = s.StartUtc, EndUtc = s.EndUtc })
            .ToListAsync(ct);

        return slots
            .GroupBy(s => DateOnly.FromDateTime(tz.ToLocal(s.StartUtc)))
            .Select(g => new SlotDayVm { Date = g.Key, Slots = g.ToList() })
            .ToList();
    }

    // ------------------------------------------------------------------ create (stored procedure)

    public async Task<ServiceResult<int>> CreateAsync(int customerId, CreateBookingCommand command, CancellationToken ct = default)
    {
        var bookingId = new SqlParameter("@BookingId", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resultCode = new SqlParameter("@ResultCode", SqlDbType.Int) { Direction = ParameterDirection.Output };

        // Parameterised call - user input never becomes part of the SQL text.
        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.CreateBooking @CustomerId, @ProviderId, @ServiceId, @TimeSlotId, @Address, @City, @Notes, " +
            "@CommissionRate, @MinLeadMinutes, @NowUtc, @BookingId OUTPUT, @ResultCode OUTPUT",
            [
                new SqlParameter("@CustomerId", customerId),
                new SqlParameter("@ProviderId", command.ProviderId),
                new SqlParameter("@ServiceId", command.ServiceId),
                new SqlParameter("@TimeSlotId", command.TimeSlotId),
                new SqlParameter("@Address", SqlDbType.NVarChar, 300) { Value = command.Address.Trim() },
                new SqlParameter("@City", SqlDbType.NVarChar, 100) { Value = command.City.Trim() },
                new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object?)command.Notes?.Trim() ?? DBNull.Value },
                new SqlParameter("@CommissionRate", SqlDbType.Decimal) { Precision = 5, Scale = 4, Value = _options.CommissionRate },
                new SqlParameter("@MinLeadMinutes", _options.MinBookingLeadMinutes),
                new SqlParameter("@NowUtc", SqlDbType.DateTime2) { Value = tz.UtcNow },
                bookingId,
                resultCode
            ], ct);

        var code = resultCode.Value is int c ? c : -1;
        if (code != 0)
        {
            logger.LogInformation("CreateBooking rejected for customer {CustomerId} slot {SlotId}: code {Code}", customerId, command.TimeSlotId, code);
            return code switch
            {
                1 => ServiceResult<int>.Forbidden("Your account cannot make bookings right now."),
                2 => ServiceResult<int>.Fail("This professional is not accepting bookings at the moment."),
                3 => ServiceResult<int>.Fail("The selected service is not available from this professional."),
                4 => ServiceResult<int>.NotFound("The selected time slot does not exist."),
                5 => ServiceResult<int>.Fail($"This slot starts too soon. Choose a slot at least {_options.MinBookingLeadMinutes} minutes from now."),
                6 => ServiceResult<int>.Conflict("Sorry, someone just booked this slot. Please choose another time."),
                7 => ServiceResult<int>.Fail("You cannot book your own services."),
                8 => ServiceResult<int>.Conflict("You already have another booking at this time."),
                _ => ServiceResult<int>.Fail("The booking could not be created.")
            };
        }

        var id = (int)bookingId.Value;
        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Customer).Include(b => b.Service).Include(b => b.Provider)
            .FirstAsync(b => b.Id == id, ct);

        await notifications.NotifyAsync(booking.Provider.UserId, "New booking request",
            $"{booking.Customer.FullName} requested {booking.Service.Title} on {tz.Format(booking.ScheduledStartUtc)}.",
            NotificationType.Booking, $"/Booking/Details/{id}", ct);
        await notifications.NotifyAsync(customerId, "Booking request sent",
            $"Your request {booking.Reference} for {booking.Service.Title} is waiting for the professional to accept.",
            NotificationType.Booking, $"/Booking/Details/{id}", ct);
        await activity.LogAsync(customerId, "Booking created",
            $"{booking.Customer.FullName} booked {booking.Service.Title} ({booking.Reference})", "bi-calendar-plus", ct);

        return ServiceResult<int>.Ok(id);
    }

    // ------------------------------------------------------------------ provider actions

    public Task<ServiceResult> AcceptAsync(int providerUserId, int bookingId, CancellationToken ct = default) =>
        ChangeAsProviderAsync(providerUserId, bookingId, ct, (b, now) =>
        {
            var check = BookingPolicy.CanRespond(b, now);
            if (!check.Succeeded) return check;
            b.Status = BookingStatus.Confirmed;
            b.ConfirmedAtUtc = now;
            b.StatusReason = null;
            return ServiceResult.Ok();
        }, b => ("Booking confirmed", $"{b.Reference} for {b.Service.Title} on {tz.Format(b.ScheduledStartUtc)} is confirmed.", "bi-calendar-check"));

    public Task<ServiceResult> RejectAsync(int providerUserId, int bookingId, string? reason, CancellationToken ct = default) =>
        ChangeAsProviderAsync(providerUserId, bookingId, ct, (b, now) =>
        {
            var check = BookingPolicy.CanRespond(b, now);
            if (!check.Succeeded) return check;
            b.Status = BookingStatus.Rejected;
            b.StatusReason = string.IsNullOrWhiteSpace(reason) ? "The professional is unavailable for this slot." : reason.Trim().Truncate(500);
            return ServiceResult.Ok();
        }, b => ("Booking declined", $"{b.Reference} was declined: {b.StatusReason}", "bi-x-octagon"));

    public Task<ServiceResult> CompleteAsync(int providerUserId, int bookingId, CancellationToken ct = default) =>
        ChangeAsProviderAsync(providerUserId, bookingId, ct, (b, now) =>
        {
            var check = BookingPolicy.CanComplete(b, now, _options);
            if (!check.Succeeded) return check;
            b.Status = BookingStatus.Completed;
            b.CompletedAtUtc = now;
            return ServiceResult.Ok();
        }, b => ("Job completed", $"{b.Service.Title} ({b.Reference}) is complete. Share your experience with a review.", "bi-check2-circle"));

    private async Task<ServiceResult> ChangeAsProviderAsync(
        int providerUserId, int bookingId, CancellationToken ct,
        Func<Booking, DateTime, ServiceResult> mutate,
        Func<Booking, (string Title, string Message, string Icon)> describe)
    {
        var booking = await db.Bookings
            .Include(b => b.Provider).ThenInclude(p => p.User)
            .Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return ServiceResult.NotFound("Booking not found.");
        if (booking.Provider.UserId != providerUserId)
            return ServiceResult.Forbidden("Only the assigned professional can update this booking.");

        var now = tz.UtcNow;
        var result = mutate(booking, now);
        if (!result.Succeeded) return result;
        booking.UpdatedAtUtc = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("This booking was changed by someone else a moment ago. Refresh and try again.");
        }

        var (title, message, icon) = describe(booking);
        await notifications.NotifyAsync(booking.CustomerId, title, message, NotificationType.Booking, $"/Booking/Details/{booking.Id}", ct);
        await activity.LogAsync(providerUserId, title, $"{booking.Provider.User.FullName}: {booking.Reference} is now {booking.Status}", icon, ct);
        return ServiceResult.Ok();
    }

    // ------------------------------------------------------------------ customer / admin actions

    public async Task<ServiceResult> CancelAsync(int userId, int bookingId, string? reason, bool asAdmin, CancellationToken ct = default)
    {
        var booking = await db.Bookings
            .Include(b => b.Provider).ThenInclude(p => p.User)
            .Include(b => b.Customer)
            .Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return ServiceResult.NotFound("Booking not found.");

        var now = tz.UtcNow;
        if (asAdmin)
        {
            if (!booking.IsActive) return ServiceResult.Fail("Only pending or confirmed bookings can be cancelled.");
        }
        else
        {
            if (booking.CustomerId != userId)
                return ServiceResult.Forbidden("Only the customer who made this booking can cancel it.");
            var check = BookingPolicy.CanCustomerCancel(booking, now, _options);
            if (!check.Succeeded) return check;
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAtUtc = now;
        booking.UpdatedAtUtc = now;
        booking.CancelledBy = asAdmin ? AppRoles.Admin : AppRoles.Customer;
        booking.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().Truncate(500);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("This booking was changed by someone else a moment ago. Refresh and try again.");
        }

        var who = asAdmin ? "the platform team" : booking.Customer.FullName;
        await notifications.NotifyAsync(booking.Provider.UserId, "Booking cancelled",
            $"{booking.Reference} on {tz.Format(booking.ScheduledStartUtc)} was cancelled by {who}. The slot is open again.",
            NotificationType.Booking, $"/Booking/Details/{booking.Id}", ct);
        if (asAdmin)
        {
            await notifications.NotifyAsync(booking.CustomerId, "Booking cancelled",
                $"{booking.Reference} was cancelled by the platform team. {booking.StatusReason}",
                NotificationType.Booking, $"/Booking/Details/{booking.Id}", ct);
        }
        await activity.LogAsync(userId, "Booking cancelled", $"{booking.Reference} cancelled by {who}", "bi-slash-circle", ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RescheduleAsync(int customerId, int bookingId, int newTimeSlotId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var booking = await db.Bookings
                .Include(b => b.Provider).ThenInclude(p => p.User)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking is null) return ServiceResult.NotFound("Booking not found.");
            if (booking.CustomerId != customerId) return ServiceResult.Forbidden("Only the customer who made this booking can reschedule it.");

            var now = tz.UtcNow;
            var check = BookingPolicy.CanReschedule(booking, now, _options);
            if (!check.Succeeded) return check;
            if (booking.TimeSlotId == newTimeSlotId) return ServiceResult.Fail("Choose a different time slot.");

            var slot = await db.TimeSlots.FirstOrDefaultAsync(s => s.Id == newTimeSlotId && s.ProviderId == booking.ProviderId && s.IsActive, ct);
            if (slot is null) return ServiceResult.NotFound("The selected time slot does not exist.");
            if (slot.StartUtc < now.AddMinutes(_options.MinBookingLeadMinutes))
                return ServiceResult.Fail("The new slot starts too soon.");

            // Re-check availability on the server inside the serializable transaction.
            var taken = await db.Bookings.AnyAsync(b => b.TimeSlotId == newTimeSlotId
                && b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled, ct);
            if (taken) return ServiceResult.Conflict("Sorry, that slot was just booked. Please choose another time.");

            var clash = await db.Bookings.AnyAsync(b => b.CustomerId == customerId && b.Id != bookingId
                && ActiveStatuses.Contains(b.Status) && b.ScheduledStartUtc < slot.EndUtc && b.ScheduledEndUtc > slot.StartUtc, ct);
            if (clash) return ServiceResult.Conflict("You already have another booking at that time.");

            var oldStart = booking.ScheduledStartUtc;
            booking.TimeSlotId = slot.Id;
            booking.ScheduledStartUtc = slot.StartUtc;
            booking.ScheduledEndUtc = slot.EndUtc;
            booking.RescheduleCount++;
            booking.UpdatedAtUtc = now;
            // A new time needs the professional's agreement again.
            booking.Status = BookingStatus.Pending;
            booking.ConfirmedAtUtc = null;
            booking.StatusReason = $"Rescheduled from {tz.Format(oldStart)}";

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await notifications.NotifyAsync(booking.Provider.UserId, "Booking rescheduled",
                $"{booking.Reference} moved from {tz.Format(oldStart)} to {tz.Format(slot.StartUtc)}. Please accept the new time.",
                NotificationType.Booking, $"/Booking/Details/{booking.Id}", ct);
            await activity.LogAsync(customerId, "Booking rescheduled", $"{booking.Reference} moved to {tz.Format(slot.StartUtc)}", "bi-arrow-repeat", ct);
            return ServiceResult.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("This booking was changed by someone else a moment ago. Refresh and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 })
        {
            return ServiceResult.Conflict("Sorry, that slot was just booked. Please choose another time.");
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            // Deadlock victim under SERIALIZABLE - another reschedule won the race.
            return ServiceResult.Conflict("That slot is being booked by someone else right now. Please try again.");
        }
    }

    // ------------------------------------------------------------------ queries

    public async Task<ServiceResult<BookingDetailsViewModel>> GetDetailsAsync(int bookingId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Customer)
            .Include(b => b.Provider).ThenInclude(p => p.User)
            .Include(b => b.Provider).ThenInclude(p => p.Category)
            .Include(b => b.Service)
            .Include(b => b.Review)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return ServiceResult<BookingDetailsViewModel>.NotFound("Booking not found.");

        BookingScope scope;
        if (booking.CustomerId == userId) scope = BookingScope.Customer;
        else if (booking.Provider.UserId == userId) scope = BookingScope.Provider;
        else if (isAdmin) scope = BookingScope.Admin;
        else return ServiceResult<BookingDetailsViewModel>.Forbidden("You do not have access to this booking.");

        var now = tz.UtcNow;
        var cancel = BookingPolicy.CanCustomerCancel(booking, now, _options);
        return ServiceResult<BookingDetailsViewModel>.Ok(new BookingDetailsViewModel
        {
            Booking = booking,
            ViewerScope = scope,
            CanCancel = scope == BookingScope.Customer ? cancel.Succeeded : scope == BookingScope.Admin && booking.IsActive,
            CancelBlockedReason = scope == BookingScope.Customer && booking.IsActive && !cancel.Succeeded ? cancel.Error : null,
            CanReschedule = scope == BookingScope.Customer && BookingPolicy.CanReschedule(booking, now, _options).Succeeded,
            CanReview = scope == BookingScope.Customer && BookingPolicy.CanReview(booking, booking.Review is not null).Succeeded,
            CanRespond = scope == BookingScope.Provider && BookingPolicy.CanRespond(booking, now).Succeeded,
            CanComplete = scope == BookingScope.Provider && BookingPolicy.CanComplete(booking, now, _options).Succeeded
        });
    }

    public async Task<PagedResult<BookingListItemVm>> QueryAsync(BookingScope scope, int? userId, BookingQuery query, CancellationToken ct = default)
    {
        var now = tz.UtcNow;
        var q = ScopedQuery(scope, userId);

        q = (query.Status ?? "all").ToLowerInvariant() switch
        {
            "upcoming" => q.Where(b => ActiveStatuses.Contains(b.Status) && b.ScheduledEndUtc >= now),
            "history" => q.Where(b => !ActiveStatuses.Contains(b.Status) || b.ScheduledEndUtc < now),
            "pending" => q.Where(b => b.Status == BookingStatus.Pending),
            "confirmed" => q.Where(b => b.Status == BookingStatus.Confirmed),
            "completed" => q.Where(b => b.Status == BookingStatus.Completed),
            "cancelled" => q.Where(b => b.Status == BookingStatus.Cancelled),
            "rejected" => q.Where(b => b.Status == BookingStatus.Rejected),
            "unreviewed" => q.Where(b => b.Status == BookingStatus.Completed && b.Review == null),
            _ => q
        };

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            var idTerm = term.StartsWith("LSM-", StringComparison.OrdinalIgnoreCase) ? term[4..] : term;
            var hasId = int.TryParse(idTerm, out var bookingId);
            q = q.Where(b => (hasId && b.Id == bookingId)
                             || b.Service.Title.Contains(term)
                             || b.Customer.FullName.Contains(term)
                             || b.Provider.User.FullName.Contains(term)
                             || b.City.Contains(term));
        }
        if (query.From.HasValue)
        {
            var fromUtc = tz.LocalDayRangeUtc(query.From.Value).StartUtc;
            q = q.Where(b => b.ScheduledStartUtc >= fromUtc);
        }
        if (query.To.HasValue)
        {
            var toUtc = tz.LocalDayRangeUtc(query.To.Value).EndUtc;
            q = q.Where(b => b.ScheduledStartUtc < toUtc);
        }

        var status = (query.Status ?? "").ToLowerInvariant();
        q = status switch
        {
            "upcoming" or "pending" or "confirmed" => q.OrderBy(b => b.ScheduledStartUtc).ThenBy(b => b.Id),
            "recent" => q.OrderByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.Id),
            _ => q.OrderByDescending(b => b.ScheduledStartUtc).ThenByDescending(b => b.Id)
        };

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 5, 50);
        var total = await q.CountAsync(ct);
        var items = await Project(q.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        ApplyPermissions(items, scope, now);

        return new PagedResult<BookingListItemVm> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<IReadOnlyDictionary<BookingStatus, int>> CountByStatusAsync(BookingScope scope, int? userId, CancellationToken ct = default)
    {
        var counts = await ScopedQuery(scope, userId)
            .GroupBy(b => b.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return Enum.GetValues<BookingStatus>().ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0);
    }

    /// <summary>Called by the background service: pending requests whose start time passed are auto-cancelled.</summary>
    public async Task<int> ExpireStalePendingBookingsAsync(CancellationToken ct = default)
    {
        var now = tz.UtcNow;
        var stale = await db.Bookings.Include(b => b.Service)
            .Where(b => b.Status == BookingStatus.Pending && b.ScheduledStartUtc <= now)
            .Take(100).ToListAsync(ct);
        foreach (var b in stale)
        {
            b.Status = BookingStatus.Cancelled;
            b.CancelledBy = "System";
            b.CancelledAtUtc = now;
            b.UpdatedAtUtc = now;
            b.StatusReason = "Expired: the professional did not respond before the appointment time.";
        }
        if (stale.Count == 0) return 0;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0; // a provider acted at the same moment; try again next cycle
        }

        foreach (var b in stale)
        {
            await notifications.NotifyAsync(b.CustomerId, "Booking request expired",
                $"{b.Reference} for {b.Service.Title} expired because it was not accepted in time.",
                NotificationType.Booking, $"/Booking/Details/{b.Id}", ct);
        }
        return stale.Count;
    }

    // ------------------------------------------------------------------ helpers

    private IQueryable<Booking> ScopedQuery(BookingScope scope, int? userId) => scope switch
    {
        BookingScope.Customer => db.Bookings.AsNoTracking().Where(b => b.CustomerId == userId),
        BookingScope.Provider => db.Bookings.AsNoTracking().Where(b => b.Provider.UserId == userId),
        _ => db.Bookings.AsNoTracking()
    };

    /// <summary>Projection = SQL JOIN across Bookings, Users, Providers, Services, Categories and Reviews.</summary>
    private static IQueryable<BookingListItemVm> Project(IQueryable<Booking> q) => q.Select(b => new BookingListItemVm
    {
        Id = b.Id,
        CustomerId = b.CustomerId,
        CustomerName = b.Customer.FullName,
        CustomerPhone = b.Customer.PhoneNumber,
        ProviderId = b.ProviderId,
        ProviderName = b.Provider.User.FullName,
        ServiceTitle = b.Service.Title,
        CategoryName = b.Service.Category.Name,
        StartUtc = b.ScheduledStartUtc,
        EndUtc = b.ScheduledEndUtc,
        Price = b.Price,
        ProviderEarning = b.ProviderEarning,
        Status = b.Status,
        StatusReason = b.StatusReason,
        City = b.City,
        Address = b.Address,
        HasReview = b.Review != null,
        RescheduleCount = b.RescheduleCount,
        CreatedAtUtc = b.CreatedAtUtc,
        IsSampleData = b.IsSampleData
    });

    private void ApplyPermissions(IEnumerable<BookingListItemVm> items, BookingScope scope, DateTime now)
    {
        foreach (var i in items)
        {
            var shadow = new Booking
            {
                Status = i.Status, ScheduledStartUtc = i.StartUtc, ScheduledEndUtc = i.EndUtc, RescheduleCount = i.RescheduleCount
            };
            i.CanCancel = scope == BookingScope.Customer ? BookingPolicy.CanCustomerCancel(shadow, now, _options).Succeeded
                        : scope == BookingScope.Admin && shadow.IsActive;
            i.CanReschedule = scope == BookingScope.Customer && BookingPolicy.CanReschedule(shadow, now, _options).Succeeded;
            i.CanReview = scope == BookingScope.Customer && BookingPolicy.CanReview(shadow, i.HasReview).Succeeded;
            i.CanRespond = scope == BookingScope.Provider && BookingPolicy.CanRespond(shadow, now).Succeeded;
            i.CanComplete = scope == BookingScope.Provider && BookingPolicy.CanComplete(shadow, now, _options).Succeeded;
        }
    }
}
