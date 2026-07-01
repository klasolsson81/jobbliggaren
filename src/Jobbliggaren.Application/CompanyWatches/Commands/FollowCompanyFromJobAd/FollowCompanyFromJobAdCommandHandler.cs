using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;

/// <summary>
/// #311 #455 (ADR 0087 D3/D8(c)) — resolves the ad's employer org.nr SERVER-SIDE via
/// <see cref="IJobAdEmployerReader"/> and follows it through the shared
/// <see cref="CompanyWatchFollowExecutor"/>. The raw org.nr never leaves this handler: it is not
/// returned, not put in a URL, and not logged (ADR 0087 D8(c) — a sole-prop org.nr can be a
/// personnummer). A followed sole-prop employer is stored plaintext + owner-scoped (D8(b), Klas
/// Art. 32 risk-accept) — the personnummer heuristic gates SURFACING, never the follow itself
/// (D8 rejected excluding sole-prop employers as a feature gap).
/// </summary>
public sealed class FollowCompanyFromJobAdCommandHandler(
    IAppDbContext db,
    IJobAdEmployerReader employerReader,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector)
    : ICommandHandler<FollowCompanyFromJobAdCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        FollowCompanyFromJobAdCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("CompanyWatch.Unauthorized", "Användaren är inte autentiserad."));

        var orgNrByJobAd = await employerReader.GetOrganizationNumbersByJobAdIdsAsync(
            [command.JobAdId], cancellationToken);

        // Absent from the map ⇒ the ad does not exist (or is soft-deleted / retracted) → NotFound (404).
        if (!orgNrByJobAd.TryGetValue(command.JobAdId, out var rawOrgNr))
            return Result.Failure<Guid>(
                DomainError.NotFound("JobAd.NotFound", "Annonsen kunde inte hittas."));

        // B2 (CTO deldom 5): the ad carries no (or a malformed) employer org.nr → not followable.
        // Validation 400, not NotFound/Conflict — the ad exists, there is no state conflict; the request
        // is simply not fulfillable. Backstop against a stale FE that posts a non-followable ad.
        var orgNrResult = OrganizationNumber.Create(rawOrgNr);
        if (orgNrResult.IsFailure)
            return Result.Failure<Guid>(DomainError.Validation(
                "CompanyWatch.EmployerOrganizationNumberMissing",
                "Den här annonsen saknar ett organisationsnummer för arbetsgivaren och kan inte bevakas."));

        return await CompanyWatchFollowExecutor.FollowOrResurrectAsync(
            db, dbExceptionInspector, currentUser.UserId.Value, orgNrResult.Value, clock, cancellationToken);
    }
}
