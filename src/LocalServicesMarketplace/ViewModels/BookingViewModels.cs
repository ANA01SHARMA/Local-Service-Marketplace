using System.ComponentModel.DataAnnotations;
using LocalServicesMarketplace.Models;

namespace LocalServicesMarketplace.ViewModels;

public record CreateBookingCommand(int ProviderId, int ServiceId, int TimeSlotId, string Address, string City, string? Notes);

public class BookingCreateViewModel
{
    [Required]
    public int ProviderId { get; set; }

    [Required(ErrorMessage = "Choose a service."), Display(Name = "Service")]
    public int? ServiceId { get; set; }

    [Required(ErrorMessage = "Choose a time slot."), Display(Name = "Time slot")]
    public int? TimeSlotId { get; set; }

    [Required, StringLength(300, MinimumLength = 5), Display(Name = "Service address")]
    public string Address { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(1000), Display(Name = "Notes for the professional")]
    public string? Notes { get; set; }

    // Display-only data (re-populated on POST failures)
    public Provider? Provider { get; set; }
    public IReadOnlyList<Service> Services { get; set; } = [];
    public IReadOnlyList<SlotDayVm> SlotDays { get; set; } = [];
}

public class BookingListItemVm
{
    public int Id { get; init; }
    public string Reference => Booking.FormatReference(Id);
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }
    public int ProviderId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ServiceTitle { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public decimal Price { get; init; }
    public decimal ProviderEarning { get; init; }
    public BookingStatus Status { get; init; }
    public string? StatusReason { get; init; }
    public string City { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public bool HasReview { get; init; }
    public int RescheduleCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public bool IsSampleData { get; init; }

    // Computed by the service using BookingPolicy for the current viewer
    public bool CanCancel { get; set; }
    public bool CanReschedule { get; set; }
    public bool CanReview { get; set; }
    public bool CanRespond { get; set; }
    public bool CanComplete { get; set; }
}

public enum BookingScope { Customer, Provider, Admin }

/// <summary>Query-string bound booking filter used by all three dashboards.</summary>
public class BookingQuery
{
    /// <summary>all | upcoming | history | pending | confirmed | completed | cancelled | rejected</summary>
    public string? Status { get; set; }
    public string? Q { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class BookingDetailsViewModel
{
    public Booking Booking { get; init; } = null!;
    public BookingScope ViewerScope { get; init; }
    public bool CanCancel { get; init; }
    public string? CancelBlockedReason { get; init; }
    public bool CanReschedule { get; init; }
    public bool CanReview { get; init; }
    public bool CanRespond { get; init; }
    public bool CanComplete { get; init; }
}

public class RescheduleViewModel
{
    public int BookingId { get; set; }

    [Required(ErrorMessage = "Choose a new time slot."), Display(Name = "New time slot")]
    public int? NewTimeSlotId { get; set; }

    public Booking? Booking { get; set; }
    public IReadOnlyList<SlotDayVm> SlotDays { get; set; } = [];
}

public class ReviewInputModel
{
    [Required]
    public int BookingId { get; set; }

    [Required, Range(1, 5, ErrorMessage = "Choose a rating from 1 to 5 stars.")]
    public int Rating { get; set; }

    [StringLength(1000), Display(Name = "Your review")]
    public string? Comment { get; set; }

    public Booking? Booking { get; set; }
}

public class ComplaintInputModel
{
    [Display(Name = "Related booking")]
    public int? BookingId { get; set; }

    [Required, StringLength(150, MinimumLength = 5)]
    public string Subject { get; set; } = string.Empty;

    [Required, StringLength(2000, MinimumLength = 20), Display(Name = "What happened?")]
    public string Description { get; set; } = string.Empty;
}
