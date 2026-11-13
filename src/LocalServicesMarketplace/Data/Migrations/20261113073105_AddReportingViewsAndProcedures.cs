using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalServicesMarketplace.Data.Migrations
{
    /// <summary>
    /// SQL Server views and stored procedures used by the application.
    /// Each object is created in its own batch (CREATE VIEW / PROCEDURE must be the only statement in a batch).
    ///
    /// Status codes used throughout: 0 Pending, 1 Confirmed, 2 Completed, 3 Rejected, 4 Cancelled.
    /// Provider VerificationStatus: 2 = Approved.
    /// </summary>
    public partial class AddReportingViewsAndProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ------------------------------------------------------------------ VIEWS

            // Verified, active providers that have at least one review, with job and price stats.
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.TopRatedProviders
AS
SELECT  p.Id                AS ProviderId,
        u.FullName          AS ProviderName,
        p.Headline,
        c.Name              AS CategoryName,
        c.Slug              AS CategorySlug,
        p.City,
        p.AverageRating,
        p.TotalReviews,
        p.ExperienceYears,
        u.ProfileImagePath,
        (SELECT COUNT(*) FROM dbo.Bookings b WHERE b.ProviderId = p.Id AND b.Status = 2)        AS CompletedJobs,
        (SELECT MIN(s.Price) FROM dbo.Services s WHERE s.ProviderId = p.Id AND s.IsActive = 1)  AS StartingPrice,
        p.IsSampleData
FROM    dbo.Providers  p
JOIN    dbo.Users      u ON u.Id = p.UserId
JOIN    dbo.Categories c ON c.Id = p.CategoryId
WHERE   p.VerificationStatus = 2
  AND   u.IsBlocked = 0
  AND   p.TotalReviews > 0;");

            // Booking counts and money per calendar month (by booking creation date, UTC).
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.MonthlyBookingStatistics
AS
SELECT  YEAR(b.CreatedAtUtc)                                       AS [Year],
        MONTH(b.CreatedAtUtc)                                      AS [Month],
        COUNT(*)                                                   AS TotalBookings,
        SUM(CASE WHEN b.Status = 0 THEN 1 ELSE 0 END)              AS PendingBookings,
        SUM(CASE WHEN b.Status = 1 THEN 1 ELSE 0 END)              AS ConfirmedBookings,
        SUM(CASE WHEN b.Status = 2 THEN 1 ELSE 0 END)              AS CompletedBookings,
        SUM(CASE WHEN b.Status = 4 THEN 1 ELSE 0 END)              AS CancelledBookings,
        SUM(CASE WHEN b.Status = 3 THEN 1 ELSE 0 END)              AS RejectedBookings,
        CAST(SUM(CASE WHEN b.Status = 2 THEN b.Price ELSE 0 END) AS DECIMAL(18,2))       AS CompletedValue,
        CAST(SUM(CASE WHEN b.Status = 2 THEN b.PlatformFee ELSE 0 END) AS DECIMAL(18,2)) AS PlatformRevenue
FROM    dbo.Bookings b
GROUP BY YEAR(b.CreatedAtUtc), MONTH(b.CreatedAtUtc);");

            // One row per provider: booking outcomes, earnings and completion rate (LEFT JOIN keeps new providers).
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.ProviderPerformance
AS
SELECT  p.Id                    AS ProviderId,
        u.FullName              AS ProviderName,
        u.Email,
        c.Name                  AS CategoryName,
        p.City,
        p.VerificationStatus,
        p.AverageRating,
        p.TotalReviews,
        COUNT(b.Id)                                                AS TotalBookings,
        SUM(CASE WHEN b.Status = 0 THEN 1 ELSE 0 END)              AS PendingJobs,
        SUM(CASE WHEN b.Status = 1 THEN 1 ELSE 0 END)              AS ConfirmedJobs,
        SUM(CASE WHEN b.Status = 2 THEN 1 ELSE 0 END)              AS CompletedJobs,
        SUM(CASE WHEN b.Status = 4 THEN 1 ELSE 0 END)              AS CancelledJobs,
        SUM(CASE WHEN b.Status = 3 THEN 1 ELSE 0 END)              AS RejectedJobs,
        CAST(ISNULL(SUM(CASE WHEN b.Status = 2 THEN b.ProviderEarning END), 0) AS DECIMAL(18,2)) AS TotalEarnings,
        CAST(CASE WHEN COUNT(b.Id) = 0 THEN 0
                  ELSE 100.0 * SUM(CASE WHEN b.Status = 2 THEN 1 ELSE 0 END) / COUNT(b.Id)
             END AS DECIMAL(18,2))                                 AS CompletionRate
FROM        dbo.Providers  p
JOIN        dbo.Users      u ON u.Id = p.UserId
JOIN        dbo.Categories c ON c.Id = p.CategoryId
LEFT JOIN   dbo.Bookings   b ON b.ProviderId = p.Id
GROUP BY p.Id, u.FullName, u.Email, c.Name, p.City, p.VerificationStatus, p.AverageRating, p.TotalReviews;");

            // ------------------------------------------------------------------ STORED PROCEDURES

            // Creates a booking atomically. Result codes:
            //   0 ok, 1 customer blocked/missing, 2 provider not bookable, 3 service invalid, 4 slot invalid,
            //   5 slot too soon / in the past, 6 slot already booked, 7 self-booking, 8 customer double-booked
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.CreateBooking
    @CustomerId      INT,
    @ProviderId      INT,
    @ServiceId       INT,
    @TimeSlotId      INT,
    @Address         NVARCHAR(300),
    @City            NVARCHAR(100),
    @Notes           NVARCHAR(1000) = NULL,
    @CommissionRate  DECIMAL(5,4),
    @MinLeadMinutes  INT,
    @NowUtc          DATETIME2,
    @BookingId       INT OUTPUT,
    @ResultCode      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @BookingId = NULL;
    SET @ResultCode = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @CustomerId AND IsBlocked = 0)
            BEGIN SET @ResultCode = 1; ROLLBACK TRANSACTION; RETURN; END

        IF NOT EXISTS (SELECT 1
                       FROM dbo.Providers p JOIN dbo.Users u ON u.Id = p.UserId
                       WHERE p.Id = @ProviderId AND p.VerificationStatus = 2 AND p.IsAcceptingBookings = 1 AND u.IsBlocked = 0)
            BEGIN SET @ResultCode = 2; ROLLBACK TRANSACTION; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.Providers WHERE Id = @ProviderId AND UserId = @CustomerId)
            BEGIN SET @ResultCode = 7; ROLLBACK TRANSACTION; RETURN; END

        DECLARE @Price DECIMAL(10,2);
        SELECT @Price = Price FROM dbo.Services WHERE Id = @ServiceId AND ProviderId = @ProviderId AND IsActive = 1;
        IF @Price IS NULL
            BEGIN SET @ResultCode = 3; ROLLBACK TRANSACTION; RETURN; END

        -- Lock the slot row (UPDLOCK) and hold the lock until commit (HOLDLOCK = serializable range lock)
        DECLARE @Start DATETIME2, @End DATETIME2;
        SELECT @Start = StartUtc, @End = EndUtc
        FROM dbo.TimeSlots WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @TimeSlotId AND ProviderId = @ProviderId AND IsActive = 1;
        IF @Start IS NULL
            BEGIN SET @ResultCode = 4; ROLLBACK TRANSACTION; RETURN; END

        IF @Start < DATEADD(MINUTE, @MinLeadMinutes, @NowUtc)
            BEGIN SET @ResultCode = 5; ROLLBACK TRANSACTION; RETURN; END

        -- A slot is taken by a Pending, Confirmed or Completed booking (Rejected/Cancelled release it).
        IF EXISTS (SELECT 1 FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
                   WHERE TimeSlotId = @TimeSlotId AND Status IN (0, 1, 2))
            BEGIN SET @ResultCode = 6; ROLLBACK TRANSACTION; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
                   WHERE CustomerId = @CustomerId AND Status IN (0, 1)
                     AND ScheduledStartUtc < @End AND ScheduledEndUtc > @Start)
            BEGIN SET @ResultCode = 8; ROLLBACK TRANSACTION; RETURN; END

        DECLARE @Fee DECIMAL(10,2) = ROUND(@Price * @CommissionRate, 2);

        INSERT INTO dbo.Bookings
            (CustomerId, ProviderId, ServiceId, TimeSlotId, ScheduledStartUtc, ScheduledEndUtc,
             Address, City, Notes, Price, PlatformFee, ProviderEarning, Status, RescheduleCount,
             CreatedAtUtc, UpdatedAtUtc, IsSampleData)
        VALUES
            (@CustomerId, @ProviderId, @ServiceId, @TimeSlotId, @Start, @End,
             @Address, @City, @Notes, @Price, @Fee, @Price - @Fee, 0, 0,
             @NowUtc, @NowUtc, 0);

        SET @BookingId = CAST(SCOPE_IDENTITY() AS INT);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        -- Unique index UX_Bookings_ActiveSlot caught a race: report as 'slot already booked'.
        IF ERROR_NUMBER() IN (2601, 2627) BEGIN SET @ResultCode = 6; RETURN; END
        -- Deadlock victim under contention: also a lost race for the slot.
        IF ERROR_NUMBER() = 1205 BEGIN SET @ResultCode = 6; RETURN; END;
        THROW;
    END CATCH
