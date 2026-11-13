using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models;

/// <summary>
/// Identity / qualification document uploaded by a provider. The file lives in PRIVATE storage
/// (outside wwwroot) and is only streamed to the owner or an admin through an authorised action.
/// </summary>
public class ProviderVerificationDocument
{
    public int Id { get; set; }

    public int ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;

    public DocumentType DocumentType { get; set; }

    /// <summary>Random server-side file name (never the user supplied one).</summary>
    [Required, StringLength(100)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    [StringLength(500)]
    public string? ReviewNote { get; set; }

    public int? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }

    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}
