using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Infrastructure.Diagnostics;
using Jobbliggaren.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The host sampler is composed in the Api and nowhere else (#1982, ADR 0158, CTO ruling Q6): nothing in
/// <c>AddPersistence</c> or <c>AddApplication</c>, which the Worker also loads, nothing in the rest of Infrastructure,
/// and nothing in the Worker. The layer tests catch the direction of a dependency; none of them catches a
/// registration that sits in the wrong composition root. The rule names the two types and not the namespace:
/// the Worker's generated Mediator code legitimately references the query handler.
/// </summary>
public class HostObservationCompositionTests
{
    private static readonly string[] Composed =
    [
        typeof(ProcFsHostObservationProbe).FullName!,
        typeof(HostObservationSampler).FullName!,
    ];

    [Fact]
    public void Worker_should_not_reference_the_host_sampler_or_its_probe()
    {
        var result = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .ShouldNot().HaveDependencyOnAny(Composed)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"The host sampler is Api-only, the Worker references it: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_should_not_compose_the_host_sampler_or_its_probe()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .That().DoNotResideInNamespaceStartingWith(typeof(ProcFsHostObservationProbe).Namespace!)
            .ShouldNot().HaveDependencyOnAny(Composed)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"AddPersistence and AddEmailSender are loaded by the Worker too: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_compose_the_host_sampler_or_its_probe()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .That().DoNotResideInNamespaceStartingWith(typeof(HostObservationSampler).Namespace!)
            .ShouldNot().HaveDependencyOnAny(Composed)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"AddApplication is loaded by the Worker too: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