END");

            // Recomputes and stores a provider's average rating / review count, and returns them.
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.CalculateProviderAverageRating
    @ProviderId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Average DECIMAL(3,2), @Count INT;

    SELECT  @Count   = COUNT(*),
            @Average = CAST(ISNULL(AVG(CAST(r.Rating AS DECIMAL(5,2))), 0) AS DECIMAL(3,2))
    FROM    dbo.Reviews r
    WHERE   r.ProviderId = @ProviderId;

    UPDATE dbo.Providers
    SET    AverageRating = @Average,
           TotalReviews  = @Count
    WHERE  Id = @ProviderId;

    SELECT @ProviderId AS ProviderId, @Average AS AverageRating, @Count AS TotalReviews;
END");

            // 12 rows (Jan..Dec) for a year, including months with no bookings.
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.GetMonthlyBookingReport
    @Year INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Months AS (
        SELECT 1 AS [Month]
        UNION ALL
        SELECT [Month] + 1 FROM Months WHERE [Month] < 12
    )
    SELECT  m.[Month],
            DATENAME(MONTH, DATEFROMPARTS(@Year, m.[Month], 1))                         AS MonthName,
            COUNT(b.Id)                                                                 AS TotalBookings,
            SUM(CASE WHEN b.Status = 2 THEN 1 ELSE 0 END)                               AS CompletedBookings,
            SUM(CASE WHEN b.Status = 4 THEN 1 ELSE 0 END)                               AS CancelledBookings,
            SUM(CASE WHEN b.Status = 3 THEN 1 ELSE 0 END)                               AS RejectedBookings,
            SUM(CASE WHEN b.Status IN (0, 1) THEN 1 ELSE 0 END)                         AS ActiveBookings,
            COUNT(DISTINCT b.CustomerId)                                                AS UniqueCustomers,
            CAST(ISNULL(SUM(CASE WHEN b.Status = 2 THEN b.Price END), 0) AS DECIMAL(18,2))           AS GrossBookingValue,
            CAST(ISNULL(SUM(CASE WHEN b.Status = 2 THEN b.PlatformFee END), 0) AS DECIMAL(18,2))     AS PlatformRevenue,
            CAST(ISNULL(SUM(CASE WHEN b.Status = 2 THEN b.ProviderEarning END), 0) AS DECIMAL(18,2)) AS ProviderPayouts
    FROM    Months m
    LEFT JOIN dbo.Bookings b
           ON YEAR(b.CreatedAtUtc) = @Year AND MONTH(b.CreatedAtUtc) = m.[Month]
    GROUP BY m.[Month]
    ORDER BY m.[Month];
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.GetMonthlyBookingReport;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.CalculateProviderAverageRating;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.CreateBooking;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.ProviderPerformance;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.MonthlyBookingStatistics;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.TopRatedProviders;");
        }
    }
}
