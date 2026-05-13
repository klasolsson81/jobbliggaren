using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;

namespace JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;

/// <summary>
/// Race-säker upsert av en extern JobAd per ADR 0032 §5. Triggad per-item av
/// <c>SyncPlatsbankenStreamJob</c> + <c>SyncPlatsbankenSnapshotJob</c> samt
/// (efter P8b-handler-refaktor) av admin-trigger-flödet via snapshot-handlern.
///
/// System-command — saknar medvetet <c>IAuthenticatedRequest</c> (anropas från
/// Hangfire-worker utan inloggad user, samma mönster som <c>MarkGhostedCommand</c>).
/// Får INTE exponeras via API-endpoint utan dedikerad RBAC-policy.
///
/// Aggregerad audit per job-run skrivs av orchestrator (CTO-rond 6 punkt 1 +
/// ADR 0032 §8 — per-item audit hade gett ~50k rader/dygn vilket är spam mot
/// GDPR Art. 30 syfte). Denna command är därför INTE <c>IAuditableCommand</c>.
/// </summary>
public sealed record UpsertExternalJobAdCommand(
    JobSource Source,
    string ExternalId,
    JobAdImportItem Item)
    : ICommand<Result<UpsertOutcome>>;
