using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Reporting;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Http;

namespace LocalServicesMarketplace.Services.Interfaces;

// Business-service contracts. Controllers (MVC and API) depend only on these interfaces,
// which are registered with the built-in dependency injection container in Program.cs.

public interface IBookingService
{
    Task<IReadOnlyList<SlotDayVm>> GetAvailableSlotsAsync(int providerId, int days = 14, int? excludeBookingId = null, CancellationToken ct = default);
    Task<ServiceResult<int>> CreateAsync(int customerId, CreateBookingCommand command, CancellationToken ct = default);
    Task<ServiceResult> AcceptAsync(int providerUserId, int bookingId, CancellationToken ct = default);
    Task<ServiceResult> RejectAsync(int providerUserId, int bookingId, string? reason, CancellationToken ct = default);
    Task<ServiceResult> CompleteAsync(int providerUserId, int bookingId, CancellationToken ct = default);
    Task<ServiceResult> CancelAsync(int userId, int bookingId, string? reason, bool asAdmin, CancellationToken ct = default);
    Task<ServiceResult> RescheduleAsync(int customerId, int bookingId, int newTimeSlotId, CancellationToken ct = default);
    Task<ServiceResult<BookingDetailsViewModel>> GetDetailsAsync(int bookingId, int userId, bool isAdmin, CancellationToken ct = default);
    Task<PagedResult<BookingListItemVm>> QueryAsync(BookingScope scope, int? userId, BookingQuery query, CancellationToken ct = default);
    Task<IReadOnlyDictionary<BookingStatus, int>> CountByStatusAsync(BookingScope scope, int? userId, CancellationToken ct = default);
    Task<int> ExpireStalePendingBookingsAsync(CancellationToken ct = default);
}

