using System.Runtime.CompilerServices;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — Testcontainers test for the orchestrator's real write path against Postgres. The
/// orchestrator resolves the CONCRETE <see cref="ScbCompanyRegisterStore"/> from child scopes (no
/// port — Fork 2), so its wiring (filter → upsert → floor-gated sweep → audit + the timestamp coupling
/// + the relative-floor read from prior audit rows) is only reachable through real DB. A fake source
/// feeds controlled batches; an injected fixed clock makes the timestamps deterministic.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ScbCompanyRegisterRefresherIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 7, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(7);

    [Fact]
    public async Task RefreshAsync_ExcludesPnr_KeepsOwnFreshRows_ThenRelativeFloorSkipsSweep()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // --- Run 1: clean, fetched 1000 ≥ absolute floor, no prior baseline → sweep APPLIES ---
        var run1 = await BuildRefresher(
            new FakeSource([Legal("5560000078"), PnrShaped()], fetched: 1000), T0).RefreshAsync(ct);

        run1.RowsExcludedPersonnummerShaped.ShouldBe(1); // the GDPR guard fired end-to-end
        run1.RowsUpserted.ShouldBe(1);
        run1.SweepApplied.ShouldBeTrue();
        var afterRun1 = await ReadAllAsync(ct);
        afterRun1.ShouldHaveSingleItem().OrganizationNumber.ShouldBe("5560000078"); // pnr NEVER persisted
        // Timestamp coupling: the sweep uses runStartedAt == the just-stamped synced_at, so this run's
        // own fresh row (synced_at == runStartedAt, not < it) is never swept.
        afterRun1[0].Status.ShouldBe(CompanyRegisterStatus.Active);

        // --- Run 2: fetched 100 < 0.80 × 1000 → relative floor SKIPS the sweep ---
        var run2 = await BuildRefresher(
            new FakeSource([Legal("5560000078")], fetched: 100), T1).RefreshAsync(ct);

        run2.SweepApplied.ShouldBeFalse();
        // Proves GetMaxObservedTotalRowsFetchedAsync read run 1's TotalRowsFetched=1000 from audit_log
        // (the auditor-serialization ↔ store-read contract end-to-end).
        run2.SweepSkipReason.ShouldBe("below-relative-floor");
        (await ReadAllAsync(ct)).ShouldAllBe(e => e.Status == CompanyRegisterStatus.Active); // no false deregistration
    }

    private ScbCompanyRegisterRefresher BuildRefresher(IScbCompanyRegisterSource source, DateTimeOffset now) =>
        new(source,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(now),
            Options.Create(new ScbRegisterOptions { Enabled = true, FloorAbsolute = 1, FloorRelativeRatio = 0.80 }),
            NullLogger<ScbCompanyRegisterRefresher>.Instance);

    private static ScbCompanyRecord Legal(string orgNr) =>
        new(orgNr, "Acme AB", "0180", "Stockholm", ["29100"], false, "1");

    // 3rd digit '0' < '2' → personnummer-shaped → must be excluded end-to-end, never reaching the DB.
    private static ScbCompanyRecord PnrShaped() =>
        new("9001011234", "Anna Andersson", "0180", "Stockholm", [], false, "1");

    private async Task ResetAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM audit_log WHERE event_type = 'System.CompanyRegisterSynced';", ct);
    }

    private async Task<List<ScbCompanyRegisterEntry>> ReadAllAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<ScbCompanyRegisterEntry>().AsNoTracking()
            .OrderBy(e => e.OrganizationNumber).ToListAsync(ct);
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeSource(IReadOnlyList<ScbCompanyRecord> batch, int fetched) : IScbCompanyRegisterSource
    {
        public async IAsyncEnumerable<IReadOnlyList<ScbCompanyRecord>> StreamLegalEntitiesAsync(
            ScbSyncOutcome outcome, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            outcome.RecordCounted();
            outcome.RecordFetched(fetched); // drives the floor independently of the batch's row count
            await Task.CompletedTask.ConfigureAwait(false);
            yield return batch;
        }
    }
}
