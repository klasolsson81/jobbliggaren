using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — the SCB company-register refresh ORCHESTRATOR: implements
/// <see cref="IScbCompanyRegisterRefresher"/> (the Worker/admin use-case port). It streams legal
/// entities from <see cref="IScbCompanyRegisterSource"/>, drops any personnummer-shaped org.nr
/// (defense-in-depth GDPR guard), bulk-upserts each batch into <c>company_register</c> via
/// <see cref="ScbCompanyRegisterStore"/> in a per-batch child scope, runs the floor-gated deregister
/// sweep, and records a <c>CompanyRegisterSynced</c> audit row. Lives in Infrastructure (parity
/// <c>TaxonomySnapshotSeeder</c>) because it writes the Infrastructure-internal replica via the
/// concrete <c>AppDbContext</c> — an Application job would need a persistence-mechanics port ADR 0009
/// rejects (senior-cto-advisor 2026-07-04, Fork 2).
/// </summary>
internal sealed partial class ScbCompanyRegisterRefresher(
    IScbCompanyRegisterSource source,
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider clock,
    IOptions<ScbRegisterOptions> options,
    ILogger<ScbCompanyRegisterRefresher> logger) : IScbCompanyRegisterRefresher
{
    // Read audit rows up to 90 days back for the relative-floor baseline (covers a monthly cadence
    // plus slack).
    private const int FloorBaselineWindowDays = 90;

    public async Task<ScbCompanyRegisterRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var startedAt = clock.UtcNow;

        if (!opts.Enabled)
        {
            LogDisabled(logger);
            return new ScbCompanyRegisterRefreshResult(
                RowsUpserted: 0, RowsDeregistered: 0, RowsExcludedPersonnummerShaped: 0,
                RowsExcludedInvalid: 0, TotalRowsFetched: 0, SweepApplied: false,
                SweepSkipReason: "disabled", ProtectedPartitionCount: 0,
                StartedAt: startedAt, CompletedAt: clock.UtcNow);
        }

        var runId = Guid.NewGuid();
        LogStarted(logger);

        var outcome = new ScbSyncOutcome();
        var rowsUpserted = 0;
        var excludedPersonnummerShaped = 0;
        var excludedInvalid = 0;

        await foreach (var batch in source
            .StreamLegalEntitiesAsync(outcome, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The legal-entities-only ingest guard (pure, unit-tested in ScbLegalEntityFilter): drops
            // invalid org.nrs AND — defense-in-depth behind the SCB Juridisk-form ≠ 10 query filter —
            // any personnummer-shaped org.nr. This exclusion IS the register's near-GDPR-free
            // foundation (ADR 0091 / CLAUDE.md §5 highest-priority), enforced regardless of upstream.
            var filtered = ScbLegalEntityFilter.Apply(batch);
            excludedPersonnummerShaped += filtered.ExcludedPersonnummerShaped;
            excludedInvalid += filtered.ExcludedInvalid;

            if (filtered.Entries.Count == 0)
                continue;

            // Child DI scope per batch → fresh AppDbContext/connection → no pooled connection held
            // across the 1–3 h of throttled SCB waits (parity SyncPlatsbankenSnapshotJob, batch-granular
            // because the raw-SQL upsert bypasses the change tracker — no per-row scope needed).
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ScbCompanyRegisterStore>();
            rowsUpserted += await store.UpsertBatchAsync(filtered.Entries, startedAt, cancellationToken).ConfigureAwait(false);
        }

        var (sweepApplied, rowsDeregistered, skipReason) =
            await MaybeDeregisterAsync(outcome, startedAt, opts, cancellationToken).ConfigureAwait(false);

        var protectedPartitionCount = outcome.ProtectedPartitions.Count;

        // #640 (Guard 2) — a distinct diagnostic for a no-SNI completeness gap: the binary truncation
        // latch collapses every skip reason to one string, so surface the reconciliation gap explicitly
        // (counts only, never an org.nr). A gap is believed non-existent — if it ever fires, it means an
        // entity carries a division but no listed 5-digit SNI, which the ladder cannot see.
        if (outcome.ReconciliationGaps > 0)
            LogReconciliationGap(logger, outcome.ReconciliationGaps);

        var completedAt = clock.UtcNow;

        await using (var auditScope = scopeFactory.CreateAsyncScope())
        {
            var auditor = auditScope.ServiceProvider.GetRequiredService<ISystemEventAuditor>();
            await auditor.RecordAsync(new CompanyRegisterSynced(
                AggregateId: runId,
                OccurredAt: completedAt,
                RowsUpserted: rowsUpserted,
                RowsDeregistered: rowsDeregistered,
                RowsExcludedPersonnummerShaped: excludedPersonnummerShaped,
                RowsExcludedInvalid: excludedInvalid,
                TotalRowsFetched: outcome.TotalRowsFetched,
                SweepApplied: sweepApplied,
                SweepSkipReason: skipReason,
                ProtectedPartitionCount: protectedPartitionCount,
                StartedAt: startedAt,
                CompletedAt: completedAt), cancellationToken).ConfigureAwait(false);
        }

        LogCompleted(logger, rowsUpserted, rowsDeregistered, excludedPersonnummerShaped,
            excludedInvalid, outcome.TotalRowsFetched, sweepApplied,
            (completedAt - startedAt).TotalMinutes);

        return new ScbCompanyRegisterRefreshResult(
            RowsUpserted: rowsUpserted,
            RowsDeregistered: rowsDeregistered,
            RowsExcludedPersonnummerShaped: excludedPersonnummerShaped,
            RowsExcludedInvalid: excludedInvalid,
            TotalRowsFetched: outcome.TotalRowsFetched,
            SweepApplied: sweepApplied,
            SweepSkipReason: skipReason,
            ProtectedPartitionCount: protectedPartitionCount,
            StartedAt: startedAt,
            CompletedAt: completedAt);
    }

    /// <summary>
    /// Runs the deregister sweep only when the extract completed cleanly AND cleared both safety
    /// floors (parity the JobTech snapshot floor-guard). A partial/under-floor run must never flip the
    /// untouched majority to Deregistered.
    /// </summary>
    private async Task<(bool Applied, int Rows, string? SkipReason)> MaybeDeregisterAsync(
        ScbSyncOutcome outcome, DateTimeOffset runStartedAt, ScbRegisterOptions opts, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ScbCompanyRegisterStore>();

        // maxObserved (prior-run fetched baseline) drives the relative floor only; skip the read when a
        // cheaper gate (truncation) already forces a skip. The gate itself is a pure, unit-tested
        // decision (ScbSweepGate) applying all three checks.
        var maxObserved = outcome.TruncatedOrErrored
            ? null
            : await store.GetMaxObservedTotalRowsFetchedAsync(FloorBaselineWindowDays, ct).ConfigureAwait(false);

        var (apply, skipReason) = ScbSweepGate.Decide(outcome, opts, maxObserved);
        if (!apply)
        {
            LogSweepSkipped(logger, skipReason!, outcome.TotalRowsFetched);
            return (false, 0, skipReason);
        }

        // #640 — the sweep runs, but excludes the over-cap (kommun, SNI) tails the run could only
        // partially fetch (partition-scoped sweep). An empty set → unrestricted sweep (#628 back-compat).
        var rows = await store
            .DeregisterMissingAsync(runStartedAt, outcome.ProtectedPartitions, ct)
            .ConfigureAwait(false);
        return (true, rows, null);
    }

    // LoggerMessages carry counts + verdicts ONLY — never an org.nr (CLAUDE.md §5).
    [LoggerMessage(EventId = 5710, Level = LogLevel.Information,
        Message = "ScbCompanyRegisterRefresher: startad (population/refresh).")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 5711, Level = LogLevel.Information,
        Message = "ScbCompanyRegisterRefresher: ScbRegister:Enabled=false — no-op (ingen SCB-anrop, inget cert).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 5712, Level = LogLevel.Information,
        Message = "ScbCompanyRegisterRefresher: klart — upserted={Upserted}, deregistered={Deregistered}, excludedPnr={ExcludedPnr}, excludedInvalid={ExcludedInvalid}, fetched={Fetched}, sweepApplied={SweepApplied}, durationMin={DurationMin}.")]
    private static partial void LogCompleted(ILogger logger, int upserted, int deregistered,
        int excludedPnr, int excludedInvalid, int fetched, bool sweepApplied, double durationMin);

    [LoggerMessage(EventId = 5713, Level = LogLevel.Warning,
        Message = "ScbCompanyRegisterRefresher: deregister-sweep SKIPPAD ({Reason}) — fetched={Fetched}. Ingen falsk avregistrering.")]
    private static partial void LogSweepSkipped(ILogger logger, string reason, int fetched);

    [LoggerMessage(EventId = 5714, Level = LogLevel.Warning,
        Message = "ScbCompanyRegisterRefresher: SNI-fullständighetsgap upptäckt ({Gaps} st) — en division saknar 5-siffrig täckning; körningen markeras trunkerad (sweep avstängd). Loggar aldrig org.nr.")]
    private static partial void LogReconciliationGap(ILogger logger, int gaps);
}
