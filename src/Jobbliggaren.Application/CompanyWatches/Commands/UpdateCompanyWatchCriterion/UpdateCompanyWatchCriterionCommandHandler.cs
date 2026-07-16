using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.UpdateCompanyWatchCriterion;

/// <summary>
/// Partial update, owner-scoped (C-D10). "Criterion does not exist" and "criterion belongs to
/// somebody else" are the same <c>NotFound</c> to the caller — never an existence oracle — while a
/// cross-user attempt is still detected and logged (ADR 0031 probe, the house fetch-then-check
/// idiom). Domain methods own the transitions; this handler only sequences them.
/// </summary>
public sealed class UpdateCompanyWatchCriterionCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<UpdateCompanyWatchCriterionCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateCompanyWatchCriterionCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure(DomainError.Validation(
                "CompanyWatchCriterion.Unauthorized", "Användaren är inte autentiserad."));
        }

        var userId = currentUser.UserId.Value;
        var criterionId = new CompanyWatchCriterionId(command.Id);

        // Tracked load — the aggregate methods mutate the two text[] backing lists in place.
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
                    "CompanyWatchCriterion", command.Id, userId, "UpdateCompanyWatchCriterion");
            }

            return Result.Failure(DomainError.NotFound("CompanyWatchCriterion", command.Id));
        }

        if (command.Criteria is not null)
        {
            var specResult = CompanyWatchCriteriaSpec.Create(
                command.Criteria.SniCodes, command.Criteria.MunicipalityCodes);
            if (specResult.IsFailure)
                return Result.Failure(specResult.Error);

            var updateResult = criterion.UpdateCriteria(specResult.Value, clock);
            if (updateResult.IsFailure)
                return updateResult;
        }

        if (command.Label is not null)
        {
            var renameResult = criterion.Rename(command.Label, clock);
            if (renameResult.IsFailure)
                return renameResult;
        }

        return Result.Success();
    }
}
