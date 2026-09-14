namespace Jobbliggaren.Application.CompanyWatches.Queries.ResolveOccupationDivisions;

/// <summary>
/// The wire shape of <see cref="ResolveOccupationDivisionsQuery"/>. <see cref="Occupations"/> is the
/// deriver's ranked candidate list; an empty list means the word is not an occupation the taxonomy
/// knows, and the picker then behaves exactly as before. Several candidates are a CHOICE the user
/// makes, never a pick the server makes for her (AGENTS.md §5 — SSYK derivation without user
/// confirmation; ADR 0040 Beslut 4).
/// </summary>
public sealed record OccupationDivisionsDto(
    string Word,
    IReadOnlyList<OccupationDivisionCandidateDto> Occupations);

/// <summary>
/// One occupation group the word resolved to, with its evidence (<see cref="MatchedOn"/> — the
/// occupation-name label that grounded the match, explainable by design) and its profile in one of
/// three states. The nullable members are non-null exactly under the state that has them, inherited
/// from <c>OccupationDivisionProfile</c>. Every profiled ad is in exactly one of three places —
/// <see cref="Divisions"/>, <see cref="BelowThresholdAdCount"/> (real huvudgrupper under the share
/// cut) or <see cref="WithoutDivisionAdCount"/> (no huvudgrupp known) — so the surface can show the
/// whole denominator and never a distribution that reads as complete while a third of it is missing. Names stay Swedish in every locale (ADR 0137 decision 2 —
/// register data is a proper noun); <see cref="Label"/> is the group's taxonomy label.
/// </summary>
public sealed record OccupationDivisionCandidateDto(
    string OccupationGroupConceptId,
    string Label,
    string MatchedOn,
    string State,
    int? TotalAds,
    IReadOnlyList<DivisionShareDto>? Divisions,
    int? BelowThresholdAdCount,
    int? BelowThresholdSharePercent,
    int? WithoutDivisionAdCount,
    int? WithoutDivisionSharePercent,
    DateTimeOffset? ProfiledAt)
{
    public const string StateProfiled = "profiled";
    public const string StateTooFewAds = "tooFewAds";
    public const string StateNotProfiled = "notProfiled";
}

/// <summary>
/// One huvudgrupp and its share of the occupation group's ads. The share is computed ONCE, here,
/// so the two surfaces that may render it cannot round it differently; the count travels with it so
/// the share is never an opaque number (AGENTS.md §5). The code, never the name: the picker holds
/// every division name from the reference tree.
/// </summary>
public sealed record DivisionShareDto(string Code, int AdCount, int SharePercent);
