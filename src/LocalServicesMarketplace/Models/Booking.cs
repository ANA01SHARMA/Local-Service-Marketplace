using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LocalServicesMarketplace.Models;

public class Booking
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public User Customer { get; set; } = null!;

    public int ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int TimeSlotId { get; set; }
    public TimeSlot TimeSlot { get; set; } = null!;

    /// <summary>Copied from the slot at booking time so history survives slot changes.</summary>
    public DateTime ScheduledStartUtc { get; set; }
    public DateTime ScheduledEndUtc { get; set; }

    [Required, StringLength(300)]
    public string Address { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Price snapshot at booking time.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    /// <summary>Platform commission charged on the booking.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal PlatformFee { get; set; }

    /// <summary>Amount the provider earns once completed (Price - PlatformFee).</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal ProviderEarning { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    [StringLength(500)]
    public string? StatusReason { get; set; }

    [StringLength(20)]
    public string? CancelledBy { get; set; }

    public int RescheduleCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public bool IsSampleData { get; set; }

    /// <summary>Optimistic concurrency token (SQL Server rowversion).</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public Review? Review { get; set; }

    [NotMapped]
    public string Reference => FormatReference(Id);

    [NotMapped]
    public bool IsActive => Status is BookingStatus.Pending or BookingStatus.Confirmed;

    public static string FormatReference(int id) => $"LSM-{id:D5}";
}
