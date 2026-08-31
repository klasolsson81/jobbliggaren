using Jobbliggaren.Application.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C + ADR 0067 Beslut 5a + #1546 — utökad typeahead-union. Slår ihop
/// (i) taxonomi-snapshot-labels (Län/Kommun/Yrkesområde/Yrkesgrupp, in-memory
/// ACL — ADR 0043), (ii) arbetsgivare (#1546, Active-only, bakom en egen
/// ≥3-grind och en egen budget) och (iii) lokal <c>job_ads.Title</c>
/// ILIKE-prefix (ADR 0042 Beslut C, oförändrad gren). Returnerar
/// <see cref="SuggestionDto"/> per förslag. Additiv utökning av ADR 0042
/// Beslut C — korsref, ej supersession; titel-vägen består.
/// DoS-skydd: min prefix ≥2 + Limit-cap (validator) + JobAdSuggestPolicy
/// rate-limit (endpoint, #1546 — INTE den delade SuggestPolicy) +
/// LIKE-metateckens-escaping (titel-grenen).
/// </summary>
public sealed record SuggestJobAdTermsQuery(string Prefix, int Limit = 10)
    : IQuery<IReadOnlyList<SuggestionDto>>;
