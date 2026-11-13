using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace LocalServicesMarketplace.Models;

/// <summary>
/// Application user (customers, providers and admins). Backed by ASP.NET Core Identity,
/// stored in the "Users" table. Passwords are only ever stored as Identity hashes.
/// </summary>
public class User : IdentityUser<int>
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    /// <summary>Relative public URL of the uploaded profile picture, e.g. /uploads/profiles/abc.jpg</summary>
    [StringLength(300)]
    public string? ProfileImagePath { get; set; }

    public bool IsBlocked { get; set; }

    [StringLength(300)]
    public string? BlockedReason { get; set; }

    /// <summary>Set for the development-seeded admin so the UI can nag until the password is changed.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>True for rows created by the demo seeder, so demo data can be told apart from live data.</summary>
    public bool IsSampleData { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public Provider? Provider { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}

/// <summary>Identity role stored in the "Roles" table.</summary>
public class Role : IdentityRole<int>
{
    public Role() { }
    public Role(string name) : base(name) { }

    [StringLength(200)]
    public string? Description { get; set; }
}
