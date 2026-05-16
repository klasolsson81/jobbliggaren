using JobbPilot.Application.Common;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C — typeahead C1. Lokal query mot <c>job_ads.Title</c>
/// (ILIKE-prefix, left-anchored). C2 (JobTech taxonomy-API per keystroke)
/// avvisat (CTO). Returnerar distinkta aktiva titel-förslag, capade.
/// DoS-skydd: min prefix ≥2 + Limit-cap (validator) + SuggestPolicy
/// rate-limit (endpoint) + LIKE-metateckens-escaping (handler).
/// </summary>
public sealed record SuggestJobAdTermsQuery(string Prefix, int Limit = 10)
    : IQuery<IReadOnlyList<string>>;
