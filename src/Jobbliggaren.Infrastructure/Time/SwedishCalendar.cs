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
/// första hand"). Making it an <c>IOptions</c> value would drag in §11's
/// dev-boot contract (<c>appsettings.Local.json.example</c> plus runbook) for a
/// value nobody will ever set, and would open a fail-open surface a constant
/// cannot have — not a <i>mistyped</i> id, which throws either way, but a
/// <b>valid-but-wrong</b> one: <c>Europe/Oslo</c> shares Sweden's offsets
/// exactly and would never be noticed, and <c>Europe/Helsinki</c> would be an
/// hour off all year. The magic-string half of §5 is not waived but satisfied —
/// the value is named once, here. This is a ruling, not an oversight; contest
/// it in review if you disagree.
/// (An earlier draft argued that a <i>mistyped</i> id would silently yield the
/// wrong day. That is false — it fails closed. <c>dotnet-architect</c> caught
/// it; the conclusion survives on environment-invariance alone.)
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
/// <b>The gate is a test, not a constructor and not the DI line.</b> Resolving
/// the zone at construction would look fail-loud but is not: DI type
/// registration is lazy, so a bad id would surface on first use rather than at
/// boot. <c>SwedishCalendarTests</c> resolves the id explicitly and is what
/// actually fails if a runtime cannot find it. A static-initialiser failure is
/// sticky and wrapped — every later access rethrows the cached
/// <c>TypeInitializationException</c> naming this type, with
/// <c>TimeZoneNotFoundException</c> inside — which is diagnosable enough.
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
    ///
    /// <para>
    /// <b>The result is normalised to <c>Offset == Zero</c>, and that is not
    /// cosmetic.</b> These values are used as parameters in `timestamptz`
    /// comparisons, and Npgsql writes a <see cref="DateTimeOffset"/> to
    /// `timestamp with time zone` <i>only</i> when the offset is zero — a
    /// non-zero one throws. This repository has already been bitten by exactly
    /// that: `PlatsbankenJobSource` normalises JobTech dates at the ACL
    /// boundary for the same reason, and records that the bug was invisible on
    /// a UTC host and fired locally in Sweden at +02:00.
    /// </para>
    /// <para>
    /// Normalising here rather than at each call site keeps the one dangerous
    /// property impossible to get wrong, and makes the port's own contract
    /// ("at the UTC instant the Swedish boundary falls on") literally true of
    /// the representation and not merely of the instant.
    /// </para>
    /// </summary>
    private static DateTimeOffset ToInstant(int year, int month, int day)
    {
        var midnight = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}
