using LocalServicesMarketplace.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalServicesMarketplace.Data.Configurations;

// Fluent API configuration: keys, relationships, indexes and constraints.
// SQL Server does not allow multiple cascade paths, so most relationships use Restrict
// and the application soft-deletes (IsActive / IsBlocked) instead of hard-deleting.

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(u => u.IsBlocked);
        b.HasIndex(u => u.CreatedAtUtc);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasIndex(c => c.Slug).IsUnique();
        b.HasIndex(c => c.Name).IsUnique();
        b.Property(c => c.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}

public class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> b)
    {
        b.HasIndex(p => p.UserId).IsUnique();
        b.HasIndex(p => new { p.VerificationStatus, p.City });
        b.HasIndex(p => p.AverageRating);

        b.HasOne(p => p.User).WithOne(u => u.Provider)
            .HasForeignKey<Provider>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(p => p.Category).WithMany(c => c.Providers)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Providers_Rating", "[AverageRating] >= 0 AND [AverageRating] <= 5");
            t.HasCheckConstraint("CK_Providers_Experience", "[ExperienceYears] >= 0 AND [ExperienceYears] <= 60");
        });
    }
}

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.HasIndex(s => new { s.ProviderId, s.IsActive });
        b.HasIndex(s => new { s.CategoryId, s.IsActive, s.Price });

        b.HasOne(s => s.Provider).WithMany(p => p.Services)
            .HasForeignKey(s => s.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(s => s.Category).WithMany(c => c.Services)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Services_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_Services_Duration", "[DurationMinutes] BETWEEN 15 AND 600");
        });
    }
}

public class TimeSlotConfiguration : IEntityTypeConfiguration<TimeSlot>
{
    public void Configure(EntityTypeBuilder<TimeSlot> b)
    {
        // A provider cannot publish the same start time twice.
        b.HasIndex(t => new { t.ProviderId, t.StartUtc }).IsUnique();
        b.HasIndex(t => new { t.ProviderId, t.IsActive, t.StartUtc });

        b.HasOne(t => t.Provider).WithMany(p => p.TimeSlots)
            .HasForeignKey(t => t.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t => t.HasCheckConstraint("CK_TimeSlots_Range", "[EndUtc] > [StartUtc]"));
    }
}

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        // THE double-booking guard: at most one ACTIVE (Pending=0 / Confirmed=1) booking per slot.
        // Completed / Rejected / Cancelled rows fall outside the filter so history is kept.
        b.HasIndex(x => x.TimeSlotId)
            .IsUnique()
            .HasFilter("[Status] IN (0, 1)")
            .HasDatabaseName("UX_Bookings_ActiveSlot");

        b.HasIndex(x => new { x.CustomerId, x.Status });
        b.HasIndex(x => new { x.ProviderId, x.Status, x.ScheduledStartUtc });
        b.HasIndex(x => x.CreatedAtUtc);

        b.HasOne(x => x.Customer).WithMany(u => u.Bookings)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Provider).WithMany(p => p.Bookings)
            .HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Service).WithMany(s => s.Bookings)
            .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TimeSlot).WithMany(t => t.Bookings)
            .HasForeignKey(x => x.TimeSlotId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Bookings_Price", "[Price] >= 0 AND [PlatformFee] >= 0 AND [ProviderEarning] >= 0");
            t.HasCheckConstraint("CK_Bookings_Status", "[Status] BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_Bookings_Schedule", "[ScheduledEndUtc] > [ScheduledStartUtc]");
        });
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        // One review per booking.
        b.HasIndex(r => r.BookingId).IsUnique();
        b.HasIndex(r => new { r.ProviderId, r.CreatedAtUtc });

        b.HasOne(r => r.Booking).WithOne(bk => bk.Review)
            .HasForeignKey<Review>(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Customer).WithMany()
            .HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Provider).WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProviderId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t => t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAtUtc });
        b.HasOne(n => n.User).WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> b)
    {
        b.HasIndex(c => new { c.Status, c.CreatedAtUtc });
        b.HasOne(c => c.Complainant).WithMany()
            .HasForeignKey(c => c.ComplainantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.ResolvedBy).WithMany()
            .HasForeignKey(c => c.ResolvedById).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.Booking).WithMany()
            .HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProviderVerificationDocumentConfiguration : IEntityTypeConfiguration<ProviderVerificationDocument>
{
    public void Configure(EntityTypeBuilder<ProviderVerificationDocument> b)
    {
        b.HasIndex(d => d.StoredFileName).IsUnique();
        b.HasIndex(d => new { d.ProviderId, d.Status });
        b.HasOne(d => d.Provider).WithMany(p => p.VerificationDocuments)
            .HasForeignKey(d => d.ProviderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(d => d.ReviewedBy).WithMany()
            .HasForeignKey(d => d.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_ProviderDocs_Size", "[FileSizeBytes] > 0"));
    }
}

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> b)
    {
        b.HasIndex(a => a.CreatedAtUtc);
        b.HasOne(a => a.User).WithMany()
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
