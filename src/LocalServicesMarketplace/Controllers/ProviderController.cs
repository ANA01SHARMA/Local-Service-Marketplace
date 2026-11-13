using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LocalServicesMarketplace.Controllers;

/// <summary>Provider dashboard. Every action works on the signed-in provider's own profile only.</summary>
[Authorize(Roles = AppRoles.Provider)]
public class ProviderController(
    IProviderService providers,
    IBookingService bookings,
    IReviewService reviews,
    ICategoryService categories,
    ITimeZoneService tz,
    SignInManager<User> signInManager,
    UserManager<User> userManager) : Controller
{
    private Provider _provider = null!;

    /// <summary>Resolves the provider profile for the current user before every action.</summary>
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var provider = await providers.GetByUserIdAsync(User.GetUserId(), HttpContext.RequestAborted);
        if (provider is null)
        {
            context.Result = Forbid();
            return;
        }
        _provider = provider;
        ViewBag.ProviderStatus = provider.VerificationStatus;
        ViewBag.ProviderId = provider.Id;
        await next();
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Overview";
        return View(await providers.GetDashboardAsync(_provider.Id, ct));
    }

    // ------------------------------------------------------------------ profile

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        ViewData["Title"] = "Professional profile";
        await LoadListsAsync(ct);
        return View(new ProviderProfileInputModel
        {
            FullName = _provider.User.FullName,
            PhoneNumber = _provider.User.PhoneNumber,
            Headline = _provider.Headline,
            Bio = _provider.Bio,
            ExperienceYears = _provider.ExperienceYears,
            Skills = _provider.Skills,
            CategoryId = _provider.CategoryId,
            City = _provider.City,
            ServiceAreas = _provider.ServiceAreas,
            IsAcceptingBookings = _provider.IsAcceptingBookings,
            ProfileImagePath = _provider.User.ProfileImagePath,
            Email = _provider.User.Email ?? string.Empty,
            VerificationStatus = _provider.VerificationStatus
        });
    }

    [HttpPost]
    public async Task<IActionResult> Profile(ProviderProfileInputModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Professional profile";
        if (ModelState.IsValid)
        {
            var result = await providers.UpdateProfileAsync(User.GetUserId(), model, ct);
            if (result.Succeeded)
            {
                var user = await userManager.GetUserAsync(User);
                if (user is not null) await signInManager.RefreshSignInAsync(user);
                TempData["Success"] = "Profile saved.";
                return RedirectToAction(nameof(Profile));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        model.ProfileImagePath = _provider.User.ProfileImagePath;
        model.Email = _provider.User.Email ?? string.Empty;
        model.VerificationStatus = _provider.VerificationStatus;
        await LoadListsAsync(ct);
        return View(model);
    }

    // ------------------------------------------------------------------ services

    [HttpGet]
    public async Task<IActionResult> Services(int? edit, CancellationToken ct)
    {
        ViewData["Title"] = "My services";
        var list = await providers.GetServicesAsync(_provider.Id, ct);
        var editing = edit.HasValue ? list.FirstOrDefault(s => s.Id == edit) : null;
        ViewBag.Services = list;
        ViewBag.Categories = await categories.GetActiveAsync(ct);
        return View(editing is null
            ? new ServiceInputModel { CategoryId = _provider.CategoryId }
            : new ServiceInputModel
            {
                Id = editing.Id, Title = editing.Title, CategoryId = editing.CategoryId, Description = editing.Description,
                Price = editing.Price, PriceUnit = editing.PriceUnit, DurationMinutes = editing.DurationMinutes, IsActive = editing.IsActive
            });
    }

    [HttpPost]
    public async Task<IActionResult> Services(ServiceInputModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await providers.SaveServiceAsync(_provider.Id, model, ct);
            if (result.Succeeded)
            {
                TempData["Success"] = model.Id is > 0 ? "Service updated." : "Service added.";
                return RedirectToAction(nameof(Services));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        ViewData["Title"] = "My services";
        ViewBag.Services = await providers.GetServicesAsync(_provider.Id, ct);
        ViewBag.Categories = await categories.GetActiveAsync(ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteService(int id, CancellationToken ct)
    {
        var result = await providers.DeleteServiceAsync(_provider.Id, id, ct);
        if (result.Succeeded) TempData["Success"] = "Service removed. Services with past bookings are hidden instead of deleted.";
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Services));
    }

    // ------------------------------------------------------------------ availability

    [HttpGet]
    public async Task<IActionResult> Availability(DateOnly? from, CancellationToken ct)
    {
        ViewData["Title"] = "Availability";
        var start = from ?? tz.LocalToday;
        return View(await providers.GetAvailabilityAsync(_provider.Id, start, 14, ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddSlots([Bind(Prefix = "AddSlots")] AddSlotsInputModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Availability));
        }
        var result = await providers.AddSlotsAsync(_provider.Id, input, ct);
        if (result.Succeeded) TempData["Success"] = $"{result.Value} time slot{(result.Value == 1 ? "" : "s")} added.";
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Availability), new { from = input.FromDate?.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveSlot(int id, DateOnly? from, CancellationToken ct)
    {
        var result = await providers.RemoveSlotAsync(_provider.Id, id, ct);
        if (result.Succeeded) TempData["Success"] = "Time slot removed.";
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Availability), new { from = from?.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    public async Task<IActionResult> ClearDay(DateOnly date, DateOnly? from, CancellationToken ct)
    {
        var result = await providers.RemoveFreeSlotsOnDateAsync(_provider.Id, date, ct);
        TempData["Success"] = $"{result.Value} free slot{(result.Value == 1 ? "" : "s")} removed from {date:ddd d MMM}. Booked slots were kept.";
        return RedirectToAction(nameof(Availability), new { from = from?.ToString("yyyy-MM-dd") });
    }

    // ------------------------------------------------------------------ bookings

    public async Task<IActionResult> Requests([FromQuery] BookingQuery query, CancellationToken ct)
    {
        ViewData["Title"] = "Booking requests";
        query.Status = "pending";
        ViewData["Query"] = query;
        return View("Jobs", await bookings.QueryAsync(BookingScope.Provider, User.GetUserId(), query, ct));
    }

    /// <summary>Upcoming jobs (?status=upcoming) and completed jobs (?status=completed).</summary>
    public async Task<IActionResult> Jobs([FromQuery] BookingQuery query, CancellationToken ct)
    {
        query.Status ??= "upcoming";
        ViewData["Title"] = query.Status switch
        {
            "completed" => "Completed jobs",
            "history" => "Job history",
            "confirmed" => "Confirmed jobs",
            _ => "Upcoming jobs"
        };
        ViewData["Query"] = query;
        return View(await bookings.QueryAsync(BookingScope.Provider, User.GetUserId(), query, ct));
    }

    public async Task<IActionResult> Earnings(CancellationToken ct)
    {
        ViewData["Title"] = "Earnings";
        return View(await providers.GetEarningsAsync(_provider.Id, ct));
    }

    public async Task<IActionResult> Reviews(int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Reviews";
        ViewBag.Provider = _provider;
        return View(await reviews.GetProviderReviewsAsync(_provider.Id, page, 10, ct));
    }

    // ------------------------------------------------------------------ verification

    [HttpGet]
    public async Task<IActionResult> Verification(CancellationToken ct)
    {
        ViewData["Title"] = "Verification";
        return View(await providers.GetVerificationAsync(_provider.Id, ct));
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument([Bind(Prefix = "Upload")] DocumentUploadInputModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid || input.File is null)
        {
            TempData["Error"] = "Choose a document type and a PDF, JPG or PNG file.";
            return RedirectToAction(nameof(Verification));
        }
        var result = await providers.UploadDocumentAsync(_provider.Id, input.DocumentType, input.File, ct);
        if (result.Succeeded) TempData["Success"] = "Document uploaded.";
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Verification));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken ct)
    {
        var result = await providers.DeleteDocumentAsync(_provider.Id, id, ct);
        if (result.Succeeded) TempData["Success"] = "Document deleted.";
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Verification));
    }

    [HttpPost]
    public async Task<IActionResult> SubmitVerification(CancellationToken ct)
    {
        var result = await providers.SubmitForVerificationAsync(_provider.Id, ct);
        if (result.Succeeded) TempData["Success"] = "Submitted for review. We'll notify you once an admin has checked your documents.";
        else TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Verification));
    }

    /// <summary>Streams one of the provider's own private documents.</summary>
    [HttpGet]
    public async Task<IActionResult> Document(int id, CancellationToken ct)
    {
        var result = await providers.OpenDocumentAsync(id, User.GetUserId(), false, ct);
        if (!result.Succeeded) return result.ErrorKind == ServiceErrorKind.Forbidden ? Forbid() : NotFound();
        var (stream, contentType, fileName) = result.Value;
        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(fileName)}\"";
        return File(stream, contentType);
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        ViewBag.Categories = await categories.GetActiveAsync(ct);
        ViewBag.Cities = await categories.GetCitiesAsync(ct);
    }
}
