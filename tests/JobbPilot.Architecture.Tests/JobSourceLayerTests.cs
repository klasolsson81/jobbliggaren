using NetArchTest.Rules;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// F2-P8b anti-regression. JobTech/JobSource-koden ska respektera Clean Arch:
/// Domain får INTE bero på Refit/HttpClient, Application får INTE bero på
/// Refit eller konkreta JobTech-DTOs (wire-format ska stanna i Infrastructure).
/// </summary>
public class JobSourceLayerTests
{
    [Fact]
    public void Domain_should_not_depend_on_Refit_or_HttpClient()
    {
        var result = Types.InAssembly(typeof(JobbPilot.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Refit",
                "System.Net.Http",
                "Microsoft.Extensions.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Domain läcker mot Refit/HTTP: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_Refit()
    {
        var result = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Refit")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot Refit: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void IJobSource_is_in_Application_layer()
    {
        // ADR 0032 §2 — IJobSource är Application-port, inte Infrastructure-detalj.
        var ijobSource = typeof(JobbPilot.Application.JobAds.Abstractions.IJobSource);
        ijobSource.Assembly.ShouldBe(typeof(JobbPilot.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobTech_wire_types_are_internal_to_Infrastructure()
    {
        // Refit-interfaces + DTOs ska vara internal så de inte kan refereras från
        // Application/Api/Worker (wire-format-koppling skulle bryta DI-isolation).
        var infrastructureAsm = typeof(JobbPilot.Infrastructure.AssemblyMarker).Assembly;

        var publicJobTechTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "JobbPilot.Infrastructure.JobSources.Platsbanken"
                        && t.IsPublic
                        && t.Name.StartsWith("JobTech", StringComparison.Ordinal)
                        && t.Name != "JobTechOptions"
                        && t.Name != "JobTechPayloadSanitizer")
            .Select(t => t.FullName)
            .ToList();

        publicJobTechTypes.ShouldBeEmpty(
            "Wire-format-typer ska vara internal (JobTechOptions och JobTechPayloadSanitizer " +
            $"är medvetna undantag). Public: {string.Join(", ", publicJobTechTypes!)}");
    }
}
