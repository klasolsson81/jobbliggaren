using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAdExtractedTerms;

/// <summary>
/// Fas 4 STEG 4 (F4-4) — reads a job ad's persisted deterministic keyword/skill
/// extraction (the explainable surface, CLAUDE.md §5). Pure read: the extraction
/// is produced at ingest + by the local backfill, not here. The matching engine
/// (F4-6) reads the persisted column directly — this query is the debug/explain
/// surface. Returns <c>null</c> when the ad does not exist (→ 404 at the endpoint,
/// parity <c>GetJobAdQuery</c>); an ad not yet extracted yields an empty term
/// list. The garbage-floor (NotEmpty <see cref="JobAdId"/>) is enforced by
/// <see cref="GetJobAdExtractedTermsQueryValidator"/>.
/// </summary>
public sealed record GetJobAdExtractedTermsQuery(Guid JobAdId)
    : IQuery<JobAdExtractionDto?>;
