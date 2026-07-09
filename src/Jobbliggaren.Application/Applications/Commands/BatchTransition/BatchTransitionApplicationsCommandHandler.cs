using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using ApplicationId = Jobbliggaren.Domain.Applications.ApplicationId;

namespace Jobbliggaren.Application.Applications.Commands.BatchTransition;

/// <summary>
/// All-or-nothing two-phase bulk transition (#630 PR 9, CTO bind Q1): phase 1
/// resolves EVERY requested application (owner-scoped) and throws before any
/// mutation if one is missing or foreign — a thrown exception bypasses
/// UnitOfWorkBehavior's unconditional SaveChanges, so a partial batch can
/// never persist. Phase 2 then mutates all via the same
/// <c>Application.TransitionTo</c> path as the single endpoint.
/// </summary>
public sealed class BatchTransitionApplicationsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<BatchTransitionApplicationsCommand, Result>
{
    public async ValueTask<Result> Handle(
        BatchTransitionApplicationsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Silent dedup of identical (id, target) items (CTO bind Q6): a resent
        // double-click transitions once. Conflicting duplicates (same id,
        // different target) were already rejected by the validator, so First()
        // is the group's only target.
        var targetsById = command.Items
            .GroupBy(i => i.ApplicationId)
            .ToDictionary(
                g => g.Key,
                g => ApplicationStatus.FromName(g.First().TargetStatus));

        // Phase 1 — resolve every aggregate before mutating anything. Per-id
        // equality lookups (PK + owner filter, single-path parity): a
        // Contains() over the strongly-typed ApplicationId does not translate
        // (GetJobAdStatusBatchQueryHandler incident), and materializing the
        // owner's ENTIRE application set would be an unbounded fetch (§5).
        var loaded = new List<(Domain.Applications.Application App, ApplicationStatus Target)>(
            targetsById.Count);
        var missing = new List<Guid>();
        foreach (var (id, target) in targetsById)
        {
            var appId = new ApplicationId(id);
            var app = await db.Applications
                .FirstOrDefaultAsync(
                    a => a.Id == appId && a.JobSeekerId == jobSeekerId, cancellationToken);
            if (app is null)
            {
                missing.Add(id);
                continue;
            }
            loaded.Add((app, target));
        }

        if (missing.Count > 0)
        {
            // IDOR parity with the single path, per missing id: an id that
            // exists but belongs to another user is a distinct cross-user
            // attempt (ops signal); an unknown id is not. The response never
            // distinguishes the two — one uniform 404 for the whole batch,
            // thrown BEFORE any mutation (no enumeration oracle, no partial
            // persist).
            foreach (var id in missing)
            {
                var appId = new ApplicationId(id);
                var exists = await db.Applications
                    .AsNoTracking()
                    .AnyAsync(a => a.Id == appId, cancellationToken);
                if (exists)
                {
                    failedAccessLogger.LogCrossUserAttempt(
                        "Application", id, currentUser.UserId.Value,
                        "BatchTransitionApplications");
                }
            }
            throw new NotFoundException("En eller flera ansökningar hittades inte.");
        }

        // Phase 2 — mutate all. Only reachable when every id resolved as the
        // caller's own. TransitionTo's sole failure path (soft-deleted) is
        // unreachable here — soft-deleted rows are query-filtered and already
        // surfaced as not-found above — so a failure is a server bug: fail
        // loud rather than return an error for mutations the unconditional
        // UnitOfWork SaveChanges would persist anyway (ApplyCvImprovements
        // precedent).
        foreach (var (app, target) in loaded)
        {
            var itemResult = app.TransitionTo(target, clock);
            if (itemResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"BatchTransition phase-2 failure ({itemResult.Error.Code}) for an " +
                    $"owner-resolved aggregate — inconsistent state, refusing partial persist.");
            }
        }

        return Result.Success();
    }
}
