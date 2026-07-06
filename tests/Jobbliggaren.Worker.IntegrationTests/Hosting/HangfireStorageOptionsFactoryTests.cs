using Jobbliggaren.Worker.Hosting;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// #688 (ADR 0091 amendment 2026-07-05) — unit test for <see cref="HangfireStorageOptionsFactory"/>.
/// The <c>PostgreSqlStorageOptions</c> construction was lifted from <c>Program.cs</c> into a static
/// factory (parity <see cref="HangfireConnectionStringResolverTests"/>) so the load-bearing
/// sliding-invisibility flag is assertable WITHOUT booting Hangfire (CLAUDE.md §2.4 — "if it needs
/// ASP.NET to test, the design is wrong"). Pure option construction — no DB / Testcontainers.
/// </summary>
public class HangfireStorageOptionsFactoryTests
{
    [Fact]
    public void Create_EnablesSlidingInvisibilityTimeout_ForBothPrepareModes()
    {
        // The #688 fix: without sliding invisibility a fetched job becomes re-fetchable at the 30 min
        // InvisibilityTimeout WHILE STILL RUNNING — the ~2 h SCB population was re-fetched at ~29.5 min
        // ceilings (8 starts / 0 completions). The flag must hold regardless of PrepareSchemaIfNecessary
        // (Worker registration passes true, the mirrored Api registration passes false).
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: true)
            .UseSlidingInvisibilityTimeout.ShouldBeTrue();
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: false)
            .UseSlidingInvisibilityTimeout.ShouldBeTrue();
    }

    [Fact]
    public void Create_PinsSchemaName_ToHangfire()
    {
        // Worker and Api must share one Hangfire schema; a drift here would split the job store.
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: true)
            .SchemaName.ShouldBe("hangfire");
    }

    [Fact]
    public void Create_PinsInvisibilityTimeout_To30Minutes()
    {
        // Pinned explicitly (dotnet-architect #688): crash-recovery latency AND the sliding heartbeat
        // cadence (InvisibilityTimeout / 5 = 6 min) both derive from this value — it must not float on
        // a Hangfire.PostgreSql package default across upgrades.
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: true)
            .InvisibilityTimeout.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Create_PinsDistributedLockTimeout_To12Hours()
    {
        // #693 — the DisableConcurrentExecution distributed lock has NO heartbeat renewal (verified
        // Hangfire.PostgreSql 1.21.1 PostgreSqlDistributedLock); a held lock is stealable at
        // acquired + DistributedLockTimeout. 12 h covers the real ~11 h SCB runtime so a duplicate
        // execution cannot take over the lock mid-run (a duplicate took over at exactly +10:00 on the
        // #688 live run at the 10-min default). Global storage-level value — must not float on the
        // 10-min Hangfire.PostgreSql package default across upgrades. Holds regardless of
        // PrepareSchemaIfNecessary (the flag is orthogonal to the lock timeout).
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: true)
            .DistributedLockTimeout.ShouldBe(TimeSpan.FromHours(12));
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: false)
            .DistributedLockTimeout.ShouldBe(TimeSpan.FromHours(12));
    }

    [Fact]
    public void Create_PassesThroughPrepareSchemaIfNecessary_WhenTrue()
    {
        // The Worker owns schema preparation (true) — the argument is a pass-through, not overridden.
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: true)
            .PrepareSchemaIfNecessary.ShouldBeTrue();
    }

    [Fact]
    public void Create_PassesThroughPrepareSchemaIfNecessary_WhenFalse()
    {
        // The mirrored Api registration passes false (no-op today; kept symmetric with the Worker so a
        // future Api HangfireServer cannot regress the sliding-invisibility posture).
        HangfireStorageOptionsFactory.Create(prepareSchemaIfNecessary: false)
            .PrepareSchemaIfNecessary.ShouldBeFalse();
    }
}
