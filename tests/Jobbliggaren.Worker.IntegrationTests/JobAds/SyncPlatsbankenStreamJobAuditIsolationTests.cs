using System.Text.Json;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.JobAds;

/// <summary>
/// #982 — the audit-ISOLATION acceptance test for <see cref="SyncPlatsbankenStreamJob"/> against
/// REAL Postgres. It proves the load-bearing claim the per-item child scope (Option 1) exists for: a
/// stream item whose upsert <c>SaveChangesAsync</c> FAILS must NOT (a) drop the run's GDPR Art. 30
/// record-of-processing, nor (b) fell a sibling upsert.
///
/// <para>
/// <b>The bug this pins.</b> Before the fix, the Stream job ran every upsert AND the final audit
/// write on ONE shared job-scope <c>IAppDbContext</c>. A poisoned upsert (issue #982: a corrupted
/// <c>raw_payload</c> → 22P02) failed its <c>SaveChangesAsync</c> but EF left the entity tracked;
/// the auditor's later <c>SaveChangesAsync</c> re-flushed it, threw again, and the
/// <c>System.JobAdsSynced</c> Art. 30 row was silently dropped and misreported as an
/// <c>audit_write_failure</c>.
/// </para>
///
/// <para>
/// <b>Why a DB trigger, not a corrupted payload.</b> Defect 1 fixes the specific 22P02 cause, so a
/// corrupted payload no longer reproduces the failure. GENUINE poison needs an upsert's atomic
/// <c>SaveChangesAsync</c> to fail AFTER <c>db.JobAds.Add(...)</c>, leaving a tracked Added entity —
/// induced deterministically by a sentinel-keyed plpgsql BEFORE INSERT trigger on <c>job_ads</c>
/// that raises (SQLSTATE P0001, NOT a 23505 the upsert handler would swallow) whenever
/// <c>external_id</c> equals the run's poison sentinel.
/// </para>
///
/// <para>
/// <b>The counterfactual (built in).</b> The poison item is streamed FIRST, the clean item SECOND.
/// On the fixed code every item owns its own child scope, so the poisoned item's tracker dies with
/// its scope: the clean item commits, and the auditor writes on a job-scope context nothing ever
/// touched → the Art. 30 row lands (delta 1). On the OLD shared-context code the poisoned Added row
/// survived in the single tracker; the auditor's <c>SaveChangesAsync</c> re-flushed it and threw →
/// no audit row (delta 0 → RED) and the clean item's write was collateral.
/// </para>
///
/// <para>
/// The Postgres container is SHARED across the serial <c>[Collection("Worker")]</c>, so the trigger
/// + function carry the per-test <c>_run</c> suffix and are DROPped in a <c>finally</c> — a leaked
/// trigger would fail unrelated later tests in the collection.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class SyncPlatsbankenStreamJobAuditIsolationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // Per-test-unique run id (xUnit news a fresh instance per [Fact]). Used for the trigger/function
    // names (the shared container demands uniqueness) and the external ids (isolate from other tests).
    private readonly string _run = Guid.NewGuid().ToString("N")[..20];

    // The plpgsql identities must start with a letter (Postgres identifier rule): the _run suffix is
    // hex and could lead with a digit, so both names are letter-prefixed.
    private string PoisonFn => $"poison_fn_{_run}";
    private string PoisonTrg => $"poison_trg_{_run}";
    private string PoisonExternalId => $"poison-{_run}";
    private string CleanExternalId => $"clean-{_run}";

    // The job's `now` — fixed so the audit row's occurred_at is deterministic. Any date lands (the
    // audit_log_default partition is the safety net), so no partition provisioning is needed.
    private static readonly DateTimeOffset Now =
        new(2026, 6, 1, 3, 20, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_LandsTheArt30AuditRow_EvenWhenAnItemUpsertFails()
    {
        var ct = TestContext.Current.CancellationToken;

        // Poison FIRST, clean SECOND — so the clean item is processed AFTER the failure (the
        // batch-cascade + audit-drop counterfactual both hinge on the ordering).
        var changes = new JobAdChange[]
        {
            new JobAdUpsert(PoisonExternalId, ValidItem(PoisonExternalId), Now),
            new JobAdUpsert(CleanExternalId, ValidItem(CleanExternalId), Now),
        };
        var jobSource = StubJobSource(changes);

        var beforeAggregateIds = await GetJobAdsSyncedAggregateIdsAsync(ct);

        await InstallPoisonTriggerAsync(ct);
        try
        {
            // Isolation held: the poisoned upsert's failure is caught per-event, never propagated.
            await Should.NotThrowAsync(() => RunStreamJobAsync(jobSource, ct));

            // (1) THE Art. 30 GUARANTEE (RED on the old shared-context code): exactly ONE new
            // System.JobAdsSynced row landed despite the poisoned item in the batch.
            var newIds = (await GetJobAdsSyncedAggregateIdsAsync(ct))
                .Except(beforeAggregateIds).ToList();
            newIds.Count.ShouldBe(1,
                "the run's Art. 30 record-of-processing must land even when an item upsert failed — " +
                "on the old shared-context code the auditor re-flushed the poisoned entity and threw, " +
                "dropping the row (delta 0)");

            var payload = await ReadPayloadAsync(newIds[0], ct);
            payload.ShouldNotBeNull("the audit row must carry a serialized JobAdsSynced payload");
            var counts = JsonSerializer.Deserialize<AuditPayload>(payload!);
            counts.ShouldNotBeNull();
            counts!.JobType.ShouldBe("stream");
            counts.Fetched.ShouldBe(2, "both stream events were fetched");
            counts.Errors.ShouldBe(1, "the poisoned upsert is counted as one error");
            counts.Added.ShouldBe(1, "only the clean ad was added");

            // (2) BATCH ISOLATION: the clean ad committed despite the poison streamed before it — its
            // own child scope's SaveChanges is independent (on shared-context code it was collateral).
            (await JobAdExistsAsync(CleanExternalId, ct)).ShouldBeTrue(
                "the clean ad, streamed after the poison, must commit in its own child scope");

            // The poison ad never committed — the trigger rolled its INSERT back.
            (await JobAdExistsAsync(PoisonExternalId, ct)).ShouldBeFalse(
                "the poisoned ad's INSERT was rejected by the trigger, so it must not persist");
        }
        finally
        {
            await DropPoisonTriggerAsync();
        }
    }

    // ─────────────────────────── SUT construction ───────────────────────────

    // The job runs its production per-item child-scope path end-to-end against real Postgres: the
    // scope factory is the fixture's REAL root, so each item resolves a real IMediator (and thus the
    // real UpsertExternalJobAdCommandHandler + UnitOfWorkBehavior + AppDbContext) from a real child
    // scope. The auditor is resolved from a SEPARATE "job" scope — mirroring production, where the
    // Hangfire job scope holds the injected auditor while the per-item child scopes are siblings off
    // the root. That separation is exactly what keeps the audit write off the poisoned context.
    private async Task RunStreamJobAsync(IJobSource jobSource, CancellationToken ct)
    {
        using var jobScope = _fixture.Services.CreateScope();
        var auditor = jobScope.ServiceProvider.GetRequiredService<ISystemEventAuditor>();

        var job = new SyncPlatsbankenStreamJob(
            jobSource,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            auditor,
            new IngestionThroughputReporter(
                Options.Create(new IngestionThroughputOptions()),
                NullLogger<IngestionThroughputReporter>.Instance),
            _fixture.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<SyncPlatsbankenStreamJob>());

        await job.RunAsync(ct);
    }

    // ─────────────────────────── Poison trigger (install / drop) ───────────────────────────

    // A sentinel-keyed plpgsql BEFORE INSERT trigger on job_ads: any INSERT whose external_id equals
    // the run's poison id raises (P0001 — NOT the 23505 the upsert handler swallows), so the poisoned
    // upsert's atomic SaveChanges fails AFTER db.JobAds.Add (genuine poison). Two statements, two calls.
    private async Task InstallPoisonTriggerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Identifiers and a plpgsql body cannot be parameterized, so the SQL is built into plain
        // `string` locals (not interpolated-string expressions at the call site) to satisfy EF1002.
        string createFunction =
            $@"CREATE OR REPLACE FUNCTION {PoisonFn}() RETURNS trigger AS $fn$
BEGIN
    IF NEW.external_id = '{PoisonExternalId}' THEN
        RAISE EXCEPTION 'poison sentinel {_run}';
    END IF;
    RETURN NEW;
END;
$fn$ LANGUAGE plpgsql;";
        string createTrigger =
            $@"CREATE TRIGGER {PoisonTrg} BEFORE INSERT ON job_ads
    FOR EACH ROW EXECUTE FUNCTION {PoisonFn}();";

        await db.Database.ExecuteSqlRawAsync(createFunction, ct);
        await db.Database.ExecuteSqlRawAsync(createTrigger, ct);
    }

    // Drop in a finally, no CancellationToken — cleanup must run even if the test token is cancelled
    // (a leaked trigger fails unrelated later tests in the shared Worker collection).
    private async Task DropPoisonTriggerAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string dropTrigger = $"DROP TRIGGER IF EXISTS {PoisonTrg} ON job_ads;";
        string dropFunction = $"DROP FUNCTION IF EXISTS {PoisonFn}();";
        await db.Database.ExecuteSqlRawAsync(dropTrigger);
        await db.Database.ExecuteSqlRawAsync(dropFunction);
    }

    // ─────────────────────────── Stream stub ───────────────────────────

    private static IJobSource StubJobSource(params JobAdChange[] changes)
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.StreamChangesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(changes));
        return jobSource;
    }

    private static async IAsyncEnumerable<JobAdChange> ToAsyncEnumerable(JobAdChange[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static JobAdImportItem ValidItem(string externalId)
    {
        var rawPayload = $"{{\"id\":\"{externalId}\"}}";
        return new JobAdImportItem(
            ExternalId: externalId,
            Title: "Integrationstest-annons",
            CompanyName: "Test Company AB",
            Description: "beskrivning",
            Url: $"https://example.com/jobs/{externalId}",
            PublishedAt: Now.AddDays(-1),
            ExpiresAt: Now.AddDays(30),
            SanitizedRawPayload: rawPayload,
            Facets: TestFacets.FromPayload(rawPayload),
            Requirements: [],
            DeclaredContacts: []);
    }

    // ─────────────────────────── Read-back (fresh fixture scopes) ───────────────────────────

    private async Task<List<Guid>> GetJobAdsSyncedAggregateIdsAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EventType == "System.JobAdsSynced")
            .Select(a => a.AggregateId)
            .ToListAsync(ct);
    }

    private async Task<string?> ReadPayloadAsync(Guid aggregateId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.AggregateId == aggregateId && a.EventType == "System.JobAdsSynced")
            .Select(a => a.Payload)
            .FirstAsync(ct);
    }

    private async Task<bool> JobAdExistsAsync(string externalId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobAds.AsNoTracking()
            .AnyAsync(j => j.External != null && j.External.ExternalId == externalId, ct);
    }

    // The subset of the serialized JobAdsSynced payload this test asserts on. The auditor serializes
    // with default STJ options (PascalCase, no naming policy), so these PascalCase names match.
    private sealed record AuditPayload(int Fetched, int Added, int Errors, string JobType);

    // Local fixed clock (parity BackgroundMatchingJobPoisonIsolationTests) — the shared test-support
    // FakeDateTimeProvider is not referenced by this project.
    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
