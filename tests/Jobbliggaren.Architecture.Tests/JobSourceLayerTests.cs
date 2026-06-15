using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

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
        var result = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
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
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
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
        var ijobSource = typeof(Jobbliggaren.Application.JobAds.Abstractions.IJobSource);
        ijobSource.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobTech_wire_types_are_internal_to_Infrastructure()
    {
        // Refit-interfaces + DTOs ska vara internal så de inte kan refereras från
        // Application/Api/Worker (wire-format-koppling skulle bryta DI-isolation).
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var publicJobTechTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "Jobbliggaren.Infrastructure.JobSources.Platsbanken"
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

    [Fact]
    public void F4_4b_requirement_wire_POCOs_exist_and_are_internal_to_Infrastructure()
    {
        // Fas 4 STEG 4b (architect Note 4.1) — pin BY NAME that the new must_have/
        // nice_to_have ACL POCOs (JobTechRequirements + JobTechRequirementConcept)
        // exist AND are non-public, parity JobAdKeywordExtractor_and_loader_helpers_
        // are_internal_to_Infrastructure. The general wire-type test above stays green
        // whether or not they exist; THIS test is RED until they ship + forbids a
        // future refactor from accidentally making them public.
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var requirementPocoTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "Jobbliggaren.Infrastructure.JobSources.Platsbanken"
                        && (t.Name.Contains("JobTechRequirement", StringComparison.Ordinal)))
            .ToList();

        requirementPocoTypes.ShouldContain(
            t => t.Name.Contains("JobTechRequirements", StringComparison.Ordinal),
            "JobTechRequirements-POCO saknas i Jobbliggaren.Infrastructure.JobSources.Platsbanken " +
            "(F4-4b ACL-POCO ej skriven än — väntad RED).");
        requirementPocoTypes.ShouldContain(
            t => t.Name.Contains("JobTechRequirementConcept", StringComparison.Ordinal),
            "JobTechRequirementConcept-POCO saknas (F4-4b ACL-POCO ej skriven än — väntad RED).");

        var publicRequirementPocoTypes = requirementPocoTypes
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicRequirementPocoTypes.ShouldBeEmpty(
            "JobTechRequirements/JobTechRequirementConcept ska vara internal (ACL-isolation, " +
            $"Evans 2003 §14). Public: {string.Join(", ", publicRequirementPocoTypes!)}");
    }
}
