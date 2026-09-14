namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #1682 — the read side of the occupation-group × SNI-division profile that
/// <see cref="IOccupationDivisionProfileBuilder"/> writes. Keyed on ssyk-level-4 occupation-group
/// concept ids, which is why it is its own port and not a method on <c>ICompanyWatchBrowseQuery</c>:
/// that port's methods are keyed on a live criterion predicate or a materialised saved criterion, and
/// a profile read belongs to no criterion, no user and no fingerprint.
///
/// <para>
/// Codes, never names: the picker already holds every huvudgrupp name from the reference tree, and
/// a term that translates travels as a code (ADR 0137 decision 3). Thresholds are applied here at
/// read time, never at write time — the store holds the full measurement, the threshold is product
/// policy, and the two change for different reasons.
/// </para>
/// </summary>
public interface IOccupationDivisionProfileQuery
{
    /// <summary>
    /// One profile per requested occupation group, each with its own state: the floor is a per-group
    /// property, so a request over several groups can answer <see cref="OccupationDivisionProfileState.Profiled"/>
    /// for one and <see cref="OccupationDivisionProfileState.TooFewAds"/> for another. Every requested
    /// id is present in the result; a group the profile holds no rows for is
    /// <see cref="OccupationDivisionProfileState.TooFewAds"/> at zero, not absent. An absent or
    /// over-age run answers <see cref="OccupationDivisionProfileState.NotProfiled"/> for every id
    /// without touching the profile table.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, OccupationDivisionProfile>> GetDivisionProfilesAsync(
        IReadOnlyList<string> occupationGroupConceptIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Three states, deliberately — the surface must never render "not built yet" or "too few ads to
/// say" as a zero or an empty list (ADR 0120, the <c>CriterionMaterialisationState</c> precedent).
/// </summary>
public enum OccupationDivisionProfileState
{
    /// <summary>A real answer; the only state in which a share may be rendered.</summary>
    Profiled = 0,

    /// <summary>
    /// The group has fewer ads than the derived floor, so a share would be an enumeration of
    /// individual employers wearing the grammar of a distribution. An honest refusal, never a zero.
    /// </summary>
    TooFewAds = 1,

    /// <summary>No completed run, or the last run is older than the read-age bound. Unknown.</summary>
    NotProfiled = 2,
}

/// <summary>One huvudgrupp's ad count for one occupation group. The share is the reader's to take
/// against <see cref="OccupationDivisionProfile.TotalAds"/>, so the count and its base travel together.</summary>
public sealed record OccupationDivisionShare(string DivisionCode, int AdCount);

/// <summary>
/// The profile of one occupation group. The nullable members are non-null exactly under the states
/// that have them, enforced in the constructor rather than left to readers (the
/// <c>MaterialisedAdCount</c> idiom). <see cref="NotInRegisterAdCount"/> is a scalar beside
/// <see cref="Divisions"/>, never a member of it: the not-in-register bucket is not a huvudgrupp and
/// must never be checkable, and keeping it off the list makes that unrepresentable rather than a rule.
/// </summary>
public sealed record OccupationDivisionProfile(
    OccupationDivisionProfileState State,
    int? TotalAds,
    IReadOnlyList<OccupationDivisionShare>? Divisions,
    int? NotInRegisterAdCount,
    DateTimeOffset? ProfiledAt)
{
    public int? TotalAds { get; } =
        (State != OccupationDivisionProfileState.NotProfiled) == (TotalAds is not null)
            ? TotalAds
            : throw new ArgumentException(
                "Ett annonstal finns exakt när profilen är byggd: ett tal utan körning vore en siffra "
                + "vi inte har täckning för, och en körning utan tal vore en mätning vi kastade bort.",
                nameof(TotalAds));

    public IReadOnlyList<OccupationDivisionShare>? Divisions { get; } =
        (State == OccupationDivisionProfileState.Profiled) == (Divisions is not null)
            ? Divisions
            : throw new ArgumentException(
                "Huvudgrupper finns exakt när profilen svarar: en lista under en vägran vore en "
                + "fördelning vi just sagt att underlaget inte bär.",
                nameof(Divisions));

    public int? NotInRegisterAdCount { get; } =
        (State == OccupationDivisionProfileState.Profiled) == (NotInRegisterAdCount is not null)
            ? NotInRegisterAdCount
            : throw new ArgumentException(
                "Ej-i-registret-hinken följer huvudgrupperna: den finns exakt när de finns.",
                nameof(NotInRegisterAdCount));

    public DateTimeOffset? ProfiledAt { get; } =
        (State != OccupationDivisionProfileState.NotProfiled) == (ProfiledAt is not null)
            ? ProfiledAt
            : throw new ArgumentException(
                "En tidsstämpel finns exakt när en körning finns.", nameof(ProfiledAt));

    public static OccupationDivisionProfile Profiled(
        int totalAds,
        IReadOnlyList<OccupationDivisionShare> divisions,
        int notInRegisterAdCount,
        DateTimeOffset profiledAt) =>
        new(OccupationDivisionProfileState.Profiled, totalAds, divisions, notInRegisterAdCount, profiledAt);

    public static OccupationDivisionProfile TooFewAds(int totalAds, DateTimeOffset profiledAt) =>
        new(OccupationDivisionProfileState.TooFewAds, totalAds, null, null, profiledAt);

    public static OccupationDivisionProfile NotProfiled { get; } =
        new(OccupationDivisionProfileState.NotProfiled, null, null, null, null);
}
