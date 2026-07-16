using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.DeleteCompanyWatchCriterion;

/// <summary>
/// HARD delete via tracked <c>Remove</c> — <b>never <c>ExecuteDeleteAsync</c></b>, which commits
/// outside the pipeline's UnitOfWork and would break delete+audit atomicity: the C-D9 audit row and
/// the removal must land in the SAME transaction or neither (the
/// <c>DeleteApplicationCommandHandler</c>/#782 template, verbatim reasoning). The aggregate has no
/// children — the row itself is the entire payload.
///
/// <para>
/// Owner-scoped (C-D10) with the ADR 0031 probe: cross-user and non-existent ids are the same
/// <c>NotFound</c> to the caller, and the cross-user attempt is logged. The aggregate's
/// <c>SoftDelete</c> is deliberately NOT called anywhere — see its summary; the method and the
/// <c>deleted_at</c> column await the follow-up schema-cleanup migration (G1).
/// </para>
/// </summary>
public sealed class DeleteCompanyWatchCriterionCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DeleteCompanyWatchCriterionCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteCompanyWatchCriterionCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure(DomainError.Validation(
                "CompanyWatchCriterion.Unauthorized", "Användaren är inte autentiserad."));
        }

        var userId = currentUser.UserId.Value;
        var criterionId = new CompanyWatchCriterionId(command.Id);

        // Tracked load — Remove needs the tracked entity (see the class doc).
        var criterion = await db.CompanyWatchCriteria
            .Where(c => c.Id == criterionId && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (criterion is null)
        {
            var existsForSomebodyElse = await db.CompanyWatchCriteria
                .AsNoTracking()
                .AnyAsync(c => c.Id == criterionId, cancellationToken);
            if (existsForSomebodyElse)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "CompanyWatchCriterion", command.Id, userId, "DeleteCompanyWatchCriterion");
            }

            return Result.Failure(DomainError.NotFound("CompanyWatchCriterion", command.Id));
        }

        db.CompanyWatchCriteria.Remove(criterion);
        return Result.Success();
    }
}
