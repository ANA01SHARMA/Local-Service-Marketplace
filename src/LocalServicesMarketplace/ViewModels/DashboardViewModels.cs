using System.ComponentModel.DataAnnotations;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Reporting;
using Microsoft.AspNetCore.Http;

namespace LocalServicesMarketplace.ViewModels;

// ---------------- Customer ----------------

public class CustomerDashboardViewModel
{
    public string FullName { get; init; } = string.Empty;
    public int TotalBookings { get; init; }
    public int PendingBookings { get; init; }
    public int ConfirmedBookings { get; init; }
    public int CompletedBookings { get; init; }
    public int CancelledBookings { get; init; }
    public decimal TotalSpent { get; init; }
    public int AwaitingReview { get; init; }
    public IReadOnlyList<BookingListItemVm> Upcoming { get; init; } = [];
    public IReadOnlyList<BookingListItemVm> RecentActivity { get; init; } = [];
    public IReadOnlyList<CategoryTileVm> Categories { get; init; } = [];
}

// ---------------- Provider ----------------

public class ProviderChecklistItem
{
    public string Label { get; init; } = string.Empty;
    public bool Done { get; init; }
    public string Action { get; init; } = string.Empty;
}

public class ProviderDashboardViewModel
{
    public Provider Provider { get; init; } = null!;
    public ProviderPerformance? Performance { get; init; }
    public int PendingRequests { get; init; }
    public int UpcomingJobs { get; init; }
    public decimal ThisMonthEarnings { get; init; }
    public IReadOnlyList<BookingListItemVm> NextJobs { get; init; } = [];
    public IReadOnlyList<BookingListItemVm> LatestRequests { get; init; } = [];
    public IReadOnlyList<ProviderChecklistItem> Checklist { get; init; } = [];
    public IReadOnlyList<EarningsMonthVm> EarningsTrend { get; init; } = [];
    public int FreeSlotsNext7Days { get; init; }
}

public class ProviderProfileInputModel
{
    [Required, StringLength(100, MinimumLength = 2), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Phone, StringLength(20), Display(Name = "Mobile number")]
    public string? PhoneNumber { get; set; }

    [StringLength(120)]
    public string? Headline { get; set; }

    [StringLength(2000), Display(Name = "About you")]
    public string? Bio { get; set; }

    [Range(0, 60), Display(Name = "Years of experience")]
    public int ExperienceYears { get; set; }

    [StringLength(500), Display(Name = "Skills (comma separated)")]
    public string? Skills { get; set; }

    [Required, Display(Name = "Main category")]
    public int CategoryId { get; set; }

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(500), Display(Name = "Areas you serve (comma separated)")]
    public string? ServiceAreas { get; set; }

    [Display(Name = "Accepting new bookings")]
    public bool IsAcceptingBookings { get; set; } = true;

    [Display(Name = "Profile photo")]
    public IFormFile? Photo { get; set; }

    public string? ProfileImagePath { get; set; }
    public string Email { get; set; } = string.Empty;
    public VerificationStatus VerificationStatus { get; set; }
}

public class ServiceInputModel
{
    public int? Id { get; set; }

    [Required, StringLength(120, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required, Display(Name = "Category")]
    public int CategoryId { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required, Range(typeof(decimal), "50", "100000", ErrorMessage = "Price must be between ₹50 and ₹1,00,000.")]
    public decimal Price { get; set; }

    [Display(Name = "Pricing")]
    public PriceUnit PriceUnit { get; set; } = PriceUnit.Fixed;

    [Range(15, 600), Display(Name = "Duration (minutes)")]
    public int DurationMinutes { get; set; } = 60;

    [Display(Name = "Visible to customers")]
    public bool IsActive { get; set; } = true;
}

public class ProviderSlotVm
{
    public int Id { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public bool IsActive { get; init; }
    public int? BookingId { get; init; }
    public BookingStatus? BookingStatus { get; init; }
    public string? CustomerName { get; init; }
    public bool IsPast { get; init; }
}

public class AvailabilityDayVm
{
    public DateOnly Date { get; init; }
    public IReadOnlyList<ProviderSlotVm> Slots { get; init; } = [];
}

public class AddSlotsInputModel : IValidatableObject
{
    [Required, Display(Name = "From date")]
    public DateOnly? FromDate { get; set; }

