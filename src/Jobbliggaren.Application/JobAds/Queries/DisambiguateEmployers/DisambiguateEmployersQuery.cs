using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;

/// <summary>
/// ADR 0087 D6 (#311 PR-2b C2) — disambiguate same-named employers so a user can pick the correct
/// org.nr to follow/filter (the "Volvo×20" trap: "Volvo" is many legal entities, each with its own
/// org.nr). Given a company-name term, returns the DISTINCT legal entities in the ad corpus that
/// match — one row per org.nr (the canonical key) with the employer name + ad count. Auth-gated at
/// the endpoint (RequireAuthorization); reads PUBLIC ad-corpus data, not owner-scoped. Deterministic,
/// no AI/LLM (ADR 0071). The sole-prop personnummer guard (D8(c)) is applied in the handler at the
/// surfacing boundary; the org.nr disambiguation is a SEPARATE read concern from the search/facet
/// port (ADR 0087 D6/D7 — never folded into IJobAdSearchQuery).
/// </summary>
public sealed record DisambiguateEmployersQuery(string Query)
    : IQuery<IReadOnlyList<EmployerDisambiguationDto>>
{
    /// <summary>Min length of the name term (validator-enforced on the TRIMMED value) — a 1-char
    /// ILIKE would match nearly the whole corpus.</summary>
    public const int MinQueryLength = 2;

    /// <summary>Max length of the name term — bounds the ILIKE pattern.</summary>
    public const int MaxQueryLength = 100;

    /// <summary>
    /// v1 result cap (senior-cto-advisor 2026-07-01) — a disambiguation list is a pick-one
    /// interaction; 50 distinct name-matching legal entities is already past human-usable, so a hard
    /// Take bounds the fetch (§5 no-unpaginated-fetch) without speculative pagination (YAGNI — no FE
    /// consumer yet). The projection orders by ad count desc so the most-prolific (most-likely-
    /// intended) employers surface first.
    /// </summary>
    public const int MaxResults = 50;
}
