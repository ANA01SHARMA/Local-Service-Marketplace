namespace LocalServicesMarketplace.Models;

/// <summary>
/// A bookable window in a provider's calendar. Stored in UTC.
/// A slot is "available" when it is active, in the future and has no active (Pending/Confirmed) booking.
/// </summary>
public class TimeSlot
{
    public int Id { get; set; }

    public int ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>False when the provider removed a slot that still has historical bookings.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
