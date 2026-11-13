using System.Globalization;
using Microsoft.Extensions.Options;

namespace LocalServicesMarketplace.Infrastructure;

/// <summary>
/// Converts between UTC (how everything is stored) and the marketplace's local time zone (how it is shown).
/// </summary>
public interface ITimeZoneService
{
    TimeZoneInfo Zone { get; }
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
    DateOnly LocalToday { get; }
    DateTime ToLocal(DateTime utc);
    DateTime ToUtc(DateTime local);
    DateTime ToUtc(DateOnly date, TimeOnly time);
    (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly date);
    string Format(DateTime utc, string format = "dd MMM yyyy, h:mm tt");
    string FormatDate(DateTime utc) => Format(utc, "ddd, dd MMM yyyy");
    string FormatTime(DateTime utc) => Format(utc, "h:mm tt");
    string Relative(DateTime utc);
}

public sealed class TimeZoneService : ITimeZoneService
{
    private readonly TimeProvider _clock;
    private readonly CultureInfo _culture;

    public TimeZoneService(IOptions<MarketplaceOptions> options, TimeProvider clock, ILogger<TimeZoneService> logger)
    {
        _clock = clock;
        _culture = CultureInfo.GetCultureInfo(options.Value.Culture);
        try
        {
            Zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Container without tzdata: fall back to a fixed IST offset so the app still works.
            logger.LogWarning("Time zone {Zone} not found; falling back to UTC+05:30", options.Value.TimeZone);
            Zone = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "IST");
        }
    }

    public TimeZoneInfo Zone { get; }
    public DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    public DateTime LocalNow => ToLocal(UtcNow);
    public DateOnly LocalToday => DateOnly.FromDateTime(LocalNow);

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public DateTime ToUtc(DateOnly date, TimeOnly time) => ToUtc(date.ToDateTime(time));

    public (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly date) =>
        (ToUtc(date, TimeOnly.MinValue), ToUtc(date.AddDays(1), TimeOnly.MinValue));

    public string Format(DateTime utc, string format = "dd MMM yyyy, h:mm tt") =>
        ToLocal(utc).ToString(format, _culture);

    public string Relative(DateTime utc)
    {
        var diff = UtcNow - utc;
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} d ago";
        return Format(utc, "dd MMM yyyy");
    }
}
