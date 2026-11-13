using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models;

/// <summary>Audit trail of notable platform events shown on the admin dashboard.</summary>
public class ActivityLog
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    [Required, StringLength(60)]
    public string Action { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Details { get; set; } = string.Empty;

    [StringLength(30)]
    public string Icon { get; set; } = "bi-activity";

    public DateTime CreatedAtUtc { get; set; }
}
