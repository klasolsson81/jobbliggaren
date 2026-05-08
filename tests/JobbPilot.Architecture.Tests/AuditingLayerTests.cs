using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// Architecture-regler för audit-bypass-portar per ADR 0024 delbeslut 1 + 3.
///
/// Audit-bypass-disciplinen: portar som muterar audit_log utanför normal
/// AuditBehavior-pipelinen (DDL-anrop, anonymiserings-cascade) får anropas
/// endast från explicit godkända consumers. Tester här låser konsumtions-
/// listan så att framtida läckage fångas av build-pipeline:n.
/// </summary>
public class AuditingLayerTests
{
    private const string AuditPartitionMaintainerFqn =
        "JobbPilot.Application.Common.Auditing.IAuditPartitionMaintainer";

    [Fact]
    public void IAuditPartitionMaintainer_in_Application_should_only_be_referenced_by_AuditLogRetentionJob()
    {
        // Application-lagret: bara orchestrator-jobbet får konsumera porten.
        // Andra Application-typer ska gå via Mediator + AuditBehavior, inte
        // bypassa via IAuditPartitionMaintainer.
        var consumers = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOn(AuditPartitionMaintainerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "AuditLogRetentionJob" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IAuditPartitionMaintainer får endast konsumeras av AuditLogRetentionJob i " +
            $"Application-lagret (ADR 0024 D1 audit-bypass-disciplin). Otillåtna: " +
            $"{string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IAuditPartitionMaintainer_in_Infrastructure_should_only_be_referenced_by_impl_or_DI()
    {
        // Infrastructure-lagret: bara impl-klassen + DI-registreringen får referera.
        // Andra Infrastructure-typer (t.ex. annan EF-kod) ska inte beröra audit-DDL.
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(AuditPartitionMaintainerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "AuditPartitionMaintainer", "DependencyInjection" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IAuditPartitionMaintainer i Infrastructure får endast konsumeras av " +
            $"AuditPartitionMaintainer (impl) eller DependencyInjection (registrering). " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IAuditPartitionMaintainer_should_not_be_referenced_directly_by_Worker()
    {
        // Worker konsumerar AuditLogRetentionJob (orchestrator i Application-lagret) via
        // Hangfire AddOrUpdate<TJob>. Direkt dependency på IAuditPartitionMaintainer från
        // Worker-typer är otillåtet — det skulle bryta orchestrator-mönstret från
        // ADR 0023 där Worker bara binder cron, inte komponerar logik.
        var consumers = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .That()
            .HaveDependencyOn(AuditPartitionMaintainerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        consumers.ShouldBeEmpty(
            $"Worker får inte referera IAuditPartitionMaintainer direkt — gå via " +
            $"AuditLogRetentionJob i Application-lagret. Otillåtna: " +
            $"{string.Join(", ", consumers)}");
    }
}
