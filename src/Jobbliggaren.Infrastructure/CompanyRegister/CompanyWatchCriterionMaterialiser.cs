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
/// <b>A failing criterion does not fail the run; a WHOLLY failing run does.</b> One corrupt or
/// unresolvable criterion must not deny every other user a fresh membership, so it is logged with the
/// criterion id, counted onto
/// <see cref="CompanyWatchCriterionMaterialisationResult.CriteriaFailed"/>, and skipped — the failure
/// is never swallowed (§5 forbids a catch-all without action). But if NOTHING succeeded the method
/// THROWS, and that asymmetry is the point (dotnet-architect, 2026-09-06): the Worker wrapper
/// deliberately keeps Hangfire's default retry, justified by "a transient DB blip must not turn into a
/// full day of stale membership" — and a broken connection fails every remaining criterion
/// identically, so without the throw no exception would ever leave <c>RunAsync</c> and that retry
/// could never fire for the one scenario it was retained for. A mitigation that cannot trigger is not
/// a mitigation.
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
                0, 0, 0, 0, 0, 0, 0, startedAt, clock.UtcNow);
        }

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
        var seen = 0;

        // PAGINATED, not a single ToListAsync (dotnet-architect, 2026-09-06). The corpus is
        // users x MaxPerUser, which is UNBOUNDED — MaxPerUser (20) bounds ONE USER, not the table, and
        // an earlier version of this comment claimed otherwise. §5 forbids unpaginated list fetches,
        // ADR 0045 Beslut 3 puts a 512 MiB soft cap on the Worker's working set, and each row carries
        // two text[] axes (up to 1 000 SNI + 290 kommun codes).
        //
        // OFFSET rather than keyset, and that is a forced choice worth writing down. Keyset is the
        // better shape and was the proposed one, but it needs `id > @last` in the WHERE, and
        // CompanyWatchCriterionId is a readonly record struct with NO comparison operators — so the
        // predicate does not translate, and adding operators to a Domain value object to suit a
        // background job's paging is the wrong direction for that dependency. The deep-OFFSET hazard
        // CompanyBrowseCriteria.MaxPage guards against does not apply the same way here: that is a
        // user-facing surface where the caller chooses the offset, whereas this walks its own table
        // once per night with offsets bounded by the corpus itself. ORDER BY the PK makes the walk
        // total and the run deterministic.
        var pageSize = options.Value.CriterionPageSize;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await db.CompanyWatchCriteria
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .Skip(offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var criterion in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seen++;

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
                    // boundary scan over this file (OrganizationNumberSurfacingGuardTests) is what
                    // keeps that true rather than a promise.
                    failed++;
                    LogCriterionFailed(logger, criterion.Id.Value, ex);
                }
            }

            // ONE exit, and it is total: a page shorter than the page size is the last page, and
            // that includes an empty one (0 < pageSize always). A second `page.Count == 0` break
            // used to sit above the loop and was REMOVED rather than kept as belt-and-braces --
            // measured 2026-09-06, with both present, deleting either left the paging test green,
            // because each silently covered for the other. A redundant exit is worse than no
            // redundancy: it makes the termination guarantee unmeasurable, and dropping the
            // remaining one is an infinite loop in operation.
            if (page.Count < pageSize)
                break;
        }

        // A run in which EVERY criterion failed must not report success — see the class docblock for
        // why the retained Hangfire retry depends on this throw existing.
        if (failed > 0 && materialised == 0 && tooBroad == 0)
        {
            throw new InvalidOperationException(
                $"Materialiseringen misslyckades för samtliga {failed} kriterier — ingen delmängd "
                + "skrevs. Körningen rapporteras som misslyckad så Hangfires retry kan lösa ut.");
        }

        // AGENTS.md §3.6 — a bulk-load path ANALYZEs the table it loaded. All three conditions hold:
        // these two tables have ONE periodic writer, carry no continuous DML between runs (the only
        // other writer is the FK cascade on criterion deletion, which REMOVES rows and cannot re-arm
        // autovacuum's analyze counter for the loader), and criterion_id reaches both a WHERE and a
        // join. Once per COMPLETED run, never per criterion.
        // This is not hygiene theatre: the read plan the breadth-gate bound was DERIVED against is an
        // Index Only Scan on the member PK with Heap Fetches: 0, and that plan needs current statistics
        // and a set visibility map — so without this the measured plan is not guaranteed in operation.
        // Fail-loud, which is the placement §3.6 prescribes for a retry-bounded job.
        await store.AnalyzeAsync(cancellationToken).ConfigureAwait(false);

        var result = new CompanyWatchCriterionMaterialisationResult(
            CriteriaSeen: seen,
            CriteriaMaterialised: materialised,
            CriteriaTooBroad: tooBroad,
            MembersWritten: membersWritten,
            MembersExcludedPersonnummerShaped: excludedByShapeGuard,
            MembersExcludedInvalid: excludedInvalid,
            CriteriaFailed: failed,
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
