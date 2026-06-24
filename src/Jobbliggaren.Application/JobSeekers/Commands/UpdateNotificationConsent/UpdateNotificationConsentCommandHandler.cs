using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;

/// <summary>
/// ADR 0080 Vag 4 PR-6 — sets the current user's background-match notification consent + cadence.
/// Mirrors <c>DeleteAccountCommandHandler</c>'s owner-scoped audited shape: loads the JobSeeker
/// TRACKED so the <c>UnitOfWorkBehavior</c> persists the change, delegates the GDPR consent stamping
/// to the aggregate (<c>JobSeeker.UpdateNotificationConsent</c> — first opt-in immutable, opt-out
/// records the withdrawal time), and echoes the JobSeeker id via <see cref="Result{T}"/> so
/// <c>AuditBehavior</c> can write the audit_log row (ADR 0022). The Worker's dispatch filter
/// (enabled AND withdrawn-null) honours the result on the next nightly scan; withdrawal stops
/// dispatch immediately. NO AI/LLM, no PII.
/// </summary>
public sealed class UpdateNotificationConsentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<UpdateNotificationConsentCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        UpdateNotificationConsentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        jobSeeker.UpdateNotificationConsent(command.Enabled, command.Cadence, clock);

        // Echo the JobSeeker id for the audit row (AuditBehavior.ExtractAggregateId); the endpoint
        // discards the value and returns 204.
        return Result.Success(jobSeeker.Id.Value);
    }
}
