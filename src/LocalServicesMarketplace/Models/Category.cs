using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models;

public class Category
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(80), RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Description { get; set; }

    /// <summary>Bootstrap Icons class name, e.g. "bi-wrench-adjustable".</summary>
    [StringLength(60)]
    public string Icon { get; set; } = "bi-grid";

    /// <summary>Relative URL of the category illustration.</summary>
    [StringLength(200)]
    public string? ImagePath { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<Provider> Providers { get; set; } = new List<Provider>();
}
