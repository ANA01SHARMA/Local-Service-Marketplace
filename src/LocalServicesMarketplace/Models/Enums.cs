using System.ComponentModel.DataAnnotations;

namespace LocalServicesMarketplace.Models;

/// <summary>
/// Booking lifecycle. Values are persisted as integers and are referenced by the
/// SQL views / stored procedures (0 and 1 are the "active" states), so do not renumber.
/// </summary>
public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Rejected = 3,
    Cancelled = 4
}

public enum VerificationStatus
{
    NotSubmitted = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum DocumentType
{
    [Display(Name = "Government ID (Aadhaar, PAN, passport)")] GovernmentId = 0,
    [Display(Name = "Address proof")] AddressProof = 1,
    [Display(Name = "Trade certificate")] Certification = 2,
    [Display(Name = "Business licence")] BusinessLicense = 3,
    [Display(Name = "Other")] Other = 4
}

public enum DocumentStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum ComplaintStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Dismissed = 3
}

public enum NotificationType
{
    Info = 0,
    Booking = 1,
    Review = 2,
    Verification = 3,
    Complaint = 4,
    Account = 5
}

public enum PriceUnit
{
    [Display(Name = "Fixed price")] Fixed = 0,
    [Display(Name = "Per hour")] PerHour = 1,
    [Display(Name = "Per visit")] PerVisit = 2
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
    public const string Provider = "Provider";

    public static readonly string[] All = [Admin, Customer, Provider];
}
