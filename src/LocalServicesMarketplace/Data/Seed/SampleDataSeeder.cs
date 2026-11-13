using System.Text;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services;
using LocalServicesMarketplace.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Data.Seed;

/// <summary>
/// Development-only demo data. Every row it creates has IsSampleData = true (shown as a "Demo" badge)
/// and every e-mail ends with @demo.lsm.local, so sample data is never confused with live records.
/// Deterministic (fixed random seed) and idempotent (skips if sample users already exist).
/// </summary>
public class SampleDataSeeder(
    ApplicationDbContext db,
    UserManager<User> userManager,
    ITimeZoneService tz,
    IFileStorageService files,
    IOptions<MarketplaceOptions> options,
    IOptions<SeedOptions> seedOptions,
    ILogger<SampleDataSeeder> logger)
{
    // 0 = new random dataset per fresh database (the seed is logged so a dataset can be reproduced).
    private readonly int _seed = seedOptions.Value.RandomSeed != 0 ? seedOptions.Value.RandomSeed : Random.Shared.Next(1, int.MaxValue);
    private Random _rng = Random.Shared;
    private readonly decimal _commission = options.Value.CommissionRate;

    /// <summary>
    /// How a sample provider works: first start minute (:00/:15/:30/:45), slot length suited to the trade,
    /// breaks between jobs, weekly day off and how busy they are. Derived from a stable hash of the e-mail,
    /// so the startup refresh extends calendars in the same pattern.
    /// </summary>
    private sealed record Schedule(int FirstMinute, int LastMinute, int SlotMinutes, int StepMinutes, DayOfWeek DayOff, double Busyness)
    {
        public IEnumerable<TimeOnly> Times()
        {
            for (var m = FirstMinute; m + SlotMinutes <= LastMinute; m += StepMinutes)
                yield return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(m));
        }
    }

    private static Schedule ScheduleFor(SeedData.DemoProvider d)
    {
        var h = StableHash(d.Email);
        var slot = d.Category switch
        {
            "electrical" or "carpentry" => 45,
            "ac-repair" or "beauty-salon" or "pest-control" => 90,
            "home-cleaning" or "painting" => 120,
            _ => 60
        };
        var offset = new[] { 0, 15, 30, 45 }[h % 4];
        var breakMinutes = new[] { 0, 15, 30 }[h / 4 % 3];
        var dayOff = new[] { DayOfWeek.Sunday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Saturday }[h / 12 % 5];
        var busyness = 0.12 + h / 60 % 6 * 0.08; // share of slots already taken / blocked: 12% .. 52%
        return new Schedule(d.StartHour * 60 + offset, d.EndHour * 60, slot, slot + breakMinutes, dayOff, busyness);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var h = 23;
            foreach (var c in value) h = h * 31 + c;
            return h & 0x7fffffff;
        }
    }

    /// <summary>Per-provider "personality" for the generated history.</summary>
    private sealed record Profile(double Quality, int PastJobs, double CancelRate, double RejectRate, double ReviewRate, decimal PriceFactor);

    private Profile NewProfile() => new(
        Quality: 3.3 + _rng.NextDouble() * 1.6,          // mean star rating 3.3 .. 4.9
        PastJobs: _rng.Next(4, 24),
        CancelRate: 0.03 + _rng.NextDouble() * 0.15,
        RejectRate: 0.02 + _rng.NextDouble() * 0.10,
        ReviewRate: 0.55 + _rng.NextDouble() * 0.40,
        PriceFactor: 0.8m + (decimal)_rng.NextDouble() * 0.5m); // -20% .. +30% of the catalogue price

    private int RatingFor(Profile profile)
    {
        // Normal distribution around the provider's quality (Box-Muller), clamped to 1..5 stars.
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return (int)Math.Clamp(Math.Round(profile.Quality + normal * 0.7), 1, 5);
    }

    private string? CommentFor(int rating) =>
        _rng.NextDouble() < 0.15 ? null : SeedData.ReviewComments[rating][_rng.Next(SeedData.ReviewComments[rating].Length)];

    private static decimal PriceFor(decimal catalogue, decimal factor)
    {
        var raw = catalogue * factor;
        // Mix of "...9" prices and round fifties, like real listings.
        var price = raw >= 400 && StableHash(raw.ToString("0.00")) % 3 == 0 ? Math.Round(raw / 50m) * 50m : Math.Round(raw / 10m) * 10m - 1m;
        return Math.Max(49m, price);
    }

    public async Task SeedAsync(string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.IsSampleData, ct))
        {
            await RefreshAsync(ct);
            return;
        }
        _rng = new Random(_seed);
        logger.LogInformation("Seeding sample data with random seed {Seed} (set Seed:RandomSeed to reproduce it)...", _seed);

        var now = tz.UtcNow;
        var today = tz.LocalToday;
        var categories = await db.Categories.ToDictionaryAsync(c => c.Slug, ct);

        // ---------------- customers
        var customers = new List<User>();
        for (var i = 0; i < SeedData.Customers.Length; i++)
        {
            var c = SeedData.Customers[i];
            customers.Add(await CreateUserAsync(c.FullName, c.Email, c.Phone, c.City, c.Address, AppRoles.Customer, password, now.AddDays(-_rng.Next(20, 260)).AddMinutes(-_rng.Next(1440))));
        }

        // ---------------- providers, services, documents, slots
        var providers = new List<(Provider Provider, SeedData.DemoProvider Demo, List<Service> Services)>();
        var profiles = new Dictionary<string, Profile>();
        for (var i = 0; i < SeedData.Providers.Length; i++)
        {
            var d = SeedData.Providers[i];
            var profile = profiles[d.Email] = NewProfile();
            var created = d.Status == VerificationStatus.Approved
                ? now.AddDays(-_rng.Next(120, 330)).AddMinutes(-_rng.Next(1440))
                : now.AddDays(-_rng.Next(3, 40)).AddMinutes(-_rng.Next(1440));
            var user = await CreateUserAsync(d.FullName, d.Email, d.Phone, d.City, null, AppRoles.Provider, password, created);
            var provider = new Provider
            {
                UserId = user.Id,
                CategoryId = categories[d.Category].Id,
                Headline = string.IsNullOrEmpty(d.Headline) ? null : d.Headline,
                Bio = string.IsNullOrEmpty(d.Bio) ? null : d.Bio,
                ExperienceYears = d.Experience,
                Skills = string.IsNullOrEmpty(d.Skills) ? null : d.Skills,
                ServiceAreas = string.IsNullOrEmpty(d.Areas) ? null : d.Areas,
                City = d.City,
                VerificationStatus = d.Status,
                VerificationSubmittedAtUtc = d.Status == VerificationStatus.NotSubmitted ? null : (d.Status == VerificationStatus.Pending ? now.AddHours(-(20 + i * 3)) : created.AddDays(1)),
                VerifiedAtUtc = d.Status == VerificationStatus.Approved ? created.AddDays(2) : null,
                IsAcceptingBookings = true,
                IsSampleData = true,
                CreatedAtUtc = created,
                UpdatedAtUtc = created
            };
            db.Providers.Add(provider);

            var services = new List<Service>();
            if (d.Status != VerificationStatus.NotSubmitted)
            {
                var catalogue = SeedData.ServicesByCategory[d.Category];
                // The two tutors split the tutoring catalogue between them.
                var picks = d.Email == "kavita.rao" ? catalogue[2..] : d.Email == "ananya.iyer" ? catalogue[..2] : catalogue;
                foreach (var s in picks)
                {
                    services.Add(new Service
                    {
                        Provider = provider, CategoryId = categories[d.Category].Id, Title = s.Title, Description = s.Description,
                        Price = PriceFor(s.Price, profile.PriceFactor),
                        PriceUnit = s.Unit, DurationMinutes = s.Minutes, IsActive = true, CreatedAtUtc = created, UpdatedAtUtc = created
                    });
                }
                db.Services.AddRange(services);
                await AddDocumentsAsync(provider, d, created, ct);
                AddFutureSlots(provider, ScheduleFor(d), today, today.AddDays(13), now);
            }
            providers.Add((provider, d, services));
        }
        await db.SaveChangesAsync(ct);

        // ---------------- historical bookings and reviews (up to ~8 months, never before the provider joined)
        var approved = providers.Where(p => p.Demo.Status == VerificationStatus.Approved).ToList();
        foreach (var (provider, demo, services) in approved)
        {
            var profile = profiles[demo.Email];
            var schedule = ScheduleFor(demo);
            var times = schedule.Times().ToArray();
            var oldestDay = Math.Max(3, Math.Min(240, (int)(now - provider.CreatedAtUtc).TotalDays - 3));
            var used = new HashSet<DateTime>(provider.TimeSlots.Select(s => s.StartUtc));
            for (var n = 0; n < profile.PastJobs; n++)
            {
                DateTime start;
                var guard = 0;
                do
                {
                    var date = today.AddDays(-_rng.Next(2, oldestDay));
                    if (date.DayOfWeek == schedule.DayOff) date = date.AddDays(-1);
                    start = tz.ToUtc(date, times[_rng.Next(times.Length)]);
                } while (!used.Add(start) && ++guard < 20);
                if (guard >= 20) continue;

                var customer = PickCustomer(customers, demo.City);
                var service = services[_rng.Next(services.Count)];
                var roll = _rng.NextDouble();
                var status = roll < profile.CancelRate ? BookingStatus.Cancelled
                           : roll < profile.CancelRate + profile.RejectRate ? BookingStatus.Rejected
                           : BookingStatus.Completed;
                var booked = start.AddDays(-_rng.Next(0, 6)).AddMinutes(-_rng.Next(60, 1440));
                var booking = NewBooking(provider, service, customer, start, start.AddMinutes(schedule.SlotMinutes), status, booked);
                db.Bookings.Add(booking);

                if (status == BookingStatus.Completed && _rng.NextDouble() < profile.ReviewRate)
                {
                    var rating = RatingFor(profile);
                    var reviewedAt = booking.CompletedAtUtc!.Value.AddMinutes(_rng.Next(90, 4 * 1440));
                    db.Reviews.Add(new Review
                    {
                        Booking = booking, CustomerId = customer.Id, ProviderId = provider.Id, Rating = rating, Comment = CommentFor(rating),
                        IsSampleData = true, CreatedAtUtc = reviewedAt < now ? reviewedAt : now.AddMinutes(-_rng.Next(5, 120))
                    });
                }
            }
        }
        await db.SaveChangesAsync(ct);

        // ---------------- live-looking upcoming bookings for the demo flow
        var byEmail = providers.ToDictionary(p => p.Demo.Email);
        var busy = new HashSet<(int, DateTime)>();
        var upcoming = new List<Booking>();
        void Upcoming(string providerEmail, int customerIndex, int dayOffset, int hour, BookingStatus status)
        {
            var (provider, _, services) = byEmail[providerEmail];
            var customer = customers[customerIndex];
            var slot = provider.TimeSlots
                .Where(s => s.StartUtc > now.AddHours(3) && DateOnly.FromDateTime(tz.ToLocal(s.StartUtc)) >= today.AddDays(dayOffset)
                            && tz.ToLocal(s.StartUtc).Hour >= hour && !busy.Contains((customer.Id, s.StartUtc))
                            && upcoming.All(b => b.TimeSlot != s))
                .OrderBy(s => s.StartUtc).FirstOrDefault();
            if (slot is null) return;
            busy.Add((customer.Id, slot.StartUtc));
            var booking = NewBooking(provider, services[0], customer, slot.StartUtc, slot.EndUtc, status, now.AddHours(-(2 + upcoming.Count * 5)));
            booking.TimeSlot = slot;
            upcoming.Add(booking);
        }

        Upcoming("rohit.verma", 0, 1, 11, BookingStatus.Pending);
        Upcoming("rohit.verma", 5, 2, 15, BookingStatus.Pending);
        Upcoming("rohit.verma", 2, 3, 10, BookingStatus.Confirmed);
        Upcoming("suresh.yadav", 0, 4, 12, BookingStatus.Confirmed);
        Upcoming("priya.nair", 1, 2, 10, BookingStatus.Pending);
        Upcoming("ananya.iyer", 3, 1, 17, BookingStatus.Confirmed);
        Upcoming("vikram.joshi", 4, 2, 11, BookingStatus.Pending);
        Upcoming("neha.kapoor", 5, 5, 12, BookingStatus.Confirmed);
        Upcoming("deepak.kumar", 2, 6, 9, BookingStatus.Pending);
        db.Bookings.AddRange(upcoming);

        // A confirmed job from yesterday, so "Mark as completed" can be demonstrated straight away.
        var rohit = byEmail["rohit.verma"];
        var rohitSchedule = ScheduleFor(rohit.Demo);
        var yesterday = tz.ToUtc(today.AddDays(-1), rohitSchedule.Times().First(t => t.Hour >= 15));
        var pastSlot = new TimeSlot { Provider = rohit.Provider, StartUtc = yesterday, EndUtc = yesterday.AddMinutes(rohitSchedule.SlotMinutes), IsActive = true, CreatedAtUtc = now.AddDays(-5) };
        if (!rohit.Provider.TimeSlots.Any(s => s.StartUtc == yesterday) && !await db.TimeSlots.AnyAsync(s => s.ProviderId == rohit.Provider.Id && s.StartUtc == yesterday, ct))
        {
            var done = NewBooking(rohit.Provider, rohit.Services[1], customers[0], yesterday, pastSlot.EndUtc, BookingStatus.Confirmed, now.AddDays(-3));
            done.TimeSlot = pastSlot;
            db.Bookings.Add(done);
        }
        await db.SaveChangesAsync(ct);

        // ---------------- complaints, notifications, activity
        await SeedSupportAndActivityAsync(customers, byEmail, now, ct);

        // ---------------- denormalised ratings via the stored procedure
        foreach (var (provider, _, _) in providers)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"EXEC dbo.CalculateProviderAverageRating @ProviderId = {provider.Id}", ct);
        }

        logger.LogInformation("Sample data seeded: {Customers} customers, {Providers} providers, {Bookings} bookings",
            customers.Count, providers.Count, await db.Bookings.CountAsync(ct));
    }

    /// <summary>
    /// Keeps a long-running demo database usable: the seed's times are relative to its first start, so on
    /// every start we (1) top up 14 days of free slots for sample providers and (2) make sure the demo
    /// provider still has booking requests to accept. Idempotent (unique index on ProviderId + StartUtc).
    /// </summary>
    private async Task RefreshAsync(CancellationToken ct)
    {
        var now = tz.UtcNow;
        var today = tz.LocalToday;
        var horizon = tz.LocalDayRangeUtc(today.AddDays(13)).EndUtc;
        var providers = await db.Providers.Include(p => p.User).Include(p => p.Services)
            .Where(p => p.IsSampleData && p.VerificationStatus != VerificationStatus.NotSubmitted).ToListAsync(ct);
        var added = 0;
        foreach (var provider in providers)
        {
            var demo = SeedData.Providers.FirstOrDefault(d => $"{d.Email}@{SeedData.DemoDomain}" == provider.User.Email);
            if (demo is null) continue;
            // Only extend the calendar past its last published day, so existing gaps (busy times) stay.
            var last = await db.TimeSlots.Where(s => s.ProviderId == provider.Id).MaxAsync(s => (DateTime?)s.StartUtc, ct);
            var from = last.HasValue ? DateOnly.FromDateTime(tz.ToLocal(last.Value)).AddDays(1) : today;
            if (from < today) from = today;
            added += AddFutureSlots(provider, ScheduleFor(demo), from, today.AddDays(13), now);
        }
        await db.SaveChangesAsync(ct);

        // The demo provider should always have two requests waiting.
        var rohit = providers.FirstOrDefault(p => p.User.Email == $"rohit.verma@{SeedData.DemoDomain}");
        var requests = 0;
        if (rohit is not null && rohit.Services.Count > 0)
        {
            var pending = await db.Bookings.CountAsync(b => b.ProviderId == rohit.Id && b.Status == BookingStatus.Pending && b.ScheduledStartUtc > now, ct);
            var customers = await db.Users.Where(u => u.IsSampleData && (u.Email == $"aarav.sharma@{SeedData.DemoDomain}" || u.Email == $"pooja.saxena@{SeedData.DemoDomain}"))
                .OrderBy(u => u.Id).ToListAsync(ct);
            var service = rohit.Services.OrderBy(s => s.Id).First(s => s.IsActive);
            foreach (var customer in customers.Skip(Math.Min(pending, customers.Count)))
            {
                var slot = await db.TimeSlots
                    .Where(s => s.ProviderId == rohit.Id && s.IsActive && s.StartUtc > now.AddHours(20)
                                && !s.Bookings.Any(b => b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Cancelled)
                                && !db.Bookings.Any(b => b.CustomerId == customer.Id && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed)
                                                         && b.ScheduledStartUtc < s.EndUtc && b.ScheduledEndUtc > s.StartUtc))
                    .OrderBy(s => s.StartUtc).Skip(requests * 3).FirstOrDefaultAsync(ct);
                if (slot is null) break;
                var (fee, earning) = BookingPolicy.SplitPrice(service.Price, _commission);
                db.Bookings.Add(new Booking
                {
                    CustomerId = customer.Id, ProviderId = rohit.Id, ServiceId = service.Id, TimeSlotId = slot.Id,
                    ScheduledStartUtc = slot.StartUtc, ScheduledEndUtc = slot.EndUtc,
                    Address = customer.Address ?? customer.City ?? "New Delhi", City = customer.City ?? "New Delhi",
                    Price = service.Price, PlatformFee = fee, ProviderEarning = earning, Status = BookingStatus.Pending,
                    CreatedAtUtc = now, UpdatedAtUtc = now, IsSampleData = true
                });
                db.Notifications.Add(new Notification
                {
                    UserId = rohit.UserId, Title = "New booking request", Message = $"{customer.FullName} requested {service.Title}.",
                    Type = NotificationType.Booking, Link = "/Provider/Requests", CreatedAtUtc = now
                });
                requests++;
            }
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("Sample data refreshed: {Slots} new slots, {Requests} new demo requests", added, requests);
    }

    private async Task<User> CreateUserAsync(string name, string emailLocal, string phone, string city, string? address, string role, string password, DateTime created)
    {
        var email = $"{emailLocal}@{SeedData.DemoDomain}";
        var user = new User
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = name, PhoneNumber = phone, City = city,
            Address = address, IsSampleData = true, CreatedAtUtc = created,
            LastLoginAtUtc = _rng.NextDouble() < 0.2 ? null : tz.UtcNow.AddMinutes(-_rng.Next(30, 60 * 24 * 30))
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Seeding {email} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    private Booking NewBooking(Provider provider, Service service, User customer, DateTime start, DateTime end, BookingStatus status, DateTime created)
    {
        var (fee, earning) = BookingPolicy.SplitPrice(service.Price, _commission);
        var booking = new Booking
        {
            Provider = provider, Service = service, CustomerId = customer.Id,
            ScheduledStartUtc = start, ScheduledEndUtc = end,
            Address = customer.Address ?? $"{customer.City} (address shared on confirmation)", City = customer.City ?? provider.City,
            Price = service.Price, PlatformFee = fee, ProviderEarning = earning,
            Status = status, CreatedAtUtc = created, UpdatedAtUtc = created, IsSampleData = true,
            TimeSlot = new TimeSlot { Provider = provider, StartUtc = start, EndUtc = end, IsActive = true, CreatedAtUtc = created.AddDays(-1) }
        };
        switch (status)
        {
            case BookingStatus.Confirmed:
                booking.ConfirmedAtUtc = created.AddHours(1);
                break;
            case BookingStatus.Completed:
                booking.ConfirmedAtUtc = created.AddHours(2);
                booking.CompletedAtUtc = end.AddMinutes(30);
                booking.UpdatedAtUtc = booking.CompletedAtUtc.Value;
                break;
            case BookingStatus.Cancelled:
                booking.CancelledAtUtc = created.AddHours(5);
                booking.CancelledBy = AppRoles.Customer;
                booking.StatusReason = new[] { "Plans changed, will rebook later.", "Fixed it myself in the end.", "Guests arriving that day.", "Booked the wrong date.", null }[_rng.Next(5)];
                break;
            case BookingStatus.Rejected:
                booking.StatusReason = new[] { "Already committed to another job at that time.", "That area is outside my route that day.", "Unwell, sorry.", "Need a site visit before I can quote for this." }[_rng.Next(4)];
                break;
        }
        return booking;
    }

    private User PickCustomer(List<User> customers, string city)
    {
        var local = customers.Where(c => c.City == city).ToList();
        return local.Count > 0 && _rng.NextDouble() < 0.7 ? local[_rng.Next(local.Count)] : customers[_rng.Next(customers.Count)];
    }

    private int AddFutureSlots(Provider provider, Schedule schedule, DateOnly from, DateOnly to, DateTime now)
    {
        var added = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek == schedule.DayOff) continue;
            // Some days are much busier than others.
            var dayBusyness = Math.Clamp(schedule.Busyness + (_rng.NextDouble() - 0.5) * 0.4, 0.05, 0.85);
            foreach (var time in schedule.Times())
            {
                if (_rng.NextDouble() < dayBusyness) continue;
                var start = tz.ToUtc(date, time);
                if (start <= now.AddMinutes(30)) continue;
                provider.TimeSlots.Add(new TimeSlot { Provider = provider, StartUtc = start, EndUtc = start.AddMinutes(schedule.SlotMinutes), IsActive = true, CreatedAtUtc = now });
                added++;
            }
        }
        return added;
    }

    private async Task AddDocumentsAsync(Provider provider, SeedData.DemoProvider demo, DateTime created, CancellationToken ct)
    {
        var docs = demo.Status == VerificationStatus.Approved
            ? new[] { (DocumentType.GovernmentId, "government-id-sample.pdf") }
            : new[] { (DocumentType.GovernmentId, "government-id-sample.pdf"), (DocumentType.Certification, "trade-certificate-sample.pdf") };

        foreach (var (type, fileName) in docs)
        {
            var bytes = SamplePdf(type.Label(), demo.FullName);
            await using var stream = new MemoryStream(bytes);
            var formFile = new FormFile(stream, 0, bytes.Length, "file", fileName) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            var saved = await files.SavePrivateDocumentAsync(formFile, ct);
            if (!saved.Succeeded) continue;
            provider.VerificationDocuments.Add(new ProviderVerificationDocument
            {
                Provider = provider, DocumentType = type, StoredFileName = saved.Value.StoredName, OriginalFileName = fileName,
                ContentType = saved.Value.ContentType, FileSizeBytes = bytes.Length,
                Status = demo.Status == VerificationStatus.Approved ? DocumentStatus.Approved : DocumentStatus.Pending,
                UploadedAtUtc = created.AddHours(12),
                ReviewedAtUtc = demo.Status == VerificationStatus.Approved ? created.AddDays(2) : null
            });
        }
    }

    private async Task SeedSupportAndActivityAsync(List<User> customers, Dictionary<string, (Provider Provider, SeedData.DemoProvider Demo, List<Service> Services)> providers, DateTime now, CancellationToken ct)
    {
        var admin = await (from u in db.Users
                           join ur in db.UserRoles on u.Id equals ur.UserId
                           join r in db.Roles on ur.RoleId equals r.Id
                           where r.Name == AppRoles.Admin
                           select u).FirstOrDefaultAsync(ct);

        var islaBooking = await db.Bookings.Where(b => b.CustomerId == customers[1].Id && b.Status == BookingStatus.Completed)
            .OrderByDescending(b => b.ScheduledStartUtc).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
        var kabirBooking = await db.Bookings.Where(b => b.CustomerId == customers[2].Id && b.Status == BookingStatus.Completed)
            .OrderByDescending(b => b.ScheduledStartUtc).Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);

        db.Complaints.AddRange(
            new Complaint
            {
                ComplainantId = customers[1].Id, BookingId = islaBooking, Subject = "Professional arrived 40 minutes late",
                Description = "The appointment was at 10 am but the professional only arrived at 10:40 and did not call ahead. The work itself was fine.",
                Status = ComplaintStatus.Resolved, AdminResponse = "Thanks for letting us know. We have spoken to the professional about punctuality and added a ₹100 credit to your next booking.",
                ResolvedById = admin?.Id, ResolvedAtUtc = now.AddDays(-9), CreatedAtUtc = now.AddDays(-11), IsSampleData = true
            },
            new Complaint
            {
                ComplainantId = customers[2].Id, BookingId = kabirBooking, Subject = "Charged extra for materials without asking",
                Description = "I was charged an additional ₹350 for materials that were not discussed before the work started. I would like a breakdown of the charges.",
                Status = ComplaintStatus.Open, CreatedAtUtc = now.AddHours(-30), IsSampleData = true
            },
            new Complaint
            {
                ComplainantId = customers[4].Id, Subject = "Unable to reschedule from the mobile site",
                Description = "When I try to pick a new slot on my phone the page scrolls back to the top. It worked on my laptop, so this is a minor issue.",
                Status = ComplaintStatus.InProgress, AdminResponse = "We're looking into this.", CreatedAtUtc = now.AddDays(-2), IsSampleData = true
            });

        var rohitUser = providers["rohit.verma"].Provider.UserId;
        db.Notifications.AddRange(
            new Notification { UserId = customers[0].Id, Title = "Welcome to Kaarigar", Message = "Find a verified professional, pick a time that suits you and book in under a minute.", Type = NotificationType.Account, Link = "/Services", IsRead = true, CreatedAtUtc = customers[0].CreatedAtUtc },
            new Notification { UserId = customers[0].Id, Title = "Booking confirmed", Message = "Suresh Yadav confirmed your plumbing visit.", Type = NotificationType.Booking, Link = "/Customer/Bookings?status=upcoming", CreatedAtUtc = now.AddHours(-20) },
            new Notification { UserId = rohitUser, Title = "New booking request", Message = "Aarav Sharma requested Ceiling fan installation.", Type = NotificationType.Booking, Link = "/Provider/Requests", CreatedAtUtc = now.AddHours(-2) },
            new Notification { UserId = rohitUser, Title = "New booking request", Message = "Pooja Saxena requested Ceiling fan installation.", Type = NotificationType.Booking, Link = "/Provider/Requests", CreatedAtUtc = now.AddHours(-7) },
            new Notification { UserId = rohitUser, Title = "New review received", Message = "You received 5 stars for Switchboard and socket repair.", Type = NotificationType.Review, Link = "/Provider/Reviews", IsRead = true, CreatedAtUtc = now.AddDays(-3) });

        if (admin is not null)
        {
            db.Notifications.Add(new Notification { UserId = admin.Id, Title = "Providers awaiting verification", Message = "2 professionals submitted documents for review.", Type = NotificationType.Verification, Link = "/Admin/Verification", CreatedAtUtc = now.AddHours(-20) });
        }

        var log = new (string Action, string Details, string Icon, double HoursAgo)[]
        {
            ("Verification submitted", "Lakshmi Menon submitted documents for review", "bi-patch-question", 23),
            ("Verification submitted", "Imran Qureshi submitted documents for review", "bi-patch-question", 20),
            ("Complaint opened", "Charged extra for materials without asking", "bi-life-preserver", 30),
            ("Booking created", "Pooja Saxena booked Ceiling fan installation", "bi-calendar-plus", 7),
            ("Booking created", "Aarav Sharma booked Ceiling fan installation", "bi-calendar-plus", 2),
            ("Provider registered", "Rajesh Thakur applied as a professional in New Delhi", "bi-person-badge", 50)
        };
        foreach (var (action, details, icon, hours) in log)
        {
            db.ActivityLogs.Add(new ActivityLog { Action = action, Details = details, Icon = icon, CreatedAtUtc = now.AddHours(-hours) });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Builds a tiny, valid one-page PDF clearly marked as a sample.</summary>
    private static byte[] SamplePdf(string title, string name)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $"BT /F1 22 Tf 60 770 Td ({Esc(title)}) Tj ET\n" +
                      $"BT /F1 13 Tf 60 740 Td (Issued to: {Esc(name)}) Tj ET\n" +
                      "BT /F1 11 Tf 60 715 Td (SAMPLE DOCUMENT - generated by the Local Services Marketplace demo seeder.) Tj ET\n" +
                      "BT /F1 11 Tf 60 698 Td (This is not a real identity or certification document.) Tj ET\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
