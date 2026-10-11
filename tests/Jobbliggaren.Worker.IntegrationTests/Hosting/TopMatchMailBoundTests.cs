using System.Reflection;
using Hangfire;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Worker.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// ADR 0161 D1, D7, D10 — the schedule a Toppmatch mail's fifteen-minute bound rests on. Pure tests,
/// no database.
/// </summary>
public class TopMatchMailBoundTests
{
    // Hangfire's recurring-job scheduler enqueues a due job on its next poll, and a free worker then
    // takes it off the queue. One minute covers both.
    private static readonly TimeSpan SchedulingSlack = TimeSpan.FromMinutes(1);

    [Fact]
    public void Cron_IsAMinuteStepOverTheHour_WithEveryOtherFieldStar()
    {
        var fields = BackgroundMatchingWorker.Cron.Split(' ');

        fields.Length.ShouldBe(5);
        fields[0].ShouldMatch(CronMinuteField.MinuteStepForm().ToString());
        fields[1..].ShouldAllBe(field => field == "*");
    }

    [Fact]
    public void TopMatchMailBound_IsFifteenMinutes()
    {
        BackgroundMatchingJob.TopMatchMailBound.ShouldBe(TimeSpan.FromMinutes(15),
            "the product promise, ADR 0161 D10");
    }

    [Fact]
    public void Cron_LargestGapPlusTheBudgetsAndTheSchedulingSlack_IsWithinTheTopMatchMailBound()
    {
        var largestGap = CronMinuteField.Gaps(BackgroundMatchingWorker.Cron).Max();

        (largestGap + BackgroundMatchingJob.ScanBudget + TopMatchMailDispatcher.SendBudget + SchedulingSlack)
            .ShouldBeLessThanOrEqualTo(BackgroundMatchingJob.TopMatchMailBound,
                "an ad that just missed a run waits a whole gap, then the next run's scan and send phases");
    }

    [Fact]
    public async Task StartAsync_RegistersBackgroundMatching_OnTheWorkersRunAsync_WithTheCronConstant()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var registrar = new RecurringJobRegistrar(
            manager,
            Options.Create(new ScbRegisterOptions { SyncCadenceCron = "0 6 * * 6" }),
            Options.Create(new CompanyWatchMaterialisationOptions { CadenceCron = "11 11 * * *" }),
            Options.Create(new OccupationDivisionProfileOptions { CadenceCron = "35 3 * * *" }),
            NullLogger<RecurringJobRegistrar>.Instance);

        await registrar.StartAsync(CancellationToken.None);

        var call = manager.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Single(c => (string)c.GetArguments()[0]! == RecurringJobIds.BackgroundMatching);
        var job = (Hangfire.Common.Job)call.GetArguments()[1]!;
        job.Type.ShouldBe(typeof(BackgroundMatchingWorker));
        job.Method.Name.ShouldBe(nameof(BackgroundMatchingWorker.RunAsync));
        ((string)call.GetArguments()[2]!).ShouldBe(BackgroundMatchingWorker.Cron);
    }

    // The lock is what keeps a Top row from being claimed twice (ADR 0161 D8). A tick that waits for it
    // holds a worker, and wait ÷ interval ticks can wait at once; under half an interval, at most one does.
    [Fact]
    public void RunAsync_WaitsUnderHalfTheShortestGapForTheLock_AndIsNeverRetried()
    {
        var method = typeof(BackgroundMatchingWorker).GetMethod(nameof(BackgroundMatchingWorker.RunAsync))!;
        var lockAttribute = method.GetCustomAttributesData()
            .Single(attribute => attribute.AttributeType == typeof(DisableConcurrentExecutionAttribute));
        var waitSeconds = (int)lockAttribute.ConstructorArguments
            .Single(argument => argument.ArgumentType == typeof(int)).Value!;

        waitSeconds.ShouldBe(120);
        TimeSpan.FromSeconds(waitSeconds)
            .ShouldBeLessThan(CronMinuteField.Gaps(BackgroundMatchingWorker.Cron).Min() / 2);
        method.GetCustomAttribute<AutomaticRetryAttribute>().ShouldNotBeNull().Attempts.ShouldBe(0,
            "the next tick is the retry; the watermark and the Pending rows are the queue");
    }
}
