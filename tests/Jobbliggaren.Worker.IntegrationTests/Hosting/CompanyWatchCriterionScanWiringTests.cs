using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// ADR 0175 D11 (#1663 PR 2). <c>ValidateOnBuild</c> is off in the Worker host, so a missing dependency of the scan
/// would surface only when Hangfire invokes it; this resolves the wrapper through <c>AddCompanyWatchCriterionScan</c>,
/// the registration the host makes (pinned in the Architecture tests), without running it. The fixture builds no
/// host, so <c>ValidateOnStart</c> never fires here; the binding is pinned by a value the fixture alone gives.
/// </summary>
[Collection("Worker")]
public class CompanyWatchCriterionScanWiringTests(WorkerTestFixture fixture)
{
    [Fact]
    public void The_wrapper_resolves_through_the_hosts_registration_and_binds_its_own_section()
    {
        using var scope = fixture.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionScanWorker>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IOptions<CompanyWatchCriterionScanOptions>>()
            .Value.CadenceCron.ShouldBe("44 4 * * *");
    }

    [Fact]
    public void The_wrapper_allows_no_retry_and_no_overlap_on_a_lock_of_its_own()
    {
        var run = typeof(CompanyWatchCriterionScanWorker).GetMethod(nameof(CompanyWatchCriterionScanWorker.RunAsync))!;

        run.GetCustomAttribute<AutomaticRetryAttribute>().ShouldNotBeNull().Attempts.ShouldBe(0);
        var scanLock = ResourceOf(
            run.GetCustomAttribute<DisableConcurrentExecutionAttribute>().ShouldNotBeNull(),
            Job.FromExpression<CompanyWatchCriterionScanWorker>(w => w.RunAsync(CancellationToken.None)));
        var followLock = ResourceOf(
            typeof(CompanyWatchScanWorker).GetMethod(nameof(CompanyWatchScanWorker.RunAsync))!
                .GetCustomAttribute<DisableConcurrentExecutionAttribute>()!,
            Job.FromExpression<CompanyWatchScanWorker>(w => w.RunAsync(CancellationToken.None)));

        scanLock.ShouldNotBeNullOrEmpty();
        scanLock.ShouldNotBe(followLock, "the scan shares no lock with the follow scan (ADR 0175 D2)");
    }

    // Hangfire keys the (int) constructor's lock on the job's own Type.Method (the GetResource(Job) the
    // materialisation lock pins read the same way).
    private static string? ResourceOf(DisableConcurrentExecutionAttribute attribute, Job job)
    {
        var getResource = typeof(DisableConcurrentExecutionAttribute).GetMethod(
            "GetResource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        getResource.ShouldNotBeNull("Hangfire's GetResource(Job) is gone; re-derive this pin against the new API.");
        return (string?)getResource.Invoke(attribute, [job]);
    }
}
