using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LocalServicesMarketplace.Models;

/// <summary>A priced service offered by a provider (e.g. "Kitchen tap replacement").</summary>
public class Service
{
    public int Id { get; set; }

    public int ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Range(typeof(decimal), "1", "100000")]
    public decimal Price { get; set; }

    public PriceUnit PriceUnit { get; set; } = PriceUnit.Fixed;

    [Range(15, 600)]
    public int DurationMinutes { get; set; } = 60;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
