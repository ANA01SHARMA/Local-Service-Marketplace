using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models;

/// <summary>One review per completed booking (enforced by a unique index on BookingId).</summary>
public class Review
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public int CustomerId { get; set; }
    public User Customer { get; set; } = null!;

    public int ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }

    public bool IsSampleData { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
