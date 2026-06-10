using JobbPilot.Application.Common;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C + ADR 0067 Beslut 5a — utökad typeahead-union. Slår ihop
/// (i) taxonomi-snapshot-labels (Län/Kommun/Yrkesområde/Yrkesgrupp, in-memory
/// ACL — ADR 0043) och (ii) lokal <c>job_ads.Title</c> ILIKE-prefix (ADR 0042
/// Beslut C, oförändrad gren). Returnerar <see cref="SuggestionDto"/> per
/// förslag (<c>{kind, conceptId, label}</c>). Additiv utökning av ADR 0042
/// Beslut C — korsref, ej supersession; titel-vägen består.
/// DoS-skydd: min prefix ≥2 + Limit-cap (validator) + SuggestPolicy
/// rate-limit (endpoint) + LIKE-metateckens-escaping (titel-grenen).
/// </summary>
public sealed record SuggestJobAdTermsQuery(string Prefix, int Limit = 10)
    : IQuery<IReadOnlyList<SuggestionDto>>;
