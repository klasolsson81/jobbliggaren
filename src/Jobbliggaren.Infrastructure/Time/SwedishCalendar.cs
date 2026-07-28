using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Time;

/// <summary>
/// <see cref="ISwedishCalendar"/> over <see cref="TimeZoneInfo"/>.
///
/// <para>
/// <b>The zone id is a constant, and that is a deliberate exception to the
/// hardcoded-config rule (CTO-bind 2026-07-28).</b> CLAUDE.md §5 forbids
/// hardcoded configuration, and its target is values that vary by environment:
/// connection strings, keys, endpoints. The product's home country does not
/// vary by environment — Klas ratified it as identity ("det är en svensk app i
/// första hand"). Making it an <c>IOptions</c> value would add a fail-open
/// surface, since a mistyped zone id would silently yield the wrong day rather
/// than refusing to start, and would drag in §11's dev-boot contract
/// (<c>appsettings.Local.json.example</c> plus runbook) for a value nobody
/// will ever set. The magic-string half of §5 is answered by naming it once,
/// here. This is a ruling, not an oversight; contest it in review if you
/// disagree.
/// </para>
///
/// <para>
/// <b>IANA id, no Windows fallback.</b> Since .NET 6, <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>
/// converts between IANA and Windows ids through ICU, so <c>Europe/Stockholm</c>
/// resolves on Linux and on Windows 10/11 alike. Production runs
/// <c>aspnet:10.0-noble</c> (ICU and tzdata present) and the solution sets no
/// <c>InvariantGlobalization</c>, which are the two conditions that would break
/// it. A hand-rolled fallback to <c>W. Europe Standard Time</c> would be
/// unreachable code, and a third-party id-conversion package would be a new
/// dependency for a BCL feature.
/// </para>
///
/// <para>
/// <b>The gate is a test, not this constructor.</b> Resolving the zone here
/// would look fail-loud but is not: DI type registration is lazy, so a bad id
/// would surface on first use rather than at boot.
/// <c>SwedishCalendarTests</c> resolves the id explicitly and is what actually
/// fails if a runtime cannot find it.
/// </para>
/// </summary>
public sealed class SwedishCalendar : ISwedishCalendar
{
    /// <summary>
    /// The single place the product's home time zone is named. See the type
    /// remarks for why this is a constant rather than configuration.
    /// </summary>
    public const string ZoneId = "Europe/Stockholm";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    public DateTimeOffset StartOfDay(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone);
        return ToInstant(local.Year, local.Month, local.Day);
    }

    public DateTimeOffset StartOfMonth(int year, int month) => ToInstant(year, month, 1);

    /// <summary>
    /// Midnight on the given Swedish calendar date, as the UTC instant it falls
    /// on. <see cref="TimeZoneInfo.GetUtcOffset(DateTimeOffset)"/> would need an
    /// instant we do not have yet, so the offset is taken for the unspecified
    /// local time — which is unambiguous at midnight (see the port's remarks).
    /// </summary>
    private static DateTimeOffset ToInstant(int year, int month, int day)
    {
        var midnight = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight));
    }
}
