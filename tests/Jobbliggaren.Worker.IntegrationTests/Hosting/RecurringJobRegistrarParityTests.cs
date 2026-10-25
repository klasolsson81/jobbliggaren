using Hangfire;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;
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
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        // Read the recurringJobId (first arg) of every interface AddOrUpdate(string, Job, string,
        // RecurringJobOptions) call recorded on the substitute. The generic AddOrUpdate<T> extension
        // funnels here, so this captures all 18 registrations regardless of the worker type.
        return manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Select(c => (string)c.GetArguments()[0]!)
            .ToList();
    }

    [Fact]
    public async Task StartAsync_FeedsTheSweep_ItsOwnMethod_AndItsOwnCron()
    {
        // #1681 klausul (ii) (test-writer, 2026-09-07). Mätt med mutationsanalys: BÅDA
        // korskopplingarna överlever varje annan test i PR:en.
        //
        //   job.SweepAsync -> job.RunAsync   → svep-id:t kör hela nattens fullständiga omräkning
        //                                      VARJE MINUT. Sämre än före PR:en, och grönt överallt.
        //   SweepCron -> CadenceCron         → svepet kör 05:30 dagligen. "Inom en tick, inte ett
        //                                      dygn" är precis det som faller.
        //
        // Sweep-sviten konstruerar materialiseraren direkt och rör aldrig registraren; lås-sviten
        // läser attribut via reflektion; id-paritetstesterna läser MEDVETET bara första argumentet.
        // Ingen av dem kan se detta. Grannregistreringen bär redan samma pinne av samma skäl.
        //
        // De två cron-värdena ges SKILDA värden — med bägge på sina defaults läser en förväxling
        // fortfarande rimligt, och assertionen hade inte kunnat falla.
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions
            {
                CadenceCron = "11 11 * * *",
                SweepCron = "*/7 * * * *",
            }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]!
                == RecurringJobIds.SweepChangedCompanyWatchCriteria);

        var job = (Hangfire.Common.Job)call.GetArguments()[1]!;
        job.Type.ShouldBe(typeof(CompanyWatchCriterionMaterialisationWorker));
        job.Method.Name.ShouldBe(nameof(CompanyWatchCriterionMaterialisationWorker.SweepAsync),
            "svep-id:t måste mata SweepAsync — en korskoppling till RunAsync kör hela "
            + "nattkörningen varje minut och är osynlig för id-paritetstesterna");

        ((string)call.GetArguments()[2]!).ShouldBe("*/7 * * * *",
            "svepet måste få SweepCron, aldrig CadenceCron — annars är klausul (ii) tillbaka på "
            + "ett dygns latens");
    }

    /// <summary>
    /// #1981 PR 3 (ADR 0181, security-auditor condition 3) — the send-count retention runs at 04:40 UTC, outside the hour in
    /// which daylight saving moves the Swedish date, which is what makes "at most eight dates" true at the scheduled run.
    /// A changed schedule changes the retention bound.
    /// </summary>
    [Fact]
    public async Task StartAsync_TheSendCountRetentionRunsDailyAt0440Utc_OnItsOwnWorker()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.EmailSendCountRetention);
        ((string)call.GetArguments()[2]!).ShouldBe("40 4 * * *");
        ((Hangfire.Common.Job)call.GetArguments()[1]!).Type.ShouldBe(typeof(EmailSendCountRetentionWorker));
    }

    /// <summary>
    /// #1981 PR 4a (ADR 0185) — the DMARC ingestion runs hourly on its own worker, which Hangfire never retries, and the
    /// twelve-month retention runs daily at 04:20 UTC. A changed retention schedule changes the retention bound.
    /// </summary>
    [Fact]
    public async Task StartAsync_TheDmarcIngestionRunsHourly_AndItsRetentionDailyAt0420Utc()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var calls = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .ToList();
        var ingestion = calls.Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.DmarcReportIngestion);
        ((string)ingestion.GetArguments()[2]!).ShouldBe("23 * * * *");
        ((Hangfire.Common.Job)ingestion.GetArguments()[1]!).Type.ShouldBe(typeof(DmarcReportIngestionWorker));
        var retention = calls.Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.DmarcReportRetention);
        ((string)retention.GetArguments()[2]!).ShouldBe("20 4 * * *");
        ((Hangfire.Common.Job)retention.GetArguments()[1]!).Type.ShouldBe(typeof(DmarcReportRetentionWorker));
    }

    [Fact]
    public void TheDmarcIngestionWrapper_IsNeverRetriedByHangfire_AndWaitsOutARunningOne()
    {
        var method = typeof(DmarcReportIngestionWorker).GetMethod(nameof(DmarcReportIngestionWorker.RunAsync))!;

        method.GetCustomAttributes(typeof(Hangfire.AutomaticRetryAttribute), inherit: false)
            .Cast<Hangfire.AutomaticRetryAttribute>().Single().Attempts.ShouldBe(0);
        method.GetCustomAttributes(typeof(Hangfire.DisableConcurrentExecutionAttribute), inherit: false)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task StartAsync_TheStreamImportRunsEveryTenMinutes_SoTheCardsOverdueLimitMeansTwoMissedRunsAndFive()
    {
        // #1982 (ADR 0159). The admin services card calls the import row overdue after 25 minutes, which is two
        // missed runs of this cron plus five minutes. The limit lives in Application and the cron here, and nothing
        // else ties them: a cron moved to */20 would leave the card calling a healthy import overdue for ever.
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.SyncPlatsbankenStream);
        var cron = (string)call.GetArguments()[2]!;

        cron.ShouldBe("*/10 * * * *");
        var cadence = TimeSpan.FromMinutes(int.Parse(cron.Split(' ')[0].Replace("*/", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture));
        ServiceObservationLimits.ImportOverdueAfter.ShouldBe((2 * cadence) + TimeSpan.FromMinutes(5));
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
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.MaterialiseCompanyWatchCriteria);

        var job = (Hangfire.Common.Job)call.GetArguments()[1]!;
        job.Type.ShouldBe(typeof(CompanyWatchCriterionMaterialisationWorker),
            "id:t måste mata SIN egen worker — en korskoppling till en annan wrapper är osynlig för "
            + "id-paritetstesterna");

        // #1681 klausul (ii) — wrappern bär nu TVÅ metoder, så typen ensam räcker inte längre.
        // En korskoppling till SweepAsync skulle köra det minutliga svepet en gång per dygn.
        job.Method.Name.ShouldBe(nameof(CompanyWatchCriterionMaterialisationWorker.RunAsync),
            "nattjobbets id måste mata RunAsync — sedan wrappern fick en andra metod är typen "
            + "ensam ingen identifiering");

        ((string)call.GetArguments()[2]!).ShouldBe("11 11 * * *",
            "jobbet måste få CompanyWatchMaterialisation:CadenceCron, aldrig ScbRegister:SyncCadenceCron "
            + "-- att de är skilda sektioner är hela poängen med Major 3");
    }

    [Fact]
    public async Task StartAsync_FeedsTheCriterionScanItsOwnCron_AndItsOwnWorkerType()
    {
        // ADR 0175 D11 — the Branschbevakning scan reads its cadence from its OWN section. Every cron here is
        // distinguishable, so a registration cross-wired to the materialisation's cadence or to another wrapper
        // fails this pin instead of reading plausibly.
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions { CadenceCron = "44 4 * * *" }),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.ScanCompanyWatchCriteria);
        var job = (Hangfire.Common.Job)call.GetArguments()[1]!;
        job.Type.ShouldBe(typeof(CompanyWatchCriterionScanWorker));
        job.Method.Name.ShouldBe(nameof(CompanyWatchCriterionScanWorker.RunAsync));
        ((string)call.GetArguments()[2]!).ShouldBe("44 4 * * *");
    }

    [Fact]
    public void The_criterion_scans_default_cadence_is_after_the_follow_scans_lock_wait()
    {
        // ADR 0175 D11: 03:55 UTC or later (the follow scan at 03:25 plus its 1 800-second lock wait), and before
        // the 05:30 materialisation.
        new CompanyWatchCriterionScanOptions().CadenceCron.ShouldBe("55 3 * * *");
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

        // 18 calls, all distinct — guards a copy-paste double-registration (which a set comparison
        // alone would silently absorb).
        registered.Count.ShouldBe(RecurringJobIds.All.Count);
        registered.Distinct(StringComparer.Ordinal).Count().ShouldBe(registered.Count);
    }

    [Fact]
    public async Task StartAsync_FeedsTheProfileJobItsOwnCron_AndItsOwnWorkerType()
    {
        // #1682 — the profile job reads OccupationDivisionProfile:CadenceCron from ITS OWN section, for
        // the same reason the materialisation job does: a cadence tied to another section's flag runs
        // exactly as often as that flag is true. The cron here is neither the shipped default nor either
        // sibling's, so a bind against the wrong section would show as the wrong string below.
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "22 22 * * *" }),
            Options.Create(new CompanyWatchCriterionScanOptions()),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]!
                == RecurringJobIds.BuildOccupationDivisionProfile);

        var job = (Hangfire.Common.Job)call.GetArguments()[1]!;
        job.Type.ShouldBe(typeof(OccupationDivisionProfileWorker));
        job.Method.Name.ShouldBe(nameof(OccupationDivisionProfileWorker.RunAsync));
        ((string)call.GetArguments()[2]!).ShouldBe("22 22 * * *",
            "profiljobbet måste få sin EGEN cron — varken materialiseringens eller SCB:s");
    }
}