    [Required, Display(Name = "To date")]
    public DateOnly? ToDate { get; set; }

    [Display(Name = "Working days")]
    public List<DayOfWeek> Days { get; set; } = [];

    [Required, Display(Name = "Start time")]
    public TimeOnly? StartTime { get; set; }

    [Required, Display(Name = "End time")]
    public TimeOnly? EndTime { get; set; }

    [Range(30, 240), Display(Name = "Slot length (minutes)")]
    public int SlotMinutes { get; set; } = 60;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FromDate > ToDate)
            yield return new ValidationResult("The end date must be on or after the start date.", [nameof(ToDate)]);
        if (FromDate.HasValue && ToDate.HasValue && ToDate.Value.DayNumber - FromDate.Value.DayNumber > 60)
            yield return new ValidationResult("You can add at most 60 days at a time.", [nameof(ToDate)]);
        if (StartTime >= EndTime)
            yield return new ValidationResult("The end time must be after the start time.", [nameof(EndTime)]);
        if (Days.Count == 0)
            yield return new ValidationResult("Pick at least one working day.", [nameof(Days)]);
    }
}

public class AvailabilityViewModel
{
    public IReadOnlyList<AvailabilityDayVm> Days { get; init; } = [];
    public AddSlotsInputModel AddSlots { get; init; } = new();
    public DateOnly From { get; init; }
    public int TotalFree { get; init; }
    public int TotalBooked { get; init; }
}

public class EarningsMonthVm
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Jobs { get; init; }
    public decimal Gross { get; init; }
    public decimal Fees { get; init; }
    public decimal Net { get; init; }
    public string Label => new DateTime(Year, Month, 1).ToString("MMM yy");
}

public class EarningsViewModel
{
    public decimal TotalEarnings { get; init; }
    public decimal ThisMonth { get; init; }
    public decimal UpcomingValue { get; init; }
    public decimal TotalFees { get; init; }
    public int CompletedJobs { get; init; }
    public IReadOnlyList<EarningsMonthVm> Months { get; init; } = [];
    public IReadOnlyList<BookingListItemVm> RecentPayouts { get; init; } = [];
    public decimal CommissionRate { get; init; }
}

public class DocumentUploadInputModel
{
    [Required, Display(Name = "Document type")]
    public DocumentType DocumentType { get; set; }

    [Required(ErrorMessage = "Choose a file to upload."), Display(Name = "File (PDF, JPG or PNG, max 5 MB)")]
    public IFormFile? File { get; set; }
}

public class VerificationViewModel
{
    public Provider Provider { get; init; } = null!;
    public IReadOnlyList<ProviderVerificationDocument> Documents { get; init; } = [];
    public IReadOnlyList<ProviderChecklistItem> Checklist { get; init; } = [];
    public DocumentUploadInputModel Upload { get; init; } = new();
    public bool CanSubmit { get; init; }
}

// ---------------- Admin ----------------

public class AdminDashboardViewModel
{
    public int TotalUsers { get; init; }
    public int TotalCustomers { get; init; }
    public int TotalProviders { get; init; }
    public int PendingApprovals { get; init; }
    public int TotalBookings { get; init; }
    public int CompletedBookings { get; init; }
    public int CancelledBookings { get; init; }
    public decimal TotalRevenue { get; init; }
    public int OpenComplaints { get; init; }
    public IReadOnlyList<MonthlyBookingStatistic> Monthly { get; init; } = [];
    public IReadOnlyDictionary<BookingStatus, int> StatusDistribution { get; init; } = new Dictionary<BookingStatus, int>();
    public IReadOnlyList<TopRatedProviderView> TopRated { get; init; } = [];
    public IReadOnlyList<UserListItemVm> RecentUsers { get; init; } = [];
    public IReadOnlyList<ProviderAdminListItemVm> PendingProviders { get; init; } = [];
    public IReadOnlyList<BookingListItemVm> RecentBookings { get; init; } = [];
    public IReadOnlyList<ActivityLog> Activities { get; init; } = [];
}

