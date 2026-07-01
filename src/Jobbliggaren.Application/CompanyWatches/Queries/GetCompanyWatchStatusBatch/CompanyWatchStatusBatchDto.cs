namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;

/// <summary>
/// #311 #455 (ADR 0087 D8(c); senior-cto-advisor 2026-07-01) — per-job-ad follow-state overlay for the
/// current user. Deliberately carries NO org.nr: the FE needs only (a) whether the ad's employer is
/// followable and (b) the opaque <c>CompanyWatchId</c> to unfollow via the existing DELETE-by-id. The
/// raw org.nr never leaves the server (D8(c)), so no member of this DTO is org.nr-shaped —
/// <c>OrganizationNumberSurfacingGuardTests</c> stays green by construction.
/// </summary>
public sealed record CompanyWatchStatusBatchDto(IReadOnlyList<CompanyWatchStatusDto> Statuses);

/// <summary>
/// One ad's follow-state. <paramref name="CompanyWatchId"/> is <c>null</c> when the current user does
/// not follow this ad's employer (the follow POST returns the id); <paramref name="Followable"/> is
/// <c>false</c> when the ad carries no employer org.nr (B2 not-re-ingested) — the FE hides the affordance.
/// </summary>
public sealed record CompanyWatchStatusDto(Guid JobAdId, Guid? CompanyWatchId, bool Followable);
