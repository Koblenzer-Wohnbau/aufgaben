using Aufgaben.Api.Options;
using Microsoft.Extensions.Options;

namespace Aufgaben.Api.Infrastructure;

/// <summary>
/// Zentrale Zeitquelle. "Heute" wird immer in der konfigurierten Zeitzone berechnet,
/// gespeichert wird in UTC.
/// </summary>
public sealed class AppClock(TimeProvider time, IOptions<AufgabenOptions> options)
{
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow.UtcDateTime, Zone);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    /// <summary>Wandelt ein lokales Datum mit Uhrzeit in einen UTC-Zeitpunkt um.</summary>
    public DateTimeOffset ToUtc(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            // Zeitumstellung im Frühjahr: die Uhrzeit existiert nicht, eine Stunde später nehmen.
            local = local.AddHours(1);
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone), TimeSpan.Zero);
    }

    public DateTimeOffset StartOfDayUtc(DateOnly day) => ToUtc(day, TimeOnly.MinValue);

    public DateTime ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, Zone);
}
