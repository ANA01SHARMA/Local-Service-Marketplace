using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LocalServicesMarketplace.Models;

/// <summary>Professional profile attached 1:1 to a <see cref="User"/> in the Provider role.</summary>
public class Provider
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Primary service category the provider is listed under.</summary>
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    [StringLength(120)]
    public string? Headline { get; set; }

    [StringLength(2000)]
    public string? Bio { get; set; }

    [Range(0, 60)]
    public int ExperienceYears { get; set; }

    /// <summary>Comma separated list of skills.</summary>
    [StringLength(500)]
    public string? Skills { get; set; }

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    /// <summary>Comma separated list of localities / areas served.</summary>
    [StringLength(500)]
    public string? ServiceAreas { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotSubmitted;

    [StringLength(500)]
    public string? VerificationNote { get; set; }

    public DateTime? VerificationSubmittedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>Denormalised rating, recalculated by the CalculateProviderAverageRating stored procedure.</summary>
    [Column(TypeName = "decimal(3,2)")]
    public decimal AverageRating { get; set; }

    public int TotalReviews { get; set; }

    /// <summary>Provider-controlled switch to pause new bookings (e.g. on leave).</summary>
    public bool IsAcceptingBookings { get; set; } = true;

    public bool IsSampleData { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<TimeSlot> TimeSlots { get; set; } = new List<TimeSlot>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<ProviderVerificationDocument> VerificationDocuments { get; set; } = new List<ProviderVerificationDocument>();

    [NotMapped]
    public bool IsApproved => VerificationStatus == VerificationStatus.Approved;

    [NotMapped]
    public IEnumerable<string> SkillList => SplitList(Skills);

    [NotMapped]
    public IEnumerable<string> ServiceAreaList => SplitList(ServiceAreas);

    public static IEnumerable<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
