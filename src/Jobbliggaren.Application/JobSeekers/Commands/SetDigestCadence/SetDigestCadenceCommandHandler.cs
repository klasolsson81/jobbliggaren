using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetDigestCadence;

/// <summary>
/// ADR 0087 D2 — sets the current user's digest cadence. The owner-scoped audited shape of
/// <c>UpdateNotificationConsentCommandHandler</c>: loads the JobSeeker TRACKED so the
/// <c>UnitOfWorkBehavior</c> persists the change, delegates to <c>JobSeeker.SetDigestCadence</c>, and
/// echoes the JobSeeker id for <c>AuditBehavior</c>. NO AI/LLM, no PII.
/// </summary>
public sealed class SetDigestCadenceCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetDigestCadenceCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        SetDigestCadenceCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var set = jobSeeker.SetDigestCadence(command.Cadence, clock);
        if (set.IsFailure)
            return Result.Failure<Guid>(set.Error);

        // Echo the JobSeeker id for the audit row (AuditBehavior.ExtractAggregateId); the endpoint
        // discards the value and returns 204.
        return Result.Success(jobSeeker.Id.Value);
    }
}
