using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LocalServicesMarketplace.Controllers;

/// <summary>
/// Booking workflow pages and actions shared by all roles. Every rule is enforced in
/// <see cref="IBookingService"/> on the server - the UI only hides buttons that would fail.
/// </summary>
[Authorize]
public class BookingController(
    IBookingService bookings,
    IProviderService providers,
    IReviewService reviews,
    UserManager<User> userManager) : Controller
{
    // ------------------------------------------------------------------ create

    [HttpGet, Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Create(int providerId, int? serviceId, int? slotId, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        var model = new BookingCreateViewModel
        {
            ProviderId = providerId,
            ServiceId = serviceId,
            TimeSlotId = slotId,
            Address = user?.Address ?? string.Empty,
            City = user?.City ?? string.Empty
        };
        if (!await PopulateAsync(model, ct)) return NotFound();
        ViewData["Title"] = $"Book {model.Provider!.User.FullName}";
        return View(model);
    }

    [HttpPost, Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Create(BookingCreateViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await bookings.CreateAsync(User.GetUserId(),
                new CreateBookingCommand(model.ProviderId, model.ServiceId!.Value, model.TimeSlotId!.Value, model.Address, model.City, model.Notes), ct);
            if (result.Succeeded)
            {
                TempData["Success"] = "Booking request sent. You'll be notified as soon as the professional accepts.";
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
            if (result.ErrorKind == ServiceErrorKind.Conflict) model.TimeSlotId = null;
        }

        if (!await PopulateAsync(model, ct)) return NotFound();
        ViewData["Title"] = $"Book {model.Provider!.User.FullName}";
        return View(model);
    }

    // ------------------------------------------------------------------ details

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await bookings.GetDetailsAsync(id, User.GetUserId(), User.IsInRole(AppRoles.Admin), ct);
        if (!result.Succeeded) return ToErrorResult(result);
        ViewData["Title"] = $"Booking {result.Value!.Booking.Reference}";
        return View(result.Value);
    }

    // ------------------------------------------------------------------ customer actions

    [HttpPost, Authorize(Roles = AppRoles.Customer + "," + AppRoles.Admin)]
    public async Task<IActionResult> Cancel(int id, string? reason, string? returnUrl, CancellationToken ct)
    {
        var asAdmin = User.IsInRole(AppRoles.Admin);
        var result = await bookings.CancelAsync(User.GetUserId(), id, reason, asAdmin, ct);
        Flash(result, "Booking cancelled. The time slot has been released.");
        return BackTo(returnUrl, id);
    }

    [HttpGet, Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Reschedule(int id, CancellationToken ct)
    {
        var details = await bookings.GetDetailsAsync(id, User.GetUserId(), false, ct);
        if (!details.Succeeded) return ToErrorResult(details);
        if (!details.Value!.CanReschedule)
        {
            TempData["Error"] = "This booking can no longer be rescheduled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewData["Title"] = "Choose a new time";
        return View(new RescheduleViewModel
        {
            BookingId = id,
            Booking = details.Value.Booking,
            SlotDays = await bookings.GetAvailableSlotsAsync(details.Value.Booking.ProviderId, 14, id, ct)
        });
    }

    [HttpPost, Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Reschedule(RescheduleViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await bookings.RescheduleAsync(User.GetUserId(), model.BookingId, model.NewTimeSlotId!.Value, ct);
            if (result.Succeeded)
            {
                TempData["Success"] = "Booking moved. The professional needs to accept the new time.";
                return RedirectToAction(nameof(Details), new { id = model.BookingId });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        var details = await bookings.GetDetailsAsync(model.BookingId, User.GetUserId(), false, ct);
        if (!details.Succeeded) return ToErrorResult(details);
        model.Booking = details.Value!.Booking;
        model.SlotDays = await bookings.GetAvailableSlotsAsync(model.Booking.ProviderId, 14, model.BookingId, ct);
        ViewData["Title"] = "Choose a new time";
        return View(model);
    }

    [HttpGet, Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Review(int id, CancellationToken ct)
    {
        var eligible = await reviews.GetReviewableBookingAsync(User.GetUserId(), id, ct);
        if (!eligible.Succeeded)
        {
            if (eligible.ErrorKind is ServiceErrorKind.NotFound or ServiceErrorKind.Forbidden) return ToErrorResult(eligible);
            TempData["Error"] = eligible.Error;
            return RedirectToAction(nameof(Details), new { id });
        }
        ViewData["Title"] = "Rate your experience";
        return View(new ReviewInputModel { BookingId = id, Booking = eligible.Value });
    }

    [HttpPost, Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Review(ReviewInputModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await reviews.SubmitAsync(User.GetUserId(), model.BookingId, model.Rating, model.Comment, ct);
            if (result.Succeeded)
            {
                TempData["Success"] = "Thanks for your review. It's now live on the professional's profile.";
                return RedirectToAction(nameof(Details), new { id = model.BookingId });
            }
            if (result.ErrorKind is ServiceErrorKind.NotFound or ServiceErrorKind.Forbidden) return ToErrorResult(result);
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        var eligible = await reviews.GetReviewableBookingAsync(User.GetUserId(), model.BookingId, ct);
        if (!eligible.Succeeded)
        {
            TempData["Error"] = eligible.Error;
            return RedirectToAction(nameof(Details), new { id = model.BookingId });
        }
        model.Booking = eligible.Value;
        ViewData["Title"] = "Rate your experience";
        return View(model);
    }

    // ------------------------------------------------------------------ provider actions

    [HttpPost, Authorize(Roles = AppRoles.Provider)]
    public async Task<IActionResult> Accept(int id, string? returnUrl, CancellationToken ct)
    {
        Flash(await bookings.AcceptAsync(User.GetUserId(), id, ct), "Booking accepted. The customer has been notified.");
        return BackTo(returnUrl, id);
    }

    [HttpPost, Authorize(Roles = AppRoles.Provider)]
    public async Task<IActionResult> Reject(int id, string? reason, string? returnUrl, CancellationToken ct)
    {
        Flash(await bookings.RejectAsync(User.GetUserId(), id, reason, ct), "Booking declined. The customer has been notified.");
        return BackTo(returnUrl, id);
    }

    [HttpPost, Authorize(Roles = AppRoles.Provider)]
    public async Task<IActionResult> Complete(int id, string? returnUrl, CancellationToken ct)
    {
        Flash(await bookings.CompleteAsync(User.GetUserId(), id, ct), "Job marked as completed. Earnings updated.");
        return BackTo(returnUrl, id);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<bool> PopulateAsync(BookingCreateViewModel model, CancellationToken ct)
    {
        var details = await providers.GetDetailsAsync(model.ProviderId, User.GetUserId(), false, ct);
        if (details is null || details.IsPreview) return false;
        model.Provider = details.Provider;
        model.Services = details.Services;
        model.SlotDays = details.SlotDays;
        if (model.ServiceId is null && details.Services.Count == 1) model.ServiceId = details.Services[0].Id;
        ViewBag.CanBook = details.ViewerCanBook;
        return true;
    }

    private void Flash(ServiceResult result, string success)
    {
        if (result.Succeeded) TempData["Success"] = success;
        else TempData["Error"] = result.Error;
    }

    private IActionResult BackTo(string? returnUrl, int bookingId) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Details), new { id = bookingId });

    private IActionResult ToErrorResult(ServiceResult result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(),
        ServiceErrorKind.Forbidden => Forbid(),
        _ => BadRequest(result.Error)
    };
}
