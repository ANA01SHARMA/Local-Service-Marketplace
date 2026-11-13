using LocalServicesMarketplace.Data;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Services;

public class ProviderService(
    ApplicationDbContext db,
    IBookingService bookings,
    IFileStorageService files,
    INotificationService notifications,
    IActivityLogger activity,
    ITimeZoneService tz,
    IOptions<MarketplaceOptions> options) : IProviderService
{
    private readonly MarketplaceOptions _options = options.Value;
    private const int MaxDocuments = 10;
    private const int MaxSlotsPerRequest = 500;

    // ------------------------------------------------------------------ discovery

    public async Task<PagedResult<ProviderCardVm>> SearchAsync(ProviderSearchQuery query, CancellationToken ct = default)
    {
        var earliest = tz.UtcNow.AddMinutes(_options.MinBookingLeadMinutes);
        var q = PublicProviders();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(p => p.User.FullName.Contains(term)
                             || (p.Headline != null && p.Headline.Contains(term))
                             || (p.Skills != null && p.Skills.Contains(term))
                             || p.Category.Name.Contains(term)
                             || p.Services.Any(s => s.IsActive && s.Title.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var slug = query.Category.Trim().ToLowerInvariant();
            q = q.Where(p => p.Category.Slug == slug || p.Services.Any(s => s.IsActive && s.Category.Slug == slug));
        }
        if (!string.IsNullOrWhiteSpace(query.City))
        {
            var city = query.City.Trim();
            q = q.Where(p => p.City == city);
        }
        if (query.MinPrice.HasValue || query.MaxPrice.HasValue)
        {
            var min = query.MinPrice ?? 0;
            var max = query.MaxPrice ?? decimal.MaxValue;
            q = q.Where(p => p.Services.Any(s => s.IsActive && s.Price >= min && s.Price <= max));
        }
        if (query.MinRating.HasValue)
        {
            var rating = query.MinRating.Value;
            q = q.Where(p => p.AverageRating >= rating);
        }
        if (query.AvailableOn.HasValue)
        {
            var (dayStart, dayEnd) = tz.LocalDayRangeUtc(query.AvailableOn.Value);
            var from = dayStart > earliest ? dayStart : earliest;
            q = q.Where(p => p.IsAcceptingBookings && p.TimeSlots.Any(s => s.IsActive && s.StartUtc >= from && s.StartUtc < dayEnd
                && !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled)));
        }

        var projected = ProjectCards(q, earliest);
        projected = (query.Sort ?? "rating") switch
        {
            "price_asc" => projected.OrderBy(p => p.StartingPrice ?? decimal.MaxValue).ThenByDescending(p => p.Rating),
            "price_desc" => projected.OrderByDescending(p => p.StartingPrice).ThenByDescending(p => p.Rating),
            "experience" => projected.OrderByDescending(p => p.ExperienceYears).ThenByDescending(p => p.Rating),
            "reviews" => projected.OrderByDescending(p => p.Reviews).ThenByDescending(p => p.Rating),
            "soonest" => projected.OrderBy(p => p.NextSlotUtc == null).ThenBy(p => p.NextSlotUtc),
            _ => projected.OrderByDescending(p => p.Rating).ThenByDescending(p => p.Reviews)
        };

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 3, 30);
        var total = await projected.CountAsync(ct);
        var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ProviderCardVm> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<IReadOnlyList<ProviderCardVm>> GetCardsAsync(IEnumerable<int> providerIds, CancellationToken ct = default)
    {
        var ids = providerIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var earliest = tz.UtcNow.AddMinutes(_options.MinBookingLeadMinutes);
        var cards = await ProjectCards(PublicProviders().Where(p => ids.Contains(p.Id)), earliest).ToListAsync(ct);
        return cards.OrderBy(c => ids.IndexOf(c.Id)).ToList();
    }

    public async Task<IReadOnlyList<NextSlotVm>> GetNextAvailableAsync(int take, CancellationToken ct = default)
    {
        var earliest = tz.UtcNow.AddMinutes(_options.MinBookingLeadMinutes);
        var rows = await PublicProviders()
            .Where(p => p.IsAcceptingBookings)
            .Select(p => new
            {
                p.Id, Name = p.User.FullName, Category = p.Category.Name, p.City, p.AverageRating, p.TotalReviews,
                p.User.ProfileImagePath,
                Price = p.Services.Where(s => s.IsActive).Min(s => (decimal?)s.Price),
                Next = p.TimeSlots.Where(s => s.IsActive && s.StartUtc >= earliest
                        && !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled))
                    .Min(s => (DateTime?)s.StartUtc)
            })
            .Where(x => x.Next != null)
            .OrderBy(x => x.Next).ThenByDescending(x => x.AverageRating)
            .Take(take * 3)
            .ToListAsync(ct);

        // Prefer variety: one card per category.
        return rows.GroupBy(r => r.Category).Select(g => g.First()).Take(take)
            .Select(r => new NextSlotVm
            {
                ProviderId = r.Id, ProviderName = r.Name, CategoryName = r.Category, City = r.City,
                Rating = r.AverageRating, Reviews = r.TotalReviews, ImagePath = r.ProfileImagePath,
                StartUtc = DateTime.SpecifyKind(r.Next!.Value, DateTimeKind.Utc), StartingPrice = r.Price
            }).ToList();
    }

    public async Task<PagedResult<Models.Api.ServiceDto>> SearchServicesAsync(Models.Api.ServiceSearchQuery query, CancellationToken ct = default)
    {
        var q = db.Services.AsNoTracking()
            .Where(s => s.IsActive && s.Provider.VerificationStatus == VerificationStatus.Approved && !s.Provider.User.IsBlocked);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(s => s.Title.Contains(term) || (s.Description != null && s.Description.Contains(term)) || s.Category.Name.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.Category)) q = q.Where(s => s.Category.Slug == query.Category);
        if (!string.IsNullOrWhiteSpace(query.City)) q = q.Where(s => s.Provider.City == query.City);
        if (query.MinPrice.HasValue) q = q.Where(s => s.Price >= query.MinPrice);
        if (query.MaxPrice.HasValue) q = q.Where(s => s.Price <= query.MaxPrice);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var total = await q.CountAsync(ct);
        var items = await ProjectServices(q.OrderBy(s => s.Category.DisplayOrder).ThenBy(s => s.Price).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new PagedResult<Models.Api.ServiceDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<Models.Api.ServiceDto?> GetServiceAsync(int serviceId, int? viewerUserId, bool viewerIsAdmin, CancellationToken ct = default)
    {
        var q = db.Services.AsNoTracking().Where(s => s.Id == serviceId);
        if (!viewerIsAdmin)
        {
            q = q.Where(s => (s.IsActive && s.Provider.VerificationStatus == VerificationStatus.Approved && !s.Provider.User.IsBlocked)
                             || s.Provider.UserId == viewerUserId);
        }
        return await ProjectServices(q).FirstOrDefaultAsync(ct);
    }

    private static IQueryable<Models.Api.ServiceDto> ProjectServices(IQueryable<Service> q) => q.Select(s => new Models.Api.ServiceDto
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        Price = s.Price,
        PriceUnit = s.PriceUnit,
        DurationMinutes = s.DurationMinutes,
        IsActive = s.IsActive,
        CategoryId = s.CategoryId,
        CategoryName = s.Category.Name,
        ProviderId = s.ProviderId,
        ProviderName = s.Provider.User.FullName,
        City = s.Provider.City,
        ProviderRating = s.Provider.AverageRating
    });

    public async Task<ProviderDetailsViewModel?> GetDetailsAsync(int providerId, int? viewerUserId, bool viewerIsAdmin, CancellationToken ct = default)
    {
        var provider = await db.Providers.AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == providerId, ct);
        if (provider is null) return null;

        var isOwner = viewerUserId.HasValue && provider.UserId == viewerUserId;
        var isPublic = provider.IsApproved && !provider.User.IsBlocked;
        if (!isPublic && !isOwner && !viewerIsAdmin) return null;

        var services = await db.Services.AsNoTracking().Include(s => s.Category)
            .Where(s => s.ProviderId == providerId && s.IsActive)
            .OrderBy(s => s.Price).ToListAsync(ct);

        var reviews = await db.Reviews.AsNoTracking()
            .Where(r => r.ProviderId == providerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(12)
            .Select(r => new ReviewVm
            {
                Id = r.Id, CustomerName = r.Customer.FullName, Rating = r.Rating, Comment = r.Comment,
                CreatedAtUtc = r.CreatedAtUtc, ServiceTitle = r.Booking.Service.Title, BookingId = r.BookingId,
                ProviderId = r.ProviderId, IsSampleData = r.IsSampleData
            }).ToListAsync(ct);

        var breakdown = await db.Reviews.Where(r => r.ProviderId == providerId)
            .GroupBy(r => r.Rating).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var ratingBreakdown = new int[5];
        foreach (var b in breakdown) ratingBreakdown[b.Key - 1] = b.Count;

        var completed = await db.Bookings.CountAsync(b => b.ProviderId == providerId && b.Status == BookingStatus.Completed, ct);
        var slotDays = isPublic ? await bookings.GetAvailableSlotsAsync(providerId, 14, null, ct) : [];

        return new ProviderDetailsViewModel
        {
            Provider = provider,
            Services = services,
            Reviews = reviews,
            RatingBreakdown = ratingBreakdown,
            SlotDays = slotDays,
            CompletedJobs = completed,
            IsPreview = !isPublic,
            ViewerCanBook = isPublic && provider.IsAcceptingBookings && !isOwner && services.Count > 0
        };
    }

    public Task<Provider?> GetByUserIdAsync(int userId, CancellationToken ct = default) =>
        db.Providers.Include(p => p.User).Include(p => p.Category).FirstOrDefaultAsync(p => p.UserId == userId, ct);

    // ------------------------------------------------------------------ profile

    public async Task<ServiceResult> UpdateProfileAsync(int userId, ProviderProfileInputModel input, CancellationToken ct = default)
    {
        var provider = await db.Providers.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (provider is null) return ServiceResult.NotFound("Provider profile not found.");
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId && c.IsActive, ct))
            return ServiceResult.Fail("Choose a valid category.");

        if (input.Photo is { Length: > 0 })
        {
            var saved = await files.SaveProfileImageAsync(input.Photo, ct);
            if (!saved.Succeeded) return ServiceResult.Fail(saved.Error!);
            files.DeletePublicFile(provider.User.ProfileImagePath);
            provider.User.ProfileImagePath = saved.Value;
        }

        provider.User.FullName = input.FullName.Trim();
        provider.User.PhoneNumber = input.PhoneNumber?.Trim();
        provider.User.City = input.City.Trim();
        provider.Headline = input.Headline?.Trim();
        provider.Bio = input.Bio?.Trim();
        provider.ExperienceYears = input.ExperienceYears;
        provider.Skills = NormaliseList(input.Skills);
        provider.ServiceAreas = NormaliseList(input.ServiceAreas);
        provider.CategoryId = input.CategoryId;
        provider.City = input.City.Trim();
        provider.IsAcceptingBookings = input.IsAcceptingBookings;
        provider.UpdatedAtUtc = tz.UtcNow;

        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    // ------------------------------------------------------------------ services offered

    public async Task<IReadOnlyList<Service>> GetServicesAsync(int providerId, CancellationToken ct = default) =>
        await db.Services.AsNoTracking().Include(s => s.Category)
            .Where(s => s.ProviderId == providerId)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Title)
            .ToListAsync(ct);

    public async Task<ServiceResult<Service>> SaveServiceAsync(int providerId, ServiceInputModel input, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId && c.IsActive, ct))
            return ServiceResult<Service>.Fail("Choose a valid category.");

        var now = tz.UtcNow;
        Service service;
        if (input.Id is > 0)
        {
            var existing = await db.Services.FirstOrDefaultAsync(s => s.Id == input.Id && s.ProviderId == providerId, ct);
            if (existing is null) return ServiceResult<Service>.NotFound("Service not found.");
            service = existing;
        }
        else
        {
            if (await db.Services.CountAsync(s => s.ProviderId == providerId, ct) >= 25)
                return ServiceResult<Service>.Fail("You can list at most 25 services.");
            service = new Service { ProviderId = providerId, CreatedAtUtc = now };
            db.Services.Add(service);
        }

        service.Title = input.Title.Trim();
        service.CategoryId = input.CategoryId;
        service.Description = input.Description?.Trim();
        service.Price = Math.Round(input.Price, 2);
        service.PriceUnit = input.PriceUnit;
        service.DurationMinutes = input.DurationMinutes;
        service.IsActive = input.IsActive;
        service.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return ServiceResult<Service>.Ok(service);
    }

    public async Task<ServiceResult> DeleteServiceAsync(int providerId, int serviceId, CancellationToken ct = default)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.ProviderId == providerId, ct);
        if (service is null) return ServiceResult.NotFound("Service not found.");

        if (await db.Bookings.AnyAsync(b => b.ServiceId == serviceId, ct))
        {
            service.IsActive = false;
            service.UpdatedAtUtc = tz.UtcNow;
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok(); // kept for booking history, hidden from customers
        }

        db.Services.Remove(service);
        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    // ------------------------------------------------------------------ availability

    public async Task<AvailabilityViewModel> GetAvailabilityAsync(int providerId, DateOnly from, int days, CancellationToken ct = default)
    {
        var (startUtc, _) = tz.LocalDayRangeUtc(from);
        var (_, endUtc) = tz.LocalDayRangeUtc(from.AddDays(days - 1));
        var now = tz.UtcNow;

        var rows = await db.TimeSlots.AsNoTracking()
            .Where(s => s.ProviderId == providerId && s.StartUtc >= startUtc && s.StartUtc < endUtc)
            .Select(s => new
            {
                s.Id, s.StartUtc, s.EndUtc, s.IsActive,
                Booking = s.Bookings
                    .Where(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)
                    .OrderByDescending(b => b.Id)
                    .Select(b => new { b.Id, b.Status, b.Customer.FullName })
                    .FirstOrDefault()
            })
            .Where(s => s.IsActive || s.Booking != null)
            .OrderBy(s => s.StartUtc)
            .ToListAsync(ct);

        var slots = rows.Select(r => new ProviderSlotVm
        {
            Id = r.Id, StartUtc = r.StartUtc, EndUtc = r.EndUtc, IsActive = r.IsActive,
            BookingId = r.Booking?.Id, BookingStatus = r.Booking?.Status, CustomerName = r.Booking?.FullName,
            IsPast = r.StartUtc < now
        }).ToList();

        var daysVm = slots.GroupBy(s => DateOnly.FromDateTime(tz.ToLocal(s.StartUtc)))
            .Select(g => new AvailabilityDayVm { Date = g.Key, Slots = g.ToList() })
            .ToList();

        return new AvailabilityViewModel
        {
            Days = daysVm,
            From = from,
            TotalFree = slots.Count(s => s.BookingId is null && !s.IsPast),
            TotalBooked = slots.Count(s => s.BookingStatus is BookingStatus.Pending or BookingStatus.Confirmed),
            AddSlots = new AddSlotsInputModel
            {
                FromDate = tz.LocalToday.AddDays(1),
                ToDate = tz.LocalToday.AddDays(7),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(17, 0),
                Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]
            }
        };
    }

    public async Task<ServiceResult<int>> AddSlotsAsync(int providerId, AddSlotsInputModel input, CancellationToken ct = default)
    {
        var now = tz.UtcNow;
        var candidates = new List<(DateTime Start, DateTime End)>();
        var firstMinute = (int)input.StartTime!.Value.ToTimeSpan().TotalMinutes;
        var lastMinute = (int)input.EndTime!.Value.ToTimeSpan().TotalMinutes;
        for (var date = input.FromDate!.Value; date <= input.ToDate!.Value; date = date.AddDays(1))
        {
            if (!input.Days.Contains(date.DayOfWeek)) continue;
            for (var minute = firstMinute; minute + input.SlotMinutes <= lastMinute; minute += input.SlotMinutes)
            {
                var start = tz.ToUtc(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute));
                if (start <= now) continue;
                candidates.Add((start, start.AddMinutes(input.SlotMinutes)));
            }
        }
        if (candidates.Count == 0) return ServiceResult<int>.Fail("No future slots match those dates and times.");
        if (candidates.Count > MaxSlotsPerRequest) return ServiceResult<int>.Fail($"That would create {candidates.Count} slots. Add at most {MaxSlotsPerRequest} at a time.");

        var rangeStart = candidates.Min(c => c.Start);
        var rangeEnd = candidates.Max(c => c.End);
        var existing = await db.TimeSlots
            .Where(s => s.ProviderId == providerId && s.StartUtc < rangeEnd && s.EndUtc > rangeStart)
            .ToListAsync(ct);

        var added = 0;
        foreach (var (start, end) in candidates)
        {
            var sameStart = existing.FirstOrDefault(s => s.StartUtc == start);
            if (sameStart is not null)
            {
                if (!sameStart.IsActive && sameStart.EndUtc == end && !existing.Any(o => o.IsActive && o.StartUtc < end && o.EndUtc > start))
                {
                    sameStart.IsActive = true; // re-open a previously removed slot
                    added++;
                }
                continue;
            }
            if (existing.Any(s => s.IsActive && s.StartUtc < end && s.EndUtc > start)) continue; // overlap

            var slot = new TimeSlot { ProviderId = providerId, StartUtc = start, EndUtc = end, IsActive = true, CreatedAtUtc = now };
            db.TimeSlots.Add(slot);
            existing.Add(slot);
            added++;
        }

        if (added == 0) return ServiceResult<int>.Fail("Those slots already exist or overlap with your current availability.");
        await db.SaveChangesAsync(ct);
        return ServiceResult<int>.Ok(added);
    }

    public async Task<ServiceResult> RemoveSlotAsync(int providerId, int slotId, CancellationToken ct = default)
    {
        var slot = await db.TimeSlots.Include(s => s.Bookings).FirstOrDefaultAsync(s => s.Id == slotId && s.ProviderId == providerId, ct);
        if (slot is null) return ServiceResult.NotFound("Time slot not found.");
        if (slot.Bookings.Any(b => b.IsActive))
            return ServiceResult.Conflict("This slot has an active booking. Accept, decline or complete it first.");

        if (slot.Bookings.Count > 0) slot.IsActive = false; // keep for booking history
        else db.TimeSlots.Remove(slot);
        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<int>> RemoveFreeSlotsOnDateAsync(int providerId, DateOnly date, CancellationToken ct = default)
    {
        var (start, end) = tz.LocalDayRangeUtc(date);
        var now = tz.UtcNow;
        var removed = await db.TimeSlots
            .Where(s => s.ProviderId == providerId && s.StartUtc >= start && s.StartUtc < end && s.StartUtc > now && !s.Bookings.Any())
            .ExecuteDeleteAsync(ct);
        var deactivated = await db.TimeSlots
            .Where(s => s.ProviderId == providerId && s.IsActive && s.StartUtc >= start && s.StartUtc < end && s.StartUtc > now
                        && !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
        return ServiceResult<int>.Ok(removed + deactivated);
    }

    // ------------------------------------------------------------------ verification

    public async Task<VerificationViewModel> GetVerificationAsync(int providerId, CancellationToken ct = default)
    {
        var provider = await db.Providers.AsNoTracking().Include(p => p.User).Include(p => p.Category).FirstAsync(p => p.Id == providerId, ct);
        var docs = await db.ProviderVerificationDocuments.AsNoTracking()
            .Where(d => d.ProviderId == providerId).OrderByDescending(d => d.UploadedAtUtc).ToListAsync(ct);
        var (serviceCount, futureSlots) = await CountsAsync(providerId, ct);
        var usableDocs = docs.Count(d => d.Status != DocumentStatus.Rejected);

        return new VerificationViewModel
        {
            Provider = provider,
            Documents = docs,
            Checklist = BuildChecklist(provider, serviceCount, futureSlots, usableDocs),
            CanSubmit = provider.VerificationStatus is VerificationStatus.NotSubmitted or VerificationStatus.Rejected
                        && usableDocs > 0 && serviceCount > 0 && IsProfileComplete(provider)
        };
    }

    public async Task<ServiceResult> UploadDocumentAsync(int providerId, DocumentType type, IFormFile file, CancellationToken ct = default)
    {
        if (await db.ProviderVerificationDocuments.CountAsync(d => d.ProviderId == providerId, ct) >= MaxDocuments)
            return ServiceResult.Fail($"You can upload at most {MaxDocuments} documents. Delete one first.");

        var saved = await files.SavePrivateDocumentAsync(file, ct);
        if (!saved.Succeeded) return ServiceResult.Fail(saved.Error!);

        db.ProviderVerificationDocuments.Add(new ProviderVerificationDocument
        {
            ProviderId = providerId,
            DocumentType = type,
            StoredFileName = saved.Value.StoredName,
            OriginalFileName = Path.GetFileName(file.FileName).Truncate(255),
            ContentType = saved.Value.ContentType,
            FileSizeBytes = file.Length,
            Status = DocumentStatus.Pending,
            UploadedAtUtc = tz.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteDocumentAsync(int providerId, int documentId, CancellationToken ct = default)
    {
        var doc = await db.ProviderVerificationDocuments.Include(d => d.Provider)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ProviderId == providerId, ct);
        if (doc is null) return ServiceResult.NotFound("Document not found.");
        if (doc.Status == DocumentStatus.Approved) return ServiceResult.Fail("Approved documents cannot be deleted.");
        if (doc.Provider.VerificationStatus == VerificationStatus.Pending)
            return ServiceResult.Fail("Documents cannot be removed while your profile is under review.");

        db.ProviderVerificationDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
        files.DeletePrivateDocument(doc.StoredFileName);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SubmitForVerificationAsync(int providerId, CancellationToken ct = default)
    {
        var model = await GetVerificationAsync(providerId, ct);
        if (!model.CanSubmit)
            return ServiceResult.Fail("Complete your profile, add a service and upload at least one document before submitting.");

        var provider = await db.Providers.Include(p => p.User).FirstAsync(p => p.Id == providerId, ct);
        provider.VerificationStatus = VerificationStatus.Pending;
        provider.VerificationSubmittedAtUtc = tz.UtcNow;
        provider.VerificationNote = null;
        provider.UpdatedAtUtc = tz.UtcNow;
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAdminsAsync("Provider awaiting verification",
            $"{provider.User.FullName} submitted their profile for verification.",
            NotificationType.Verification, $"/Admin/ReviewProvider/{provider.Id}", ct);
        await activity.LogAsync(provider.UserId, "Verification submitted", $"{provider.User.FullName} submitted documents for review", "bi-patch-question", ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> OpenDocumentAsync(int documentId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var doc = await db.ProviderVerificationDocuments.AsNoTracking().Include(d => d.Provider)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return ServiceResult<(Stream, string, string)>.NotFound("Document not found.");
        if (!isAdmin && doc.Provider.UserId != userId)
            return ServiceResult<(Stream, string, string)>.Forbidden("You cannot view this document.");

        var stream = files.OpenPrivateDocument(doc.StoredFileName);
        return stream is null
            ? ServiceResult<(Stream, string, string)>.NotFound("The file is missing from storage.")
            : ServiceResult<(Stream, string, string)>.Ok((stream, doc.ContentType, doc.OriginalFileName));
    }

    // ------------------------------------------------------------------ dashboard / earnings

    public async Task<ProviderDashboardViewModel> GetDashboardAsync(int providerId, CancellationToken ct = default)
    {
        var provider = await db.Providers.AsNoTracking().Include(p => p.User).Include(p => p.Category).FirstAsync(p => p.Id == providerId, ct);
        var performance = await db.ProviderPerformance.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderId == providerId, ct);
        var now = tz.UtcNow;
        var monthStart = tz.ToUtc(new DateOnly(tz.LocalToday.Year, tz.LocalToday.Month, 1), TimeOnly.MinValue);

        var pending = await db.Bookings.CountAsync(b => b.ProviderId == providerId && b.Status == BookingStatus.Pending && b.ScheduledStartUtc > now, ct);
        var upcoming = await db.Bookings.CountAsync(b => b.ProviderId == providerId && b.Status == BookingStatus.Confirmed && b.ScheduledEndUtc >= now, ct);
        var thisMonth = await db.Bookings.Where(b => b.ProviderId == providerId && b.Status == BookingStatus.Completed && b.CompletedAtUtc >= monthStart)
            .SumAsync(b => (decimal?)b.ProviderEarning, ct) ?? 0;

        var nextJobs = await bookings.QueryAsync(BookingScope.Provider, provider.UserId, new BookingQuery { Status = "confirmed", PageSize = 5 }, ct);
        var latestRequests = await bookings.QueryAsync(BookingScope.Provider, provider.UserId, new BookingQuery { Status = "pending", PageSize = 5 }, ct);

        var (serviceCount, futureSlots) = await CountsAsync(providerId, ct);
        var docCount = await db.ProviderVerificationDocuments.CountAsync(d => d.ProviderId == providerId && d.Status != DocumentStatus.Rejected, ct);
        var weekEnd = now.AddDays(7);
        var freeSlots = await db.TimeSlots.CountAsync(s => s.ProviderId == providerId && s.IsActive && s.StartUtc > now && s.StartUtc < weekEnd
            && !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled), ct);

        return new ProviderDashboardViewModel
        {
            Provider = provider,
            Performance = performance,
            PendingRequests = pending,
            UpcomingJobs = upcoming,
            ThisMonthEarnings = thisMonth,
            NextJobs = nextJobs.Items.Where(b => b.EndUtc >= now).OrderBy(b => b.StartUtc).ToList(),
            LatestRequests = latestRequests.Items,
            Checklist = BuildChecklist(provider, serviceCount, futureSlots, docCount),
            EarningsTrend = await MonthlyEarningsAsync(providerId, 6, ct),
            FreeSlotsNext7Days = freeSlots
        };
    }

    public async Task<EarningsViewModel> GetEarningsAsync(int providerId, CancellationToken ct = default)
    {
        var now = tz.UtcNow;
        var monthStart = tz.ToUtc(new DateOnly(tz.LocalToday.Year, tz.LocalToday.Month, 1), TimeOnly.MinValue);
        var completed = db.Bookings.Where(b => b.ProviderId == providerId && b.Status == BookingStatus.Completed);

        var earned = await completed.SumAsync(b => (decimal?)b.ProviderEarning, ct) ?? 0;
        var fees = await completed.SumAsync(b => (decimal?)b.PlatformFee, ct) ?? 0;
        var jobs = await completed.CountAsync(ct);

        var provider = await db.Providers.AsNoTracking().FirstAsync(p => p.Id == providerId, ct);
        var recent = await bookings.QueryAsync(BookingScope.Provider, provider.UserId, new BookingQuery { Status = "completed", PageSize = 10 }, ct);

        return new EarningsViewModel
        {
            TotalEarnings = earned,
            TotalFees = fees,
            CompletedJobs = jobs,
            ThisMonth = await completed.Where(b => b.CompletedAtUtc >= monthStart).SumAsync(b => (decimal?)b.ProviderEarning, ct) ?? 0,
            UpcomingValue = await db.Bookings.Where(b => b.ProviderId == providerId && b.Status == BookingStatus.Confirmed && b.ScheduledEndUtc >= now)
                .SumAsync(b => (decimal?)b.ProviderEarning, ct) ?? 0,
            Months = await MonthlyEarningsAsync(providerId, 12, ct),
            RecentPayouts = recent.Items,
            CommissionRate = _options.CommissionRate
        };
    }

    public IReadOnlyList<ProviderChecklistItem> BuildChecklist(Provider provider, int serviceCount, int futureSlots, int documentCount) =>
    [
        new() { Label = "Complete your profile (headline, about and skills)", Done = IsProfileComplete(provider), Action = "/Provider/Profile" },
        new() { Label = "Add at least one service with a price", Done = serviceCount > 0, Action = "/Provider/Services" },
        new() { Label = "Publish time slots customers can book", Done = futureSlots > 0, Action = "/Provider/Availability" },
        new() { Label = "Upload an ID or trade certificate", Done = documentCount > 0, Action = "/Provider/Verification" },
        new() { Label = "Get verified by the Kaarigar team", Done = provider.IsApproved, Action = "/Provider/Verification" }
    ];

    // ------------------------------------------------------------------ helpers

    private IQueryable<Provider> PublicProviders() =>
        db.Providers.AsNoTracking().Where(p => p.VerificationStatus == VerificationStatus.Approved && !p.User.IsBlocked);

    private static IQueryable<ProviderCardVm> ProjectCards(IQueryable<Provider> q, DateTime earliest) => q.Select(p => new ProviderCardVm
    {
        Id = p.Id,
        Name = p.User.FullName,
        Headline = p.Headline,
        CategoryName = p.Category.Name,
        CategorySlug = p.Category.Slug,
        City = p.City,
        Rating = p.AverageRating,
        Reviews = p.TotalReviews,
        ExperienceYears = p.ExperienceYears,
        StartingPrice = p.Services.Where(s => s.IsActive).Min(s => (decimal?)s.Price),
        ImagePath = p.User.ProfileImagePath,
        IsVerified = p.VerificationStatus == VerificationStatus.Approved,
        IsAcceptingBookings = p.IsAcceptingBookings,
        NextSlotUtc = p.TimeSlots.Where(s => s.IsActive && s.StartUtc >= earliest
                && !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled))
            .Min(s => (DateTime?)s.StartUtc),
        Skills = p.Skills,
        IsSampleData = p.IsSampleData
    });

    private async Task<(int Services, int FutureSlots)> CountsAsync(int providerId, CancellationToken ct)
    {
        var now = tz.UtcNow;
        var services = await db.Services.CountAsync(s => s.ProviderId == providerId && s.IsActive, ct);
        var slots = await db.TimeSlots.CountAsync(s => s.ProviderId == providerId && s.IsActive && s.StartUtc > now, ct);
        return (services, slots);
    }

    private async Task<IReadOnlyList<EarningsMonthVm>> MonthlyEarningsAsync(int providerId, int months, CancellationToken ct)
    {
        var today = tz.LocalToday;
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var fromUtc = tz.ToUtc(firstMonth, TimeOnly.MinValue);

        var raw = await db.Bookings
            .Where(b => b.ProviderId == providerId && b.Status == BookingStatus.Completed && b.CompletedAtUtc >= fromUtc)
            .GroupBy(b => new { b.CompletedAtUtc!.Value.Year, b.CompletedAtUtc!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Jobs = g.Count(), Gross = g.Sum(b => b.Price), Fees = g.Sum(b => b.PlatformFee), Net = g.Sum(b => b.ProviderEarning) })
            .ToListAsync(ct);

        return Enumerable.Range(0, months).Select(i =>
        {
            var m = firstMonth.AddMonths(i);
            var row = raw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month);
            return new EarningsMonthVm { Year = m.Year, Month = m.Month, Jobs = row?.Jobs ?? 0, Gross = row?.Gross ?? 0, Fees = row?.Fees ?? 0, Net = row?.Net ?? 0 };
        }).ToList();
    }

    private static bool IsProfileComplete(Provider p) =>
        !string.IsNullOrWhiteSpace(p.Headline) && !string.IsNullOrWhiteSpace(p.Bio) && !string.IsNullOrWhiteSpace(p.Skills);

    private static string? NormaliseList(string? value)
    {
        var items = Provider.SplitList(value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return items.Count == 0 ? null : string.Join(", ", items).Truncate(500);
    }
}
