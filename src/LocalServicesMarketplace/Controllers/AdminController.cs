using System.Globalization;
using System.Text;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalServicesMarketplace.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class AdminController(
    IReportService reports,
    IAdminService admin,
    ICategoryService categories,
    IComplaintService complaints,
    IBookingService bookings,
    IProviderService providers,
    ITimeZoneService tz) : Controller
{
    private int AdminId => User.GetUserId();

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Dashboard";
        return View(await reports.GetAdminDashboardAsync(ct));
    }

    // ------------------------------------------------------------------ users

    public async Task<IActionResult> Users([FromQuery] AdminUserQuery query, CancellationToken ct)
    {
        ViewData["Title"] = "Users";
        ViewData["Query"] = query;
        return View(await admin.GetUsersAsync(query, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Block(int id, string? reason, string? returnUrl, CancellationToken ct)
    {
        Flash(await admin.SetBlockedAsync(AdminId, id, true, reason, ct), "Account blocked. The user has been signed out.");
        return Back(returnUrl, nameof(Users));
    }

    [HttpPost]
    public async Task<IActionResult> Unblock(int id, string? returnUrl, CancellationToken ct)
    {
        Flash(await admin.SetBlockedAsync(AdminId, id, false, null, ct), "Account unblocked.");
        return Back(returnUrl, nameof(Users));
    }

    // ------------------------------------------------------------------ providers & verification

    public async Task<IActionResult> Providers([FromQuery] AdminProviderQuery query, CancellationToken ct)
    {
        ViewData["Title"] = "Providers";
        ViewData["Query"] = query;
        ViewBag.Categories = await categories.GetActiveAsync(ct);
        return View(await admin.GetProvidersAsync(query, ct));
    }

    public async Task<IActionResult> Verification(int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Provider verification";
        return View(await admin.GetProvidersAsync(new AdminProviderQuery { Status = VerificationStatus.Pending, Page = page }, ct));
    }

    public async Task<IActionResult> ReviewProvider(int id, CancellationToken ct)
    {
        var model = await admin.GetProviderForReviewAsync(id, ct);
        if (model is null) return NotFound();
        ViewData["Title"] = $"Review {model.Provider.User.FullName}";
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> DecideProvider(int id, bool approve, string? note, CancellationToken ct)
    {
        Flash(await admin.DecideProviderAsync(AdminId, id, approve, note, ct),
            approve ? "Provider approved and now visible to customers." : "Provider rejected. They have been told why.");
        return RedirectToAction(nameof(ReviewProvider), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ReviewDocument(int id, int providerId, bool approve, string? reason, CancellationToken ct)
    {
        // "reason" matches the shared reason modal's textarea name.
        Flash(await admin.ReviewDocumentAsync(AdminId, id, approve, reason, ct), approve ? "Document approved." : "Document rejected.");
        return RedirectToAction(nameof(ReviewProvider), new { id = providerId });
    }

    /// <summary>Streams a private verification document (admins only).</summary>
    public async Task<IActionResult> Document(int id, CancellationToken ct)
    {
        var result = await providers.OpenDocumentAsync(id, AdminId, true, ct);
        if (!result.Succeeded) return NotFound();
        var (stream, contentType, fileName) = result.Value;
        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(fileName)}\"";
        return File(stream, contentType);
    }

    // ------------------------------------------------------------------ categories

    public async Task<IActionResult> Categories(int? edit, CancellationToken ct)
    {
        ViewData["Title"] = "Categories";
        var list = await categories.GetAllForAdminAsync(ct);
        ViewBag.Categories = list;
        var editing = edit.HasValue ? list.FirstOrDefault(c => c.Category.Id == edit)?.Category : null;
        return View(editing is null
            ? new CategoryInputModel { DisplayOrder = list.Count + 1 }
            : new CategoryInputModel
            {
                Id = editing.Id, Name = editing.Name, Slug = editing.Slug, Description = editing.Description,
                Icon = editing.Icon, DisplayOrder = editing.DisplayOrder, IsActive = editing.IsActive
            });
    }

    [HttpPost]
    public async Task<IActionResult> Categories(CategoryInputModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await categories.SaveAsync(model, AdminId, ct);
            if (result.Succeeded)
            {
                TempData["Success"] = model.Id is > 0 ? "Category updated." : "Category added.";
                return RedirectToAction(nameof(Categories));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        ViewData["Title"] = "Categories";
        ViewBag.Categories = await categories.GetAllForAdminAsync(ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        Flash(await categories.DeleteAsync(id, AdminId, ct), "Category deleted.");
        return RedirectToAction(nameof(Categories));
    }

    // ------------------------------------------------------------------ services

    public async Task<IActionResult> Services(string? q, int? categoryId, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Services";
        ViewBag.Q = q;
        ViewBag.CategoryId = categoryId;
        ViewBag.Categories = await categories.GetActiveAsync(ct);
        return View(await admin.GetServicesAsync(q, categoryId, page, ct));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleService(int id, bool active, string? returnUrl, CancellationToken ct)
    {
        Flash(await admin.SetServiceActiveAsync(AdminId, id, active, ct), active ? "Service is visible again." : "Service hidden from customers.");
        return Back(returnUrl, nameof(Services));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteService(int id, string? returnUrl, CancellationToken ct)
    {
        Flash(await admin.DeleteServiceAsync(AdminId, id, ct), "Service deleted.");
        return Back(returnUrl, nameof(Services));
    }

    // ------------------------------------------------------------------ bookings

    public async Task<IActionResult> Bookings([FromQuery] BookingQuery query, CancellationToken ct)
    {
        ViewData["Title"] = "Bookings";
        query.PageSize = 15;
        return View(new AdminBookingsViewModel
        {
            Query = query,
            Results = await bookings.QueryAsync(BookingScope.Admin, null, query, ct),
            Counts = await bookings.CountByStatusAsync(BookingScope.Admin, null, ct)
        });
    }

    // ------------------------------------------------------------------ complaints

    public async Task<IActionResult> Complaints(ComplaintStatus? status, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Complaints";
        ViewBag.Status = status;
        return View(await complaints.GetAllAsync(status, page, ct));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateComplaint(ComplaintUpdateInput input, string? returnUrl, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Choose a valid status.";
            return Back(returnUrl, nameof(Complaints));
        }
        Flash(await complaints.UpdateAsync(AdminId, input, ct), "Complaint updated and the user notified.");
        return Back(returnUrl, nameof(Complaints));
    }

    // ------------------------------------------------------------------ reports

    public async Task<IActionResult> Reports(int? year, CancellationToken ct)
    {
        ViewData["Title"] = "Reports and analytics";
        return View(await reports.GetReportsAsync(year ?? tz.LocalToday.Year, ct));
    }

    /// <summary>CSV download of the GetMonthlyBookingReport stored procedure.</summary>
    public async Task<IActionResult> ExportMonthlyReport(int year, CancellationToken ct)
    {
        var rows = await reports.GetMonthlyBookingReportAsync(year, ct);
        var csv = new StringBuilder("Month,Total bookings,Completed,Cancelled,Rejected,Active,Unique customers,Gross value,Platform revenue,Provider payouts\n");
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(',',
                r.MonthName, r.TotalBookings, r.CompletedBookings, r.CancelledBookings, r.RejectedBookings, r.ActiveBookings, r.UniqueCustomers,
                r.GrossBookingValue.ToString(CultureInfo.InvariantCulture), r.PlatformRevenue.ToString(CultureInfo.InvariantCulture),
                r.ProviderPayouts.ToString(CultureInfo.InvariantCulture)));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"monthly-booking-report-{year}.csv");
    }

    // ------------------------------------------------------------------ helpers

    private void Flash(ServiceResult result, string success)
    {
        if (result.Succeeded) TempData["Success"] = success;
        else TempData["Error"] = result.Error;
    }

    private IActionResult Back(string? returnUrl, string fallbackAction) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(fallbackAction);
}
