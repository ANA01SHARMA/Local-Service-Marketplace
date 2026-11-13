namespace LocalServicesMarketplace.Models.Reporting;

// Keyless types mapped to SQL Server VIEWS (see migration AddReportingViewsAndProcedures)
// and to STORED PROCEDURE result sets. They are read-only.

/// <summary>Row of the dbo.TopRatedProviders view.</summary>
public class TopRatedProviderView
{
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? Headline { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategorySlug { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public int ExperienceYears { get; set; }
    public string? ProfileImagePath { get; set; }
    public int CompletedJobs { get; set; }
    public decimal? StartingPrice { get; set; }
    public bool IsSampleData { get; set; }
}

/// <summary>Row of the dbo.MonthlyBookingStatistics view.</summary>
public class MonthlyBookingStatistic
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalBookings { get; set; }
    public int PendingBookings { get; set; }
    public int ConfirmedBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int RejectedBookings { get; set; }
    public decimal CompletedValue { get; set; }
    public decimal PlatformRevenue { get; set; }
}

/// <summary>Row of the dbo.ProviderPerformance view.</summary>
public class ProviderPerformance
{
    public int ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public VerificationStatus VerificationStatus { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public int TotalBookings { get; set; }
    public int PendingJobs { get; set; }
    public int ConfirmedJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int CancelledJobs { get; set; }
    public int RejectedJobs { get; set; }
    public decimal TotalEarnings { get; set; }
    public decimal CompletionRate { get; set; }
}

/// <summary>Row returned by the dbo.GetMonthlyBookingReport stored procedure.</summary>
public class MonthlyBookingReportRow
{
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int RejectedBookings { get; set; }
    public int ActiveBookings { get; set; }
    public int UniqueCustomers { get; set; }
    public decimal GrossBookingValue { get; set; }
    public decimal PlatformRevenue { get; set; }
    public decimal ProviderPayouts { get; set; }
}

/// <summary>Row returned by the dbo.CalculateProviderAverageRating stored procedure.</summary>
public class ProviderRatingResult
{
    public int ProviderId { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
}
