using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;

namespace JobbPilot.Application.JobAds.Commands.ArchiveExternalJobAd;

/// <summary>
/// Arkivera en extern JobAd per ADR 0032 §6 — soft-archive bevarar
/// arbetsmarknad-historik. Triggad per stream-event av typen
/// <c>JobAdRemoval</c> från <c>SyncPlatsbankenStreamJob</c>.
///
/// System-command (samma mönster som <see cref="UpsertExternalJobAdCommand"/>) —
/// inget <c>IAuthenticatedRequest</c>. Audit aggregeras per job-run, inte per
/// item. <c>JobAd.Archive</c> är idempotent (returnerar Failure vid redan
/// arkiverad — handlern översätter till <see cref="ArchiveOutcome.AlreadyArchived"/>).
/// </summary>
public sealed record ArchiveExternalJobAdCommand(
    JobSource Source,
    string ExternalId)
    : ICommand<Result<ArchiveOutcome>>;
