using System.Diagnostics;
using System.Net;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Controllers;

[AllowAnonymous]
public class HomeController(
    IReportService reports,
    ICategoryService categories,
    IEmailService email,
    IOptions<SmtpOptions> smtp,
    ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Find trusted professionals for every need";
        return View(await reports.GetHomeAsync(ct));
    }

    public IActionResult About()
    {
        ViewData["Title"] = "About us";
        return View();
    }

    [Route("become-a-provider")]
    public async Task<IActionResult> BecomeProvider(CancellationToken ct)
    {
        ViewData["Title"] = "Become a professional";
        ViewBag.Categories = await categories.GetActiveTilesAsync(ct);
        return View();
    }

    [HttpGet]
    public IActionResult Contact()
    {
        ViewData["Title"] = "Contact us";
        var model = new ContactViewModel();
        if (User.Identity?.IsAuthenticated == true)
        {
            model.Name = User.DisplayName();
            model.Email = User.Identity.Name ?? string.Empty;
        }
        return View(model);
    }

    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Contact(ContactViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Contact us";
        if (!ModelState.IsValid) return View(model);

        var body = $"<p><strong>From:</strong> {WebUtility.HtmlEncode(model.Name)} &lt;{WebUtility.HtmlEncode(model.Email)}&gt;</p>" +
                   $"<p><strong>Subject:</strong> {WebUtility.HtmlEncode(model.Subject)}</p>" +
                   $"<p>{WebUtility.HtmlEncode(model.Message).Replace("\n", "<br>")}</p>";
        await email.SendAsync(smtp.Value.SupportAddress, $"[Contact] {model.Subject}", body, ct);
        logger.LogInformation("Contact form submitted by {Email}", model.Email);

        TempData["Success"] = "Message sent. Our support team replies within one working day.";
        return RedirectToAction(nameof(Contact));
    }

    public IActionResult Terms()
    {
        ViewData["Title"] = "Terms and cancellation policy";
        return View();
    }

    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["Title"] = "Something went wrong";
        ViewBag.TraceId = HttpContext.Items["ErrorTraceId"] as string ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        ViewBag.Details = HttpContext.Items["ErrorMessage"] as string;
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }

    [IgnoreAntiforgeryToken]
    [Route("Home/StatusCode/{code:int}")]
    public new IActionResult StatusCode(int code)
    {
        ViewData["Title"] = code == 404 ? "Page not found" : $"Error {code}";
        ViewBag.Code = code;
        Response.StatusCode = code;
        return View("StatusCode");
    }
}
