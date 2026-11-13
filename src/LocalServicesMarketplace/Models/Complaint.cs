using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models;

/// <summary>A support issue raised by a customer or provider, resolved by an admin.</summary>
public class Complaint
{
    public int Id { get; set; }

    public int ComplainantId { get; set; }
    public User Complainant { get; set; } = null!;

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    [Required, StringLength(150)]
    public string Subject { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    public ComplaintStatus Status { get; set; } = ComplaintStatus.Open;

    [StringLength(1000)]
    public string? AdminResponse { get; set; }

    public int? ResolvedById { get; set; }
    public User? ResolvedBy { get; set; }

    public bool IsSampleData { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
