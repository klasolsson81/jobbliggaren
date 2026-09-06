using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139) — the materialisation orchestrator: resolve every saved criterion against the
/// register, gate it on breadth, filter it at the write boundary, and replace its member set.
/// Hangfire-agnostic (ADR 0023 delbeslut 2; the Worker wrapper carries the attributes), pinned by
/// <c>ScbCompanyRegisterLayerTests.CompanyRegister_infrastructure_does_not_depend_on_Hangfire</c>.
///
/// <para>
/// <b>Per criterion, one at a time, and NOT batched</b> — measured before a line of it was written
/// (<c>docs/reviews/2026-09-06-1681-plan-probe-measurement.md</c>). Batching the N register queries
/// into one statement over a <c>jsonb</c> parameter keeps the GIN index but collapses the join to a
/// <c>Seq Scan on job_ads</c> per criterion, 40-50x worse, because inside a lateral over a function
/// scan the planner has no statistics for the per-criterion arrays — and the arrays sit in a
/// <c>jsonb</c> parameter it cannot look into, so every execution behaves like a generic plan on the
/// SNI axis, permanently. Sequential also fails VISIBLY: N commands under their own timeouts each
/// fail on their own, whereas the batch hangs once, silently.
/// </para>
///
/// <para>
/// <b>A failing criterion does not fail the run.</b> One corrupt or unresolvable criterion must not
/// deny every other user a fresh membership — but the failure is never swallowed either (§5 forbids a
/// catch-all without action): it is logged with the criterion id, counted, and the criterion is left
/// with whatever state it had, which the staleness stamp then reports honestly. This is the typed
/// catch-and-log CLAUDE.md §3.6 sanctions, in the place it sanctions it.
/// </para>
/// </summary>
internal sealed partial class CompanyWatchCriterionMaterialiser(
    AppDbContext db,
    CompanyWatchCriterionMemberStore store,
    IDateTimeProvider clock,
    IOptions<CompanyWatchMaterialisationOptions> options,
    ILogger<CompanyWatchCriterionMaterialiser> logger) : ICompanyWatchCriterionMaterialiser
{
    public async Task<CompanyWatchCriterionMaterialisationResult> MaterialiseAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;

        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return new CompanyWatchCriterionMaterialisationResult(
                0, 0, 0, 0, 0, 0, startedAt, clock.UtcNow);
        }

        // The whole criterion corpus, bounded by construction: CompanyWatchCriterion.MaxPerUser (20)
        // per user, and a criterion is a small row (two text[] axes). AsNoTracking — nothing here
        // mutates the aggregate, and the write path is raw SQL against two other tables entirely.
        var criteria = await db.CompanyWatchCriteria
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var materialised = 0;
        var tooBroad = 0;
        var membersWritten = 0;
        // Named WITHOUT an org.nr token on purpose. OrganizationNumberSurfacingGuardTests scans every
        // Log*() ARGUMENT list on this file for "organization"/"orgnr"/"org_nr"/"personnummer" and
        // cannot tell a COUNT of dropped values from a value. That conservatism is correct — the
        // alternative is teaching the guard to accept token-named arguments, which is how such a guard
        // goes vacuous — so the log surface is kept token-free instead. The domain name survives where
        // it belongs, on CompanyWatchCriterionMaterialisationResult and on the state row.
        var excludedByShapeGuard = 0;
        var excludedInvalid = 0;
        var failed = 0;

        foreach (var criterion in criteria)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = await MaterialiseOneAsync(criterion, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.State == MaterialisationState.TooBroad)
                    tooBroad++;
                else
                    materialised++;

                membersWritten += outcome.MemberCount;
                excludedByShapeGuard += outcome.ExcludedPersonnummerShaped;
                excludedInvalid += outcome.ExcludedInvalid;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Counted, logged WITH the criterion id, and skipped. The id is a Guid — not an
                // org.nr, not a label, nothing user-identifying (ADR 0087 D8(c)); the org.nr-log
                // boundary scan over this file (OrganizationNumberSurfacingGuardTests) is what keeps
                // that true rather than a promise.
                failed++;
                LogCriterionFailed(logger, criterion.Id.Value, ex);
            }
        }

        var result = new CompanyWatchCriterionMaterialisationResult(
            CriteriaSeen: criteria.Count,
            CriteriaMaterialised: materialised,
            CriteriaTooBroad: tooBroad,
            MembersWritten: membersWritten,
            MembersExcludedPersonnummerShaped: excludedByShapeGuard,
            MembersExcludedInvalid: excludedInvalid,
            StartedAt: startedAt,
            CompletedAt: clock.UtcNow);

        LogCompleted(
            logger, result.CriteriaSeen, result.CriteriaMaterialised, result.CriteriaTooBroad,
            result.MembersWritten, excludedByShapeGuard, excludedInvalid, failed,
            (result.CompletedAt - result.StartedAt).TotalSeconds);

        return result;
    }

    /// <summary>
    /// One criterion: select under the gate, filter at the write boundary, replace. The order is
    /// load-bearing — the gate fires on the RAW candidate count, before the personnummer filter, so a
    /// criterion cannot slip under the bound by having candidates dropped. Were it the other way
    /// round, the bound would silently be "1 000 survivors" rather than "1 000 matches", and the
    /// storage argument (Art. 5(1)(c)) would be measured against a number the register can move.
    /// </summary>
    private async Task<CriterionOutcome> MaterialiseOneAsync(
        CompanyWatchCriterion criterion, CancellationToken cancellationToken)
    {
        var candidates = await store.SelectCandidatesAsync(
                criterion.Criteria, CompanyWatchCriterionMember.MaxPerCriterion, cancellationToken)
            .ConfigureAwait(false);

        if (candidates is null)
        {
            // Refused. The member set is still DELETED (the criterion may have been narrow before it
            // was widened), and the state row records the refusal so the read side renders "för bred"
            // instead of a number, or a zero, or nothing at all.
            await store.ReplaceAsync(
                    criterion.Id.Value, [], MaterialisationState.TooBroad, 0, clock.UtcNow,
                    cancellationToken)
                .ConfigureAwait(false);

            return new CriterionOutcome(MaterialisationState.TooBroad, 0, 0, 0);
        }

        var filtered = CompanyWatchCriterionMemberFilter.Apply(candidates);

        await store.ReplaceAsync(
                criterion.Id.Value, filtered.OrganizationNumbers, MaterialisationState.Materialised,
                filtered.ExcludedPersonnummerShaped, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        // Hoisted into a token-free local for the reason given at excludedByShapeGuard above: the
        // value logged is a COUNT, and the scan reads argument identifiers, not types.
        var droppedByShapeGuard = filtered.ExcludedPersonnummerShaped;
        if (droppedByShapeGuard > 0)
        {
            // Expected NEVER to fire: the register is legal-entities-only at ingest (ADR 0091). If it
            // does, the ingest guard has a hole, and that is a security event rather than a
            // housekeeping note — hence Warning, and hence a count rather than a value.
            LogPersonnummerShapedExcluded(logger, criterion.Id.Value, droppedByShapeGuard);
        }

        return new CriterionOutcome(
            MaterialisationState.Materialised,
            filtered.OrganizationNumbers.Count,
            filtered.ExcludedPersonnummerShaped,
            filtered.ExcludedInvalid);
    }

    private readonly record struct CriterionOutcome(
        MaterialisationState State, int MemberCount, int ExcludedPersonnummerShaped, int ExcludedInvalid);

    [LoggerMessage(
        EventId = 6420,
        Level = LogLevel.Information,
        Message = "Materialisering av smarta bevakningar är avstängd (CompanyWatchMaterialisation:Enabled=false). "
                + "Befintliga medlemsmängder blir gamla och läsvägen ska rapportera det, aldrig en nolla.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 6421,
        Level = LogLevel.Information,
        Message = "Materialisering klar: {CriteriaSeen} kriterier, {Materialised} materialiserade, "
                + "{TooBroad} för breda, {MembersWritten} medlemsrader, {ExcludedPnr} pnr-formade uteslutna, "
                + "{ExcludedInvalid} ogiltiga uteslutna, {Failed} misslyckade, {DurationSeconds:F1} s.")]
    private static partial void LogCompleted(
        ILogger logger, int criteriaSeen, int materialised, int tooBroad, int membersWritten,
        int excludedPnr, int excludedInvalid, int failed, double durationSeconds);

    [LoggerMessage(
        EventId = 6422,
        Level = LogLevel.Error,
        Message = "Materialisering av kriterium {CriterionId} misslyckades. Kriteriet behåller sitt "
                + "tidigare tillstånd; övriga kriterier fortsätter.")]
    private static partial void LogCriterionFailed(ILogger logger, Guid criterionId, Exception ex);

    [LoggerMessage(
        EventId = 6423,
        Level = LogLevel.Warning,
        Message = "Kriterium {CriterionId}: {Count} kandidater uteslöts som personnummerformade vid "
                + "materialiseringens skrivgräns. Registret ska vara juridiska personer enbart (ADR 0091) "
                + "— en träff här betyder att ingest-vakten har ett hål.")]
    private static partial void LogPersonnummerShapedExcluded(
        ILogger logger, Guid criterionId, int count);
}
