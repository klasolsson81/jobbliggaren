using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

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
        var domainResult = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .That()
            .ResideInNamespaceMatching("^Jobbliggaren\\.Domain\\.(?!Auditing).*")
            .ShouldNot()
            .HaveDependencyOn("Jobbliggaren.Domain.Auditing")
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
        var assembly = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

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
    public void No_command_should_implement_both_single_and_batch_audit_markers()
    {
        // AuditBehavior checks IBatchAuditableCommand<T> BEFORE the single
        // marker and would silently ignore IAuditableCommand<T> on the same
        // command — implementing both is always a bug (two audit intents, one
        // execution). See the IBatchAuditableCommand XML doc.
        var assembly = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

        var doubleMarked = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .Where(t =>
            {
                var generics = t.GetInterfaces().Where(i => i.IsGenericType).ToList();
                return generics.Any(i =>
                        i.GetGenericTypeDefinition() == typeof(IAuditableCommand<>))
                    && generics.Any(i =>
                        i.GetGenericTypeDefinition() == typeof(IBatchAuditableCommand<>));
            })
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        doubleMarked.ShouldBeEmpty(
            $"Commands implementing BOTH audit markers: {string.Join(", ", doubleMarked)}");
    }

    [Fact]
    public void IBatchAuditableCommand_implementations_should_reside_in_Commands_namespaces()
    {
        // Parity with the single-marker rule above: the batch marker inherits
        // the non-generic IAuditableCommand, so the existing scan already
        // covers it — this test pins the intent explicitly should that
        // inheritance ever change.
        var assembly = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

        var nonCommandImplementations = assembly.GetTypes()
            .Where(t => !t.IsInterface
                        && !t.IsAbstract
                        && t.GetInterfaces().Any(i => i.IsGenericType
                            && i.GetGenericTypeDefinition() == typeof(IBatchAuditableCommand<>))
                        && !(t.Namespace?.Contains(".Commands.", StringComparison.Ordinal) ?? false))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        nonCommandImplementations.ShouldBeEmpty(
            $"IBatchAuditableCommand-implementationer utanför Commands-namespaces: " +
            $"{string.Join(", ", nonCommandImplementations)}");
    }

    [Fact]
    public void AuditBehavior_should_reside_in_Application_Common_Auditing()
    {
        // Behavior-placering verifieras genom typ-uppslag (kompilerings-tid:
        // om den flyttas bryter detta build:t)
        var behaviorType = typeof(AuditBehavior<,>);
        behaviorType.Namespace.ShouldBe("Jobbliggaren.Application.Common.Auditing");
    }
}
