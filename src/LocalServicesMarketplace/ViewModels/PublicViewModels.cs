using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Reporting;

namespace LocalServicesMarketplace.ViewModels;

public class HomeViewModel
{
    public IReadOnlyList<CategoryTileVm> Categories { get; init; } = [];
    public IReadOnlyList<ProviderCardVm> FeaturedProviders { get; init; } = [];
    public IReadOnlyList<NextSlotVm> NextAvailable { get; init; } = [];
    public IReadOnlyList<TestimonialVm> Testimonials { get; init; } = [];
    public IReadOnlyList<string> Cities { get; init; } = [];
    public int VerifiedProviders { get; init; }
    public int CompletedJobs { get; init; }
    public decimal AverageRating { get; init; }
}

public class CategoryTileVm
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Icon { get; init; } = "bi-grid";
    public string? ImagePath { get; init; }
    public string? Description { get; init; }
    public int ProviderCount { get; init; }
    public decimal? StartingPrice { get; init; }
}

public class NextSlotVm
{
    public int ProviderId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public decimal Rating { get; init; }
    public int Reviews { get; init; }
    public string? ImagePath { get; init; }
    public DateTime StartUtc { get; init; }
    public decimal? StartingPrice { get; init; }
}

public class TestimonialVm
{
    public string CustomerName { get; init; } = string.Empty;
    public string? City { get; init; }
    public int Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string ServiceTitle { get; init; } = string.Empty;
}

/// <summary>Query-string bound filter model for /services.</summary>
public class ProviderSearchQuery
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public string? City { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public decimal? MinRating { get; set; }
    public DateOnly? AvailableOn { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Q) || !string.IsNullOrWhiteSpace(Category) || !string.IsNullOrWhiteSpace(City)
        || MinPrice.HasValue || MaxPrice.HasValue || MinRating.HasValue || AvailableOn.HasValue;
}

public class ProviderCardVm
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Headline { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string CategorySlug { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public decimal Rating { get; init; }
    public int Reviews { get; init; }
    public int ExperienceYears { get; init; }
    public decimal? StartingPrice { get; init; }
    public string? ImagePath { get; init; }
    public bool IsVerified { get; init; }
    public bool IsAcceptingBookings { get; init; }
    public DateTime? NextSlotUtc { get; init; }
    public string? Skills { get; init; }
    public bool IsSampleData { get; init; }
}

public class ServicesIndexViewModel
{
    public ProviderSearchQuery Query { get; init; } = new();
    public PagedResult<ProviderCardVm> Results { get; init; } = new();
    public IReadOnlyList<CategoryTileVm> Categories { get; init; } = [];
    public IReadOnlyList<string> Cities { get; init; } = [];
    public CategoryTileVm? ActiveCategory { get; init; }
    public IReadOnlyList<ProviderCardVm> RecentlyViewed { get; init; } = [];
}

public class SlotVm
{
    public int Id { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
}

public class SlotDayVm
{
    public DateOnly Date { get; init; }
    public IReadOnlyList<SlotVm> Slots { get; init; } = [];
}

public class ReviewVm
{
    public int Id { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string ServiceTitle { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public int ProviderId { get; init; }
    public int BookingId { get; init; }
    public bool IsSampleData { get; init; }
}

public class ProviderDetailsViewModel
{
    public Provider Provider { get; init; } = null!;
    public IReadOnlyList<Service> Services { get; init; } = [];
    public IReadOnlyList<ReviewVm> Reviews { get; init; } = [];
    public int[] RatingBreakdown { get; init; } = new int[5];
    public IReadOnlyList<SlotDayVm> SlotDays { get; init; } = [];
    public int CompletedJobs { get; init; }
    public bool IsPreview { get; init; }
    public bool ViewerCanBook { get; init; }
}
