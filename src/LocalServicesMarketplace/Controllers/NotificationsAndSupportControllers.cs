using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalServicesMarketplace.Controllers;

[Authorize]
public class NotificationsController(INotificationService notifications) : Controller
{
    public async Task<IActionResult> Index(bool unread = false, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Notifications";
        ViewBag.UnreadOnly = unread;
        return View(await notifications.GetAsync(User.GetUserId(), unread, page, ct));
    }

    /// <summary>Marks a notification read and follows its link.</summary>
    [HttpPost]
    public async Task<IActionResult> Open(int id, CancellationToken ct)
    {
        var link = await notifications.MarkReadAsync(User.GetUserId(), id, ct);
        return !string.IsNullOrEmpty(link) && Url.IsLocalUrl(link) ? LocalRedirect(link) : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(User.GetUserId(), ct);
        TempData["Success"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Complaints / support tickets for customers and providers.</summary>
[Authorize(Roles = AppRoles.Customer + "," + AppRoles.Provider)]
public class SupportController(IComplaintService complaints) : Controller
{
    public async Task<IActionResult> Index(int? bookingId, CancellationToken ct)
    {
        ViewData["Title"] = "Help and complaints";
        await LoadAsync(ct);
        return View(new ComplaintInputModel { BookingId = bookingId });
    }

    [HttpPost]
    public async Task<IActionResult> Index(ComplaintInputModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Help and complaints";
        if (ModelState.IsValid)
        {
            var result = await complaints.CreateAsync(User.GetUserId(), model, ct);
            if (result.Succeeded)
            {
                TempData["Success"] = $"Complaint #{result.Value} submitted. The team will respond here and notify you.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        await LoadAsync(ct);
        return View(model);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        ViewBag.Complaints = await complaints.GetForUserAsync(User.GetUserId(), ct);
        ViewBag.BookingOptions = await complaints.GetBookingOptionsAsync(User.GetUserId(), ct);
    }
}
