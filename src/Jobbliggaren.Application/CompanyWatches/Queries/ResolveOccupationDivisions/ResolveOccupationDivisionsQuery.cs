using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ResolveOccupationDivisions;

/// <summary>
/// #1682 — the bransch picker's answer to a typed OCCUPATION word: which ssyk-level-4 occupation
/// groups the word denotes (the delivered <c>IOccupationCodeDeriver</c>, never a second matcher —
/// senior-cto-advisor D2, 2026-09-14) and, per group, where that occupation's employers sit by SNI
/// huvudgrupp, counted from our own ads (<c>IOccupationDivisionProfileQuery</c>). Nothing here is
/// authored: #560 bind 4 stands, and the four properties that keep this a measurement rather than a
/// crosswalk are the surface's — it never preselects, it always carries the count beside the share,
/// it never collapses to one division, and the not-in-register bucket stays visible.
///
/// <para>
/// <c>q</c>-shaped and debounced on the client (form A, D3): the deriver's stemmed, spread-gated
/// matching and the frozen occupation-name → group map cannot be shipped to the client faithfully,
/// so the word travels and ~1 kB comes back. Authenticated because the picker is; the data itself is
/// a corpus statistic and is user-scoped nowhere.
/// </para>
/// </summary>
public sealed record ResolveOccupationDivisionsQuery(string Word)
    : IQuery<OccupationDivisionsDto>, IAuthenticatedRequest
{
    /// <summary>Parity <c>DisambiguateEmployersQuery</c>: two characters is the shortest word worth deriving.</summary>
    public const int MinWordLength = 2;

    public const int MaxWordLength = 100;
}
