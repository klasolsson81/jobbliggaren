using Hangfire;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Worker.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// #204 / TD-83 PR2 — drift-lock between the Worker's <see cref="RecurringJobRegistrar"/> and the
/// closed trigger allowlist <see cref="RecurringJobIds.All"/>. The admin "trigger now" surface
/// validates against <c>RecurringJobIds.All</c>; if a registered job were missing from the allowlist
/// it would be untriggerable, and an allowlisted-but-unregistered id would validate then no-op. This
/// test proves the registrar registers EXACTLY the ids in <c>RecurringJobIds.All</c> — no more, no
/// fewer (security-auditor T7 parity invariant).
///
/// <para>
/// Pure unit test (NSubstitute <see cref="IRecurringJobManager"/>, no DB). Placed in
/// <c>Worker.IntegrationTests</c> because Jobbliggaren has no <c>Worker.UnitTests</c> project yet —
/// same pragmatic placement note as <see cref="HangfireWorkerOptionsTests"/>.
/// </para>
///
/// <para>
/// Capture mechanism: <c>RecurringJobRegistrar</c> calls the generic Hangfire EXTENSION method
/// <c>AddOrUpdate&lt;T&gt;(id, expr, cron)</c>, which builds a <c>Job</c> and delegates to the
/// INTERFACE method <c>AddOrUpdate(string recurringJobId, Job, string, RecurringJobOptions)</c> — the
/// only call NSubstitute records on the substitute. We read every such interface call from
/// <c>ReceivedCalls()</c> and take the first argument (the id) to avoid coupling the assertion to the
/// <c>Job</c>/<c>RecurringJobOptions</c> argument shapes.
/// </para>
/// </summary>
public class RecurringJobRegistrarParityTests
{
    private static async Task<IReadOnlyList<string>> CapturedRegisteredIdsAsync()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        // #560 — the registrar now reads the SCB refresh cron from IOptions<ScbRegisterOptions>; the
        // exact cron is irrelevant to the id-parity assertion (any non-empty value works).
        // #1681 — and the materialisation cron from its OWN IOptions section, which is the whole
        // point of that section existing (security-auditor Major 3): the job must not inherit
        // ScbRegister:Enabled=false. The exact cron is irrelevant to the id-parity assertion.
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "30 5 * * *" }),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        // Read the recurringJobId (first arg) of every interface AddOrUpdate(string, Job, string,
        // RecurringJobOptions) call recorded on the substitute. The generic AddOrUpdate<T> extension
        // funnels here, so this captures all 17 registrations regardless of the worker type.
        return manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Select(c => (string)c.GetArguments()[0]!)
            .ToList();
    }

    [Fact]
    public async Task StartAsync_FeedsTheMaterialisationJobItsOwnCron_AndItsOwnWorkerType()
    {
        // #1681 (ADR 0139) — Major 3's REAL home. The id-parity tests above deliberately read only the
        // first argument, so cross-wiring the materialisation registration to
        // scbOptions.Value.SyncCadenceCron (or to the wrong worker type) left every assertion green
        // while the job ran weekly Saturday 06:00 instead of daily 05:30 — and a criterion created on
        // a Monday then showed "not known yet" for six days, the exact defect CadenceCron's docblock
        // describes (test-writer, 2026-09-06).
        //
        // The two crons are given DISTINGUISHABLE values, which is what makes the assertion capable of
        // failing: with both at their real defaults a swap would still read plausibly.
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.MaterialiseCompanyWatchCriteria);

        var job = (Hangfire.Common.Job)call.GetArguments()[1]!;
        job.Type.ShouldBe(typeof(CompanyWatchCriterionMaterialisationWorker),
            "id:t måste mata SIN egen worker — en korskoppling till en annan wrapper är osynlig för "
            + "id-paritetstesterna");

        ((string)call.GetArguments()[2]!).ShouldBe("11 11 * * *",
            "jobbet måste få CompanyWatchMaterialisation:CadenceCron, aldrig ScbRegister:SyncCadenceCron "
            + "-- att de är skilda sektioner är hela poängen med Major 3");
    }

    [Fact]
    public async Task StartAsync_RegistersExactlyTheAllowlistIds_NoDrift()
    {
        var registered = await CapturedRegisteredIdsAsync();

        // Set equality both directions: every registered id is allowlisted AND every allowlisted id
        // is registered. A drift in either direction fails.
        registered.ToHashSet(StringComparer.Ordinal)
            .SetEquals(RecurringJobIds.All)
            .ShouldBeTrue(
                $"registered=[{string.Join(", ", registered.OrderBy(x => x, StringComparer.Ordinal))}] " +
                $"allowlist=[{string.Join(", ", RecurringJobIds.All.OrderBy(x => x, StringComparer.Ordinal))}]");
    }

    [Fact]
    public async Task StartAsync_RegistersEachIdExactlyOnce()
    {
        var registered = await CapturedRegisteredIdsAsync();

        // 17 calls, all distinct — guards a copy-paste double-registration (which a set comparison
        // alone would silently absorb).
        registered.Count.ShouldBe(RecurringJobIds.All.Count);
        registered.Distinct(StringComparer.Ordinal).Count().ShouldBe(registered.Count);
    }
}
