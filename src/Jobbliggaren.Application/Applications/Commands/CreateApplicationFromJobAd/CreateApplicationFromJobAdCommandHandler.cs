using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;

/// <summary>
/// F6 P5 Punkt 2 Del B — handler för quick-create från JobAd-modal-footer.
///
/// Precondition: JobAd existerar och är inte soft-deletad (global query
/// filter på <c>db.JobAds</c> respekteras automatiskt). Använder befintlig
/// <see cref="DomainApplication.Create"/>-factory med <c>jobAdId</c>-arg +
/// <c>coverLetter=null, manualPosting=null</c> — ingen ny Domain-yta krävs
/// (ADR 0048 Beslut d — write-side-disciplin respekterad: ingen
/// snapshot-/duplicering, ADR 0048 in-handler-read-join visar JobAd-
/// metadata på read-vägen).
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

        // Precondition: JobAd måste existera (global query filter exkluderar
        // soft-deletade) — vi vill inte skapa Application kopplad till en
        // borttagen annons. NotFound om saknas.
        var jobAdExists = await db.JobAds
            .AsNoTracking()
            .AnyAsync(j => j.Id == jobAdId, cancellationToken);

        if (!jobAdExists)
            return Result.Failure<Guid>(DomainError.NotFound("JobAd", command.JobAdId));

        var result = DomainApplication.Create(
            jobSeekerId, jobAdId, coverLetter: null, manualPosting: null, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.Applications.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
