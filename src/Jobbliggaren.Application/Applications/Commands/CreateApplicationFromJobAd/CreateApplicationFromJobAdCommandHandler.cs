using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;

/// <summary>
/// F6 P5 Punkt 2 Del B — handler för "Har ansökt"-quick-create från
/// JobAd-modal-footer.
///
/// The button says "Har ansökt" (I have applied), so the application is created
/// AND immediately transitioned to <see cref="ApplicationStatus.Submitted"/> —
/// it must NOT linger as a Draft (Klas 2026-06-28: a Draft after clicking "Har
/// ansökt" is misleading). The Draft→Submitted transition stamps
/// <c>AppliedAt</c> (issue #316), so the job appears in the activity report.
///
/// #315 (ADR 0086): this is the capture point for the ad-text SNAPSHOT. The ad's
/// fields are PROJECTED (not the JobAd aggregate materialised — dotnet-architect
/// B2) into a frozen <see cref="AdSnapshot"/> stored on the Application, so the
/// ad content survives the source JobAd being archived. The municipality is
/// captured as the raw <c>MunicipalityConceptId</c> column (#841: an ordinary, C#-written
/// ingest column since 2026-07-13 — it used to be a STORED generated column derived from
/// raw_payload, so applying to an ad past the 30-day horizon froze a permanent NULL into the
/// snapshot that exists precisely to OUTLIVE the ad. That is fixed at the root) (via
/// EF.Property) and resolved to a name on the READ path (ADR 0086 D4, final
/// ruling): the write side stays free of <c>ITaxonomyReadModel</c>, honouring the
/// project's codified read-side-only ACL invariant (TaxonomyAclLayerTests). A
/// missing ad row yields no projection → NotFound — exactly the prior
/// <c>AnyAsync</c> precondition. (It never meant more than that: the JobAd
/// soft-delete filter this used to invoke was vacuous and is now retired, #821.
/// Existence is the whole precondition; an ARCHIVED ad still resolves, and always
/// did.) This is the deliberate write-side amendment of ADR 0048 Beslut
/// (d): the read-path reference-by-id stance is unchanged; the snapshot is an
/// additive, orthogonal write-side concern.
/// </summary>
public sealed class CreateApplicationFromJobAdCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<CreateApplicationFromJobAdCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateApplicationFromJobAdCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("Application.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var jobAdId = new JobAdId(command.JobAdId);

        // Project the snapshot-relevant JobAd fields (ADR 0086 / ADR 0048 Beslut d
        // amendment): a one-time write-side copy, NOT materialising the JobAd
        // aggregate (dotnet-architect B2). The MunicipalityConceptId
        // property is read via EF.Property (the #316 pattern) and FROZEN as-is — it
        // is resolved to a name on the read path, keeping this write handler free
        // of the taxonomy ACL port (ADR 0086 D4). No row → NotFound, replacing the
        // prior AnyAsync existence precondition. JobAd carries no query filter
        // (#821), so this is a pure existence check — an Archived ad still resolves.
        var jobAdData = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == jobAdId)
            .Select(j => new JobAdSnapshotSource(
                j.Title,
                j.Company.Name,
                j.Description,
                j.Url,
                j.Source.Value,
                j.PublishedAt,
                j.ExpiresAt,
                EF.Property<string?>(j, "MunicipalityConceptId")))
            .FirstOrDefaultAsync(cancellationToken);

        if (jobAdData is null)
            return Result.Failure<Guid>(DomainError.NotFound("JobAd", command.JobAdId));

        var snapshot = AdSnapshot.Capture(
            jobAdData.Title,
            jobAdData.Company,
            jobAdData.MunicipalityConceptId,
            jobAdData.Url,
            jobAdData.Source,
            jobAdData.PublishedAt,
            jobAdData.ExpiresAt,
            jobAdData.Description, // sanitised JobAd.Description — NEVER raw_payload (ADR 0086 D5)
            clock.UtcNow);

        var result = DomainApplication.CreateFromJobAd(
            jobSeekerId, jobAdId, snapshot, coverLetter: null, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        // "Har ansökt" → submit immediately (stamps AppliedAt, issue #316). A
        // freshly-created application is Draft; Draft→Submitted is a valid
        // transition.
        var submit = result.Value.TransitionTo(ApplicationStatus.Submitted, clock);
        if (submit.IsFailure)
            return Result.Failure<Guid>(submit.Error);

        db.Applications.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }

    // Private projection shape for the snapshot capture (a record, not an
    // anonymous type, so the EF projection has a named target the rest of the
    // handler reads).
    private sealed record JobAdSnapshotSource(
        string Title,
        string Company,
        string Description,
        string Url,
        string Source,
        DateTimeOffset PublishedAt,
        DateTimeOffset? ExpiresAt,
        string? MunicipalityConceptId);
}
