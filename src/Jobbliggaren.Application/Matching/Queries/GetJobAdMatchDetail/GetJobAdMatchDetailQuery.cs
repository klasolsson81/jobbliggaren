using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;

/// <summary>
/// F4-16 (ADR 0076 Amendment (b) §5; ADR 0053 Beslut 5 amendment 2026-06-19) — the
/// single-ad, modal-altitude match detail for the job modal. Unlike the page-scoped batch
/// overlay (<c>GetJobAdMatchBatchQuery</c>, which carries only the grade + per-dimension
/// VERDICTS for ≤100 ads), this query also carries the matched/missing STRING evidence per
/// dimension — the explainability payload the F4-16 modal renders ("category-primary match:
/// grade + matched/missing per dimension", never "92% match", never a percentage ring).
/// The strings are detail-altitude: they would bloat a 100-ad list payload, so they ride
/// this dedicated single-ad query instead of the batch DTO (CTO D3).
/// <para>
/// Returns <c>null</c> for an anonymous caller (the modal is auth-gated; the guest modal
/// renders no match section). For an authenticated user the handler computes the FULL score
/// for the one ad and returns the honest per-dimension breakdown — it does NOT short-circuit
/// on an unstated occupation (unlike the batch handler): the modal shows the breakdown plus
/// the "set your preferences" signpost. Reads the primary CV's complete skills via the DEK
/// pipeline (fail-closed) — security-auditor-gated (D4).
/// </para>
/// </summary>
public sealed record GetJobAdMatchDetailQuery(Guid JobAdId) : IQuery<JobAdMatchDetailDto?>;
