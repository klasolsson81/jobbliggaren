using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateFollowedCompanyNotificationConsent;

/// <summary>
/// ADR 0087 D3/D5 (#311 PR-2b) — sets the current user's company-follow notification consent.
/// Mirrors <c>UpdateNotificationConsentCommandHandler</c>'s owner-scoped audited shape: loads the
/// JobSeeker TRACKED so the <c>UnitOfWorkBehavior</c> persists the change, delegates the GDPR
/// consent stamping to the aggregate (<c>JobSeeker.UpdateFollowedCompanyNotificationConsent</c> —
/// first opt-in immutable Art. 7(1), opt-out records the Art. 7(3) withdrawal time), and echoes the
/// JobSeeker id via <see cref="Result{T}"/> so <c>AuditBehavior</c> can write the audit_log row
/// (ADR 0022). The Worker's follow-dispatch filter (enabled AND withdrawn-null) honours the result
/// on the next nightly pass; withdrawal stops company-follow dispatch immediately. NO AI/LLM, no PII.
/// </summary>
public sealed class UpdateFollowedCompanyNotificationConsentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<UpdateFollowedCompanyNotificationConsentCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        UpdateFollowedCompanyNotificationConsentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        jobSeeker.UpdateFollowedCompanyNotificationConsent(command.Enabled, clock);

        // Echo the JobSeeker id for the audit row (AuditBehavior.ExtractAggregateId); the endpoint
        // discards the value and returns 204.
        return Result.Success(jobSeeker.Id.Value);
    }
}
