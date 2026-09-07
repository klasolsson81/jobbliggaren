using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
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

        var tally = new RunTally();
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

            // The stamp is read HERE, where the page was loaded — never at the write. See
            // ReplaceStampedAt below for why the direction matters; the window this closes is
            // largest on THIS run, because a page of up to CriterionPageSize criteria is walked
            // between the read and the last row's write.
            var stampedAt = clock.UtcNow;

            foreach (var criterion in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seen++;
                await ResolveOneIntoTallyAsync(criterion, tally, stampedAt, cancellationToken)
                    .ConfigureAwait(false);
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

        ThrowIfWhollyFailed(tally);

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

        var nightly = CompleteRun(tally, seen, startedAt);
        LogRun(nightly, tally);
        return nightly;
    }

    /// <summary>
    /// #1681 clause (ii) — the reconciling sweep. See
    /// <see cref="ICompanyWatchCriterionMaterialiser.MaterialiseChangedAsync"/> for why the committed
    /// state is the queue and no handler emits a signal.
    /// </summary>
    public async Task<CompanyWatchCriterionMaterialisationResult> MaterialiseChangedAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;

        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return new CompanyWatchCriterionMaterialisationResult(
                0, 0, 0, 0, 0, 0, 0, startedAt, clock.UtcNow);
        }

        // ONE capped list per tick, and deliberately NOT the paging loop above. Every criterion this
        // query returns drops OUT of its own predicate once resolved, so an advancing OFFSET would
        // skip rows; the nightly walk's loop is safe only because its predicate is not mutated by its
        // own work. The next tick re-derives instead — see SelectStaleCriterionIdsAsync.
        var candidates = await store
            .SelectStaleCriterionIdsAsync(options.Value.SweepBatchSize, cancellationToken)
            .ConfigureAwait(false);

        // Read with the candidate load, for the reason given at ReplaceStampedAt below.
        var stampedAt = clock.UtcNow;

        if (candidates.Count == 0)
        {
            // SILENT, and deliberately so. At a minute's cadence the idle tick is the overwhelmingly
            // common case: logging it would put ~1 440 identical zero-rows a day into the shared Seq
            // sink and bury the counter that matters on this EventId — the personnummer-shaped
            // exclusion count, which is a security signal. The per-criterion Warning (EventId 6423)
            // is the alarm and is untouched.
            return CompleteRun(new RunTally(), seen: 0, startedAt);
        }

        // The strongly-typed id, NOT its Guid: `Contains` over the raw `.Value` does not translate —
        // EF cannot see through the value object's converter inside a subquery, and the sweep would
        // throw on every tick that found a candidate. Measured 2026-09-07 (the sweep suite named it).
        var ids = candidates.Select(c => new CompanyWatchCriterionId(c.Id)).ToList();
        var stored = candidates.ToDictionary(c => c.Id, c => c.StoredFingerprint);

        // AsNoTracking: nothing here mutates the aggregate. `Contains` over the converted key
        // translates to `= ANY`, and the list is bounded by SweepBatchSize, so this is one indexed
        // statement.
        var criteria = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tally = new RunTally();
        var seen = 0;
        var skippedUnchanged = 0;

        foreach (var criterion in criteria)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // THE EXACT TEST. `updated_at` selected this row, but Rename bumps it exactly as
            // UpdateCriteria does, so the column is a superset and never the decision. A criterion
            // whose predicate is unchanged has cost one row read and one SHA-256 by this point, and
            // costs no register resolution at all — the bound ADR 0139 states for a rename.
            var current = CriteriaFingerprint.Of(criterion.Criteria);
            if (stored.TryGetValue(criterion.Id.Value, out var previous)
                && string.Equals(previous, current.Value, StringComparison.Ordinal))
            {
                skippedUnchanged++;
                continue;
            }

            seen++;
            await ResolveOneIntoTallyAsync(criterion, tally, stampedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        // NO ThrowIfWhollyFailed here, and its absence is the decision (senior-cto-advisor
        // 2026-09-07, reversing his own earlier bind on new evidence). The throw exists on the
        // nightly run SO HANGFIRE'S RETRY CAN FIRE — that is its whole stated mechanism. This run
        // carries AutomaticRetry(Attempts = 0), so there is no retry to fire and the throw would add
        // no information: CriteriaSeen, CriteriaFailed and LogCriterionFailed already distinguish
        // "nothing to do" from "everything failed". What it WOULD add is a manufactured fault — a
        // criterion deleted while this tick resolves it fails on the FK cascade, and if it was the
        // tick's only candidate the throw would register a FAILED Hangfire job for a user deleting
        // her own watch. At a minute's cadence that noise is indistinguishable from a real fault,
        // which is the signal the throw was built to protect.
        //
        // The genuinely wholly-broken case is caught upstream and more truthfully: a dead connection
        // fails SelectStaleCriterionIdsAsync, which runs BEFORE any candidate is touched.

        // §3.6, with its condition evaluated rather than inherited: this run is a bulk-load path only
        // on the ticks that actually wrote. In steady state it writes nothing, and ANALYZE on a table
        // no statement touched would be work done for no plan. Gating on the run having loaded
        // something keeps "once per COMPLETED run, never per batch" true while adding no cost to the
        // overwhelmingly common empty tick.
        if (tally.Materialised > 0 || tally.TooBroad > 0)
            await store.AnalyzeAsync(cancellationToken).ConfigureAwait(false);

        // A tick whose every candidate was dismissed by the fingerprint also stays quiet: it did no
        // work, and a renamed criterion stays a candidate until the nightly run, so it would otherwise
        // log every minute for as long as the rename is the newest edit.
        var swept = CompleteRun(tally, seen, startedAt);
        if (seen > 0 || tally.Failed > 0)
        {
            LogSweepCompleted(logger, candidates.Count, skippedUnchanged, seen);
            LogRun(swept, tally);
        }

        return swept;
    }

    /// <summary>
    /// A run in which EVERY criterion failed must not report success — see the class docblock for why
    /// the nightly wrapper's retained Hangfire retry depends on this throw existing. Shared by both
    /// runs: the sweep keeps it for the OTHER half of the same argument, since its steady state is
    /// <c>CriteriaSeen = 0</c> and that must stay distinguishable from "every candidate failed".
    /// </summary>
    private static void ThrowIfWhollyFailed(RunTally tally)
    {
        if (tally.Failed > 0 && tally.Materialised == 0 && tally.TooBroad == 0)
        {
            throw new InvalidOperationException(
                $"Materialiseringen misslyckades för samtliga {tally.Failed} kriterier — ingen "
                + "delmängd skrevs. Körningen rapporteras som misslyckad så Hangfires retry kan "
                + "lösa ut.");
        }
    }

    /// <summary>
    /// Assembles the run result. It does NOT log: the nightly run always reports, while the sweep
    /// reports only on a tick that did something. A shared method with a "should I log" flag would be
    /// the flag argument that hides two behaviours in one, so the decision stays with each caller.
    /// </summary>
    private CompanyWatchCriterionMaterialisationResult CompleteRun(
        RunTally tally, int seen, DateTimeOffset startedAt)
    {
        var result = new CompanyWatchCriterionMaterialisationResult(
            CriteriaSeen: seen,
            CriteriaMaterialised: tally.Materialised,
            CriteriaTooBroad: tally.TooBroad,
            MembersWritten: tally.MembersWritten,
            MembersExcludedPersonnummerShaped: tally.ExcludedByShapeGuard,
            MembersExcludedInvalid: tally.ExcludedInvalid,
            CriteriaFailed: tally.Failed,
            StartedAt: startedAt,
            CompletedAt: clock.UtcNow);

        return result;
    }

    /// <summary>The completion line, at Information. Both runs use it; only the sweep gates it.</summary>
    private void LogRun(CompanyWatchCriterionMaterialisationResult result, RunTally tally) =>
        LogCompleted(
            logger, result.CriteriaSeen, result.CriteriaMaterialised, result.CriteriaTooBroad,
            result.MembersWritten, tally.ExcludedByShapeGuard, tally.ExcludedInvalid, tally.Failed,
            (result.CompletedAt - result.StartedAt).TotalSeconds);

    /// <summary>
    /// One criterion, resolved into <paramref name="tally"/>. A failing criterion does not fail the
    /// run: it is logged with its id, counted, and skipped — never swallowed (§5 forbids a catch-all
    /// without action).
    /// </summary>
    private async Task ResolveOneIntoTallyAsync(
        CompanyWatchCriterion criterion, RunTally tally, DateTimeOffset stampedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await MaterialiseOneAsync(criterion, stampedAt, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.State == MaterialisationState.TooBroad)
                tally.TooBroad++;
            else
                tally.Materialised++;

            tally.MembersWritten += outcome.MemberCount;
            tally.ExcludedByShapeGuard += outcome.ExcludedPersonnummerShaped;
            tally.ExcludedInvalid += outcome.ExcludedInvalid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Counted, logged WITH the criterion id, and skipped. The id is a Guid — not an
            // org.nr, not a label, nothing user-identifying (ADR 0087 D8(c)); the org.nr-log
            // boundary scan over this file (OrganizationNumberSurfacingGuardTests) is what
            // keeps that true rather than a promise.
            tally.Failed++;
            LogCriterionFailed(logger, criterion.Id.Value, ex);
        }
    }

    /// <summary>
    /// The counters both runs accumulate into. <c>ExcludedByShapeGuard</c> is named WITHOUT an org.nr
    /// token on purpose: OrganizationNumberSurfacingGuardTests scans every <c>Log*()</c> ARGUMENT list
    /// on this file for "organization"/"orgnr"/"org_nr"/"personnummer" and cannot tell a COUNT of
    /// dropped values from a value. That conservatism is correct — the alternative is teaching the
    /// guard to accept token-named arguments, which is how such a guard goes vacuous — so the log
    /// surface is kept token-free instead. The domain name survives where it belongs, on
    /// <see cref="CompanyWatchCriterionMaterialisationResult"/> and on the state row.
    /// </summary>
    private sealed class RunTally
    {
        public int Materialised;
        public int TooBroad;
        public int MembersWritten;
        public int ExcludedByShapeGuard;
        public int ExcludedInvalid;
        public int Failed;
    }

    /// <summary>
    /// <b>ReplaceStampedAt — why <paramref name="stampedAt"/> is a PARAMETER and not
    /// <c>clock.UtcNow</c> read here</b> (senior-cto-advisor, 2026-09-07). The stamp is taken where
    /// the criteria were LOADED, so it is always earlier than the write, never later. An edit landing
    /// between the load and the write then leaves <c>updated_at &gt; materialised_at</c>, the row stays
    /// a sweep candidate, and the next tick re-resolves it. Reading the clock at write time inverted
    /// that: the row got an OLD fingerprint with a NEW timestamp, dropped out of the candidate set,
    /// and the surface said "vet inte" until the nightly run. The direction is deliberately
    /// conservative — an earlier stamp ages a row out of <c>MaxReadAgeHours</c> sooner, never later —
    /// the same anchor discipline <c>StrandedMatchReaperJob</c> already carries. One criterion must
    /// not be able to read the clock twice, which is why this is threaded rather than re-read.
    ///
    /// <para>
    /// ⚠ <b>It SHRINKS the window; it does not close it, and this must not be written as though it
    /// did.</b> An edit whose handler stamped <c>UpdatedAt</c> before our read but whose transaction
    /// committed after it is invisible under READ COMMITTED, and we then stamp a later
    /// <c>materialised_at</c> anyway. No stamp can close that — it turns on a timestamp we cannot
    /// see. It stays acceptable because the failure is honest: the read path answers "vet inte", never
    /// a number, and the nightly run repairs it inside 24 h.
    /// </para>
    ///
    /// <para>
    /// One criterion: select under the gate, filter at the write boundary, replace. The order is
    /// load-bearing — the gate fires on the RAW candidate count, before the personnummer filter, so a
    /// criterion cannot slip under the bound by having candidates dropped. Were it the other way
    /// round, the bound would silently be "1 000 survivors" rather than "1 000 matches", and the
    /// storage argument (Art. 5(1)(c)) would be measured against a number the register can move.
    /// </para>
    /// </summary>
    private async Task<CriterionOutcome> MaterialiseOneAsync(
        CompanyWatchCriterion criterion, DateTimeOffset stampedAt,
        CancellationToken cancellationToken)
    {
        // #1681 part 2 — the predicate this run is resolving, stamped onto the row it writes so a
        // later read can tell "these members are for the criterion on screen" from "these members are
        // for a predicate its owner has since edited". Computed ONCE per criterion, here, from the
        // same spec the candidate selection below uses — so the stamp cannot describe a different
        // predicate than the one that produced the members.
        var fingerprint = CriteriaFingerprint.Of(criterion.Criteria);

        var candidates = await store.SelectCandidatesAsync(
                criterion.Criteria, CompanyWatchCriterionMember.MaxPerCriterion, cancellationToken)
            .ConfigureAwait(false);

        if (candidates is null)
        {
            // Refused. The member set is still DELETED (the criterion may have been narrow before it
            // was widened), and the state row records the refusal so the read side renders "för bred"
            // instead of a number, or a zero, or nothing at all.
            await store.ReplaceAsync(
                    criterion.Id.Value, [], MaterialisationState.TooBroad, 0, stampedAt, fingerprint,
                    cancellationToken)
                .ConfigureAwait(false);

            return new CriterionOutcome(MaterialisationState.TooBroad, 0, 0, 0);
        }

        var filtered = CompanyWatchCriterionMemberFilter.Apply(candidates);

        await store.ReplaceAsync(
                criterion.Id.Value, filtered.OrganizationNumbers, MaterialisationState.Materialised,
                filtered.ExcludedPersonnummerShaped, stampedAt, fingerprint, cancellationToken)
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
        EventId = 6424,
        Level = LogLevel.Information,
        Message = "Omräkningssvep: {Candidates} kandidater, {SkippedUnchanged} oförändrade "
                + "(omdöpning eller redan aktuell), {Resolved} upplösta mot registret.")]
    private static partial void LogSweepCompleted(
        ILogger logger, int candidates, int skippedUnchanged, int resolved);

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
