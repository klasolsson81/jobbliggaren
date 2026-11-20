using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.RecentJobSearches.Behaviors;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// #2199 PR 2b. The capture behaviour is an open generic registered for every message in both hosts, so the
/// Worker constructs it on each send, and <c>ValidateOnBuild</c> skips open generics. Its new dependency, the
/// capture gate, needs the religious occupations that <c>AddPersistence</c> registers; this resolves the
/// closed behaviour through the Worker's graph, where a missing registration would otherwise surface only
/// when a job first sends a message.
/// </summary>
[Collection("Worker")]
public class RecentJobSearchCaptureWiringTests(WorkerTestFixture fixture)
{
    [Fact]
    public void The_capture_behaviour_and_its_gate_resolve_in_the_Worker_graph()
    {
        using var scope = fixture.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<RecentSearchCaptureGate>().ShouldNotBeNull();
        scope.ServiceProvider.GetServices<IPipelineBehavior<ListJobAdsQuery, JobAdListPage>>()
            .OfType<RecentJobSearchCaptureBehavior<ListJobAdsQuery, JobAdListPage>>()
            .ShouldHaveSingleItem();
    }
}