public interface IProviderService
{
    Task<PagedResult<ProviderCardVm>> SearchAsync(ProviderSearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderCardVm>> GetCardsAsync(IEnumerable<int> providerIds, CancellationToken ct = default);
    Task<ProviderDetailsViewModel?> GetDetailsAsync(int providerId, int? viewerUserId, bool viewerIsAdmin, CancellationToken ct = default);
    Task<Provider?> GetByUserIdAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<NextSlotVm>> GetNextAvailableAsync(int take, CancellationToken ct = default);
    Task<PagedResult<Models.Api.ServiceDto>> SearchServicesAsync(Models.Api.ServiceSearchQuery query, CancellationToken ct = default);
    Task<Models.Api.ServiceDto?> GetServiceAsync(int serviceId, int? viewerUserId, bool viewerIsAdmin, CancellationToken ct = default);

    Task<ServiceResult> UpdateProfileAsync(int userId, ProviderProfileInputModel input, CancellationToken ct = default);

    Task<IReadOnlyList<Service>> GetServicesAsync(int providerId, CancellationToken ct = default);
    Task<ServiceResult<Service>> SaveServiceAsync(int providerId, ServiceInputModel input, CancellationToken ct = default);
    Task<ServiceResult> DeleteServiceAsync(int providerId, int serviceId, CancellationToken ct = default);

    Task<AvailabilityViewModel> GetAvailabilityAsync(int providerId, DateOnly from, int days, CancellationToken ct = default);
    Task<ServiceResult<int>> AddSlotsAsync(int providerId, AddSlotsInputModel input, CancellationToken ct = default);
    Task<ServiceResult> RemoveSlotAsync(int providerId, int slotId, CancellationToken ct = default);
    Task<ServiceResult<int>> RemoveFreeSlotsOnDateAsync(int providerId, DateOnly date, CancellationToken ct = default);

    Task<VerificationViewModel> GetVerificationAsync(int providerId, CancellationToken ct = default);
    Task<ServiceResult> UploadDocumentAsync(int providerId, DocumentType type, IFormFile file, CancellationToken ct = default);
    Task<ServiceResult> DeleteDocumentAsync(int providerId, int documentId, CancellationToken ct = default);
    Task<ServiceResult> SubmitForVerificationAsync(int providerId, CancellationToken ct = default);
    Task<ServiceResult<(Stream Stream, string ContentType, string FileName)>> OpenDocumentAsync(int documentId, int userId, bool isAdmin, CancellationToken ct = default);

    Task<ProviderDashboardViewModel> GetDashboardAsync(int providerId, CancellationToken ct = default);
    Task<EarningsViewModel> GetEarningsAsync(int providerId, CancellationToken ct = default);
    IReadOnlyList<ProviderChecklistItem> BuildChecklist(Provider provider, int serviceCount, int futureSlots, int documentCount);
}

public interface IReviewService
{
    Task<ServiceResult<Booking>> GetReviewableBookingAsync(int customerId, int bookingId, CancellationToken ct = default);
    Task<ServiceResult<int>> SubmitAsync(int customerId, int bookingId, int rating, string? comment, CancellationToken ct = default);
    Task<PagedResult<ReviewVm>> GetProviderReviewsAsync(int providerId, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewVm>> GetCustomerReviewsAsync(int customerId, CancellationToken ct = default);
    Task<ProviderRatingResult> RecalculateProviderRatingAsync(int providerId, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryTileVm>> GetActiveTilesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CategoryAdminVm>> GetAllForAdminAsync(CancellationToken ct = default);
    Task<Category?> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<Category>> SaveAsync(CategoryInputModel input, int adminId, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, int adminId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCitiesAsync(CancellationToken ct = default);
}

public interface INotificationService
{
    Task NotifyAsync(int userId, string title, string message, NotificationType type, string? link = null, CancellationToken ct = default);
    Task NotifyAdminsAsync(string title, string message, NotificationType type, string? link = null, CancellationToken ct = default);
    Task<PagedResult<Notification>> GetAsync(int userId, bool unreadOnly, int page, CancellationToken ct = default);
    Task<int> CountUnreadAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetLatestAsync(int userId, int take, CancellationToken ct = default);
    Task<string?> MarkReadAsync(int userId, int notificationId, CancellationToken ct = default);
    Task MarkAllReadAsync(int userId, CancellationToken ct = default);
}

public interface IComplaintService
{
    Task<ServiceResult<int>> CreateAsync(int userId, ComplaintInputModel input, CancellationToken ct = default);
    Task<IReadOnlyList<ComplaintListItemVm>> GetForUserAsync(int userId, CancellationToken ct = default);
    Task<PagedResult<ComplaintListItemVm>> GetAllAsync(ComplaintStatus? status, int page, CancellationToken ct = default);
    Task<ServiceResult> UpdateAsync(int adminId, ComplaintUpdateInput input, CancellationToken ct = default);
    Task<IReadOnlyList<(int Id, string Label)>> GetBookingOptionsAsync(int userId, CancellationToken ct = default);
}

public interface IAdminService
{
    Task<PagedResult<UserListItemVm>> GetUsersAsync(AdminUserQuery query, CancellationToken ct = default);
    Task<ServiceResult> SetBlockedAsync(int adminId, int userId, bool blocked, string? reason, CancellationToken ct = default);
    Task<PagedResult<ProviderAdminListItemVm>> GetProvidersAsync(AdminProviderQuery query, CancellationToken ct = default);
    Task<ProviderReviewViewModel?> GetProviderForReviewAsync(int providerId, CancellationToken ct = default);
    Task<ServiceResult> DecideProviderAsync(int adminId, int providerId, bool approve, string? note, CancellationToken ct = default);
    Task<ServiceResult> ReviewDocumentAsync(int adminId, int documentId, bool approve, string? note, CancellationToken ct = default);
    Task<PagedResult<AdminServiceListItemVm>> GetServicesAsync(string? q, int? categoryId, int page, CancellationToken ct = default);
    Task<ServiceResult> SetServiceActiveAsync(int adminId, int serviceId, bool active, CancellationToken ct = default);
    Task<ServiceResult> DeleteServiceAsync(int adminId, int serviceId, CancellationToken ct = default);
}

public interface IReportService
{
    Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TopRatedProviderView>> GetTopRatedAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyBookingReportRow>> GetMonthlyBookingReportAsync(int year, CancellationToken ct = default);
    Task<ReportsViewModel> GetReportsAsync(int year, CancellationToken ct = default);
    Task<ProviderPerformance?> GetProviderPerformanceAsync(int providerId, CancellationToken ct = default);
    Task<HomeViewModel> GetHomeAsync(CancellationToken ct = default);
}

public interface IAccountService
{
    Task<ServiceResult<User>> RegisterCustomerAsync(RegisterCustomerViewModel model, CancellationToken ct = default);
    Task<ServiceResult<User>> RegisterProviderAsync(RegisterProviderViewModel model, CancellationToken ct = default);
    Task<ServiceResult> UpdateProfileAsync(int userId, ProfileViewModel model, CancellationToken ct = default);
    Task<string> GetPrimaryRoleAsync(User user);
}

public interface IFileStorageService
{
    Task<ServiceResult<string>> SaveProfileImageAsync(IFormFile file, CancellationToken ct = default);
    void DeletePublicFile(string? relativeUrl);
    Task<ServiceResult<(string StoredName, string ContentType)>> SavePrivateDocumentAsync(IFormFile file, CancellationToken ct = default);
    Stream? OpenPrivateDocument(string storedName);
    void DeletePrivateDocument(string storedName);
    string PublicRoot { get; }
}

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

public interface IActivityLogger
{
    Task LogAsync(int? userId, string action, string details, string icon = "bi-activity", CancellationToken ct = default);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(User user, IEnumerable<string> roles);
}