public class AdminUserQuery
{
    public string? Role { get; set; }
    public string? Status { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
}

public class UserListItemVm
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? City { get; init; }
    public string Role { get; init; } = string.Empty;
    public bool IsBlocked { get; init; }
    public string? BlockedReason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public bool IsSampleData { get; init; }
    public int BookingCount { get; init; }
    public string? ProfileImagePath { get; init; }
}

public class AdminProviderQuery
{
    public VerificationStatus? Status { get; set; }
    public string? Q { get; set; }
    public int? CategoryId { get; set; }
    public int Page { get; set; } = 1;
}

public class ProviderAdminListItemVm
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public int ExperienceYears { get; init; }
    public VerificationStatus VerificationStatus { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int DocumentCount { get; init; }
    public int ServiceCount { get; init; }
    public decimal AverageRating { get; init; }
    public int TotalReviews { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsSampleData { get; init; }
    public string? ProfileImagePath { get; init; }
}

public class ProviderReviewViewModel
{
    public Provider Provider { get; init; } = null!;
    public IReadOnlyList<ProviderVerificationDocument> Documents { get; init; } = [];
    public IReadOnlyList<Service> Services { get; init; } = [];
    public ProviderPerformance? Performance { get; init; }
}

public class CategoryInputModel
{
    public int? Id { get; set; }

    [Required, StringLength(80, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(80), RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Description { get; set; }

    [Required, StringLength(60), RegularExpression("^bi-[a-z0-9-]+$", ErrorMessage = "Use a Bootstrap Icons class such as bi-tools.")]
    public string Icon { get; set; } = "bi-grid";

    [Range(0, 999), Display(Name = "Display order")]
    public int DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class CategoryAdminVm
{
    public Category Category { get; init; } = null!;
    public int ServiceCount { get; init; }
    public int ProviderCount { get; init; }
    public int BookingCount { get; init; }
}

public class AdminServiceListItemVm
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public int ProviderId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public PriceUnit PriceUnit { get; init; }
    public bool IsActive { get; init; }
    public int BookingCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public class ComplaintListItemVm
{
    public int Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ComplainantName { get; init; } = string.Empty;
    public string ComplainantEmail { get; init; } = string.Empty;
    public string ComplainantRole { get; init; } = string.Empty;
    public int? BookingId { get; init; }
    public ComplaintStatus Status { get; init; }
    public string? AdminResponse { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ResolvedAtUtc { get; init; }
    public bool IsSampleData { get; init; }
}

public class ComplaintUpdateInput
{
    [Required]
    public int Id { get; set; }

    [Required]
    public ComplaintStatus Status { get; set; }

    [StringLength(1000), Display(Name = "Response to the user")]
    public string? AdminResponse { get; set; }
}

public class CustomerReportRow
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? City { get; init; }
    public int TotalBookings { get; init; }
    public int CompletedBookings { get; init; }
    public int CancelledBookings { get; init; }
    public decimal TotalSpent { get; init; }
    public DateTime? LastBookingUtc { get; init; }
}

public class CategoryRevenueRow
{
    public string CategoryName { get; init; } = string.Empty;
    public int CompletedBookings { get; init; }
    public decimal GrossValue { get; init; }
    public decimal PlatformRevenue { get; init; }
}

public class ReportsViewModel
{
    public int Year { get; init; }
    public IReadOnlyList<int> Years { get; init; } = [];
    public IReadOnlyList<MonthlyBookingReportRow> MonthlyReport { get; init; } = [];
    public IReadOnlyList<ProviderPerformance> ProviderPerformance { get; init; } = [];
    public IReadOnlyList<CustomerReportRow> TopCustomers { get; init; } = [];
    public IReadOnlyList<CategoryRevenueRow> CategoryRevenue { get; init; } = [];
    public decimal YearGross => MonthlyReport.Sum(r => r.GrossBookingValue);
    public decimal YearRevenue => MonthlyReport.Sum(r => r.PlatformRevenue);
    public decimal YearPayouts => MonthlyReport.Sum(r => r.ProviderPayouts);
    public int YearBookings => MonthlyReport.Sum(r => r.TotalBookings);
}

public class AdminBookingsViewModel
{
    public BookingQuery Query { get; init; } = new();
    public PagedResult<BookingListItemVm> Results { get; init; } = new();
    public IReadOnlyDictionary<BookingStatus, int> Counts { get; init; } = new Dictionary<BookingStatus, int>();
}
