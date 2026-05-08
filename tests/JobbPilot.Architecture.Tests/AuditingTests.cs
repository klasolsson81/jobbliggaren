using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// Architecture-regler för audit log-infrastruktur per ADR 0022.
/// </summary>
public class AuditingTests
{
    [Fact]
    public void AuditLogEntry_should_have_no_public_setters()
    {
        // Skydd mot regression — flat entity ska bara muteras via static factory
        var publicSetters = typeof(AuditLogEntry).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        publicSetters.ShouldBeEmpty(
            $"AuditLogEntry har public setters: {string.Join(", ", publicSetters)}");
    }

    [Fact]
    public void AuditLogEntry_should_only_be_referenced_from_audit_namespaces()
    {
        // AuditLogEntry är inte ett aggregate — andra Domain-aggregat får inte
        // referera den. Tillåtna konsumenter: Application/Infrastructure/Api/Tester.
        var domainResult = Types.InAssembly(typeof(JobbPilot.Domain.Common.AggregateRoot<>).Assembly)
            .That()
            .ResideInNamespaceMatching("^JobbPilot\\.Domain\\.(?!Auditing).*")
            .ShouldNot()
            .HaveDependencyOn("JobbPilot.Domain.Auditing")
            .GetResult();

        domainResult.IsSuccessful.ShouldBeTrue(
            $"Domain-aggregat utanför Domain.Auditing refererar AuditLogEntry: " +
            $"{string.Join(", ", domainResult.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void IAuditableCommand_implementations_should_reside_in_Commands_namespaces()
    {
        // Per ADR 0022 — markeringen är menad för commands, inte queries eller
        // andra application-typer. Förebygger att queries auditeras av misstag.
        var assembly = typeof(JobbPilot.Application.AssemblyMarker).Assembly;

        var nonCommandImplementations = assembly.GetTypes()
            .Where(t => !t.IsInterface
                        && !t.IsAbstract
                        && typeof(IAuditableCommand).IsAssignableFrom(t)
                        && !(t.Namespace?.Contains(".Commands.", StringComparison.Ordinal) ?? false))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        nonCommandImplementations.ShouldBeEmpty(
            $"IAuditableCommand-implementationer utanför Commands-namespaces: " +
            $"{string.Join(", ", nonCommandImplementations)}");
    }

    [Fact]
    public void AuditBehavior_should_reside_in_Application_Common_Auditing()
    {
        // Behavior-placering verifieras genom typ-uppslag (kompilerings-tid:
        // om den flyttas bryter detta build:t)
        var behaviorType = typeof(AuditBehavior<,>);
        behaviorType.Namespace.ShouldBe("JobbPilot.Application.Common.Auditing");
    }
}
