using System.Text.Json;
using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalServicesMarketplace.Controllers;

/// <summary>Public service discovery: /Services?q=&amp;category=&amp;city=&amp;minPrice=... (query-string filtering).</summary>
[AllowAnonymous]
public class ServicesController(IProviderService providers, ICategoryService categories) : Controller
{
    private const string RecentlyViewedKey = "RecentlyViewedProviders";

    public async Task<IActionResult> Index([FromQuery] ProviderSearchQuery query, CancellationToken ct)
    {
        var tiles = await categories.GetActiveTilesAsync(ct);
        var active = tiles.FirstOrDefault(c => string.Equals(c.Slug, query.Category, StringComparison.OrdinalIgnoreCase));
        ViewData["Title"] = active is null ? "Browse services" : active.Name;

        // Session state: remember the providers this visitor opened recently.
        var recentIds = ReadRecentlyViewed();
        var model = new ServicesIndexViewModel
        {
            Query = query,
            Results = await providers.SearchAsync(query, ct),
            Categories = tiles,
            Cities = await categories.GetCitiesAsync(ct),
            ActiveCategory = active,
            RecentlyViewed = query.HasFilters || recentIds.Count == 0 ? [] : await providers.GetCardsAsync(recentIds.Take(4), ct)
        };
        return View(model);
    }

    /// <summary>Provider profile. Reached via the conventional route /providers/{id}.</summary>
    public async Task<IActionResult> Provider(int id, CancellationToken ct)
    {
        var model = await providers.GetDetailsAsync(id, User.TryGetUserId(), User.IsInRole(AppRoles.Admin), ct);
        if (model is null) return NotFound();

        ViewData["Title"] = $"{model.Provider.User.FullName}, {model.Provider.Category.Name.ToLowerInvariant()} in {model.Provider.City}";
        if (!model.IsPreview) RememberViewed(id);
        return View(model);
    }

    private List<int> ReadRecentlyViewed()
    {
        var json = HttpContext.Session.GetString(RecentlyViewedKey);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private void RememberViewed(int providerId)
    {
        var ids = ReadRecentlyViewed();
        ids.Remove(providerId);
        ids.Insert(0, providerId);
        HttpContext.Session.SetString(RecentlyViewedKey, JsonSerializer.Serialize(ids.Take(8)));
    }
}
