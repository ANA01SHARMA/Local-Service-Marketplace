using System.Globalization;
using System.Security.Claims;
using LocalServicesMarketplace.Models;

namespace LocalServicesMarketplace.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The current principal has no numeric user id.");

    public static int? TryGetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue("full_name") ?? user.Identity?.Name ?? "User";
}

public static class DisplayExtensions
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string ToMoney(this decimal value) =>
        value.ToString(value % 1 == 0 ? "C0" : "C2", India);

    public static string ToMoney(this decimal? value) => value.HasValue ? value.Value.ToMoney() : "-";

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    /// <summary>Deterministic avatar colour index (0-5) so the same person always gets the same colour.</summary>
    public static int AvatarTone(string? name) =>
        string.IsNullOrEmpty(name) ? 0 : Math.Abs(name.Aggregate(17, (h, c) => unchecked(h * 31 + c))) % 6;

    public static string Label(this BookingStatus status) => status.ToString();

    public static string BadgeClass(this BookingStatus status) => status switch
    {
        BookingStatus.Pending => "status-pending",
        BookingStatus.Confirmed => "status-confirmed",
        BookingStatus.Completed => "status-completed",
        BookingStatus.Rejected => "status-rejected",
        BookingStatus.Cancelled => "status-cancelled",
        _ => "status-default"
    };

    public static string Icon(this BookingStatus status) => status switch
    {
        BookingStatus.Pending => "bi-hourglass-split",
        BookingStatus.Confirmed => "bi-calendar-check",
        BookingStatus.Completed => "bi-check2-circle",
        BookingStatus.Rejected => "bi-x-octagon",
        BookingStatus.Cancelled => "bi-slash-circle",
        _ => "bi-circle"
    };

    public static string BadgeClass(this VerificationStatus status) => status switch
    {
        VerificationStatus.Approved => "status-completed",
        VerificationStatus.Pending => "status-pending",
        VerificationStatus.Rejected => "status-rejected",
        _ => "status-default"
    };

    public static string Label(this VerificationStatus status) => status switch
    {
        VerificationStatus.NotSubmitted => "Not submitted",
        VerificationStatus.Pending => "Under review",
        VerificationStatus.Approved => "Verified",
        VerificationStatus.Rejected => "Rejected",
        _ => status.ToString()
    };

    public static string BadgeClass(this ComplaintStatus status) => status switch
    {
        ComplaintStatus.Open => "status-pending",
        ComplaintStatus.InProgress => "status-confirmed",
        ComplaintStatus.Resolved => "status-completed",
        _ => "status-cancelled"
    };

    public static string BadgeClass(this DocumentStatus status) => status switch
    {
        DocumentStatus.Approved => "status-completed",
        DocumentStatus.Rejected => "status-rejected",
        _ => "status-pending"
    };

    public static string Label(this DocumentType type) => type switch
    {
        DocumentType.GovernmentId => "Government ID",
        DocumentType.AddressProof => "Address proof",
        DocumentType.Certification => "Trade certificate",
        DocumentType.BusinessLicense => "Business licence",
        _ => "Other document"
    };

    public static string Label(this PriceUnit unit) => unit switch
    {
        PriceUnit.PerHour => "per hour",
        PriceUnit.PerVisit => "per visit",
        _ => "fixed price"
    };

    public static string Label(this ComplaintStatus status) => status == ComplaintStatus.InProgress ? "In progress" : status.ToString();

    public static string Icon(this NotificationType type) => type switch
    {
        NotificationType.Booking => "bi-calendar2-check",
        NotificationType.Review => "bi-star",
        NotificationType.Verification => "bi-patch-check",
        NotificationType.Complaint => "bi-life-preserver",
        NotificationType.Account => "bi-person-gear",
        _ => "bi-bell"
    };

    public static string Truncate(this string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..(max - 1)].TrimEnd() + "…";
}
