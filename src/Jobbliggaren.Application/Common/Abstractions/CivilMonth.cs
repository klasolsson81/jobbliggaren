namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// A Swedish civil (year, month) — a LABEL, never an instant, and deliberately
/// not convertible to one. The instant lives behind
/// <see cref="ISwedishCalendar"/>, because only the adapter knows the zone.
///
/// <para>
/// <b>Why this type exists rather than an <c>(int, int)</c> tuple</b>
/// (CTO-bind 2026-07-28-B). The predecessor member <c>StartOfMonth(int, int)</c>
/// returned a bare <see cref="DateTimeOffset"/> whose entire contract was three
/// prose prohibitions: do not read <c>.Year</c>/<c>.Month</c> off it, do not
/// <c>AddMonths</c> it as a series, do not <c>AddMonths(1)</c> it for a window's
/// exclusive end. Both prospective consumers wrote two of the three anyway. A
/// value whose correct use requires the caller to internalise three "never do X"
/// rules is at the wrong level of abstraction; a doc comment is a warning label
/// on an edge that should not exist. Splitting the LABEL from the INSTANT is
/// what removes the edge — and it is CLAUDE.md §5's named anti-pattern
/// (primitive obsession) read literally, since a
/// <see cref="DateTimeOffset"/> boundary carries two incompatible meanings in
/// one primitive.
/// </para>
///
/// <para>
/// <b>Stepping is called <see cref="Next"/>/<see cref="Previous"/>, NOT
/// AddMonths.</b> That name is forbidden on boundary instants, and a homonym
/// would make the prohibition un-greppable. Stepping a civil month is the only
/// month arithmetic that is safe, and it lives here so no consumer writes it:
/// before this type, the two call sites needed three hand-written
/// December-to-January rollovers between them.
/// </para>
///
/// <para>
/// <b>Range.</b> <see cref="Of"/> guards <paramref name="month"/> to 1-12 and
/// the year to <see cref="DateTime"/>'s own domain — nothing narrower. The
/// 2000-2100 bound a caller may expect is POLICY and stays where it is stated,
/// in <c>GetActivityReportQueryValidator</c>; duplicating it here would give the
/// rule two homes. <c>default(CivilMonth)</c> bypasses the factory (structs
/// always can) and is year 0, month 1 — which no calendar accepts. It fails
/// closed at the first operation that touches it: <see cref="Next"/> routes back
/// through <see cref="Of"/> and throws before the adapter is reached at all.
/// Pinned by <c>CivilMonthTests</c>, and recorded here so a reviewer reads it as
/// considered rather than unguarded.
/// </para>
///
/// <para>
/// Held as a single months-since-year-zero index so stepping is addition and the
/// rollover has no branch to get wrong, and so record-struct equality is over
/// the one field rather than two that could disagree.
/// </para>
/// </summary>
public readonly record struct CivilMonth
{
    private readonly int _monthsSinceYearZero;

    private CivilMonth(int monthsSinceYearZero) => _monthsSinceYearZero = monthsSinceYearZero;

    public int Year => _monthsSinceYearZero / 12;

    public int Month => _monthsSinceYearZero % 12 + 1;

    /// <summary>
    /// The Swedish civil month <paramref name="year"/>/<paramref name="month"/>.
    /// Fails loud on anything outside the calendar (precedent:
    /// <see cref="Jobbliggaren.Application.KnowledgeBank.Abstractions.RubricVersion"/>).
    /// </summary>
    public static CivilMonth Of(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return new CivilMonth(year * 12 + (month - 1));
    }

    /// <summary>The following civil month. December rolls the year.</summary>
    public CivilMonth Next() => FromIndex(_monthsSinceYearZero + 1);

    /// <summary>The preceding civil month. January rolls the year back.</summary>
    public CivilMonth Previous() => FromIndex(_monthsSinceYearZero - 1);

    // Routed through Of so a step off the end of the calendar throws HERE, at the
    // operation, rather than downstream in the adapter's DateTime construction.
    private static CivilMonth FromIndex(int index) => Of(index / 12, index % 12 + 1);

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
