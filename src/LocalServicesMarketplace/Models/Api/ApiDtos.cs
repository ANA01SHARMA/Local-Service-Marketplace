using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models.Api;

// Request / response contracts for the REST API (/api/*). Entities are never serialised directly.

public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required, Phone] public string PhoneNumber { get; set; } = string.Empty;
    [Required, StringLength(100)] public string City { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8)] public string Password { get; set; } = string.Empty;

    /// <summary>"Customer" (default) or "Provider". Admin accounts cannot be registered.</summary>
    [RegularExpression("^(Customer|Provider)$", ErrorMessage = "Role must be Customer or Provider.")]
    public string Role { get; set; } = AppRoles.Customer;

    /// <summary>Required when Role is Provider.</summary>
    public int? CategoryId { get; set; }
    [Range(0, 60)] public int ExperienceYears { get; set; }
}

public record UserDto(int Id, string FullName, string Email, string Role, string? City, string? PhoneNumber);

public record AuthResponse(string Token, DateTime ExpiresAtUtc, UserDto User);

public record CategoryDto(int Id, string Name, string Slug, string? Description, int ProviderCount, decimal? StartingPrice);

public class ServiceSearchQuery
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public string? City { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ServiceDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public PriceUnit PriceUnit { get; init; }
    public int DurationMinutes { get; init; }
    public bool IsActive { get; init; }
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int ProviderId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public decimal ProviderRating { get; init; }
}

public record ProviderDetailDto(
    int Id, string Name, string? Headline, string? Bio, string Category, string City, int ExperienceYears,
    IEnumerable<string> Skills, IEnumerable<string> ServiceAreas, decimal AverageRating, int TotalReviews,
    int CompletedJobs, bool IsVerified, bool IsAcceptingBookings, IEnumerable<ServiceDto> Services);

public record SlotDto(int Id, DateTime StartUtc, DateTime EndUtc, string LocalDate, string LocalTime);

public record BookingDto(
    int Id, string Reference, BookingStatus Status, int CustomerId, string CustomerName, int ProviderId, string ProviderName,
    string ServiceTitle, DateTime StartUtc, DateTime EndUtc, string StartLocal, decimal Price, string City, string Address,
    string? StatusReason, bool HasReview, DateTime CreatedAtUtc);

public class CreateBookingRequest
{
    [Required] public int ProviderId { get; set; }
    [Required] public int ServiceId { get; set; }
    [Required] public int TimeSlotId { get; set; }
    [Required, StringLength(300, MinimumLength = 5)] public string Address { get; set; } = string.Empty;
    [Required, StringLength(100)] public string City { get; set; } = string.Empty;
    [StringLength(1000)] public string? Notes { get; set; }
}

public class UpdateBookingRequest
{
    /// <summary>accept | reject | complete (provider) or reschedule (customer)</summary>
    [Required, RegularExpression("^(accept|reject|complete|reschedule)$", ErrorMessage = "Action must be accept, reject, complete or reschedule.")]
    public string Action { get; set; } = string.Empty;

    /// <summary>New slot for "reschedule".</summary>
    public int? TimeSlotId { get; set; }

    [StringLength(500)] public string? Reason { get; set; }
}

public class CreateReviewRequest
{
    [Required] public int BookingId { get; set; }
    [Required, Range(1, 5)] public int Rating { get; set; }
    [StringLength(1000)] public string? Comment { get; set; }
}
