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

    private const string AuditTrailEraserFqn =
        "JobbPilot.Application.Common.Auditing.IAuditTrailEraser";

    private const string AccountHardDeleterFqn =
        "JobbPilot.Application.Auth.Jobs.HardDeleteAccounts.IAccountHardDeleter";

    private const string IpAnonymizerFqn =
        "JobbPilot.Application.Common.Auditing.IIpAnonymizer";

    private const string SystemEventAuditorFqn =
        "JobbPilot.Application.Common.Auditing.ISystemEventAuditor";

    private const string RecruiterPiiPurgerFqn =
        "JobbPilot.Application.JobAds.Abstractions.IRecruiterPiiPurger";

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

    // ─── IAuditTrailEraser (ADR 0024 D3 — GDPR Art. 17-anonymisering) ───
    //
    // Porten anropas inte direkt från Application-lagret (orchestrator
    // HardDeleteAccountsJob går via IAccountHardDeleter, vars Infrastructure-
    // impl AccountHardDeleter använder IAuditTrailEraser). Application-
    // konsument-listan ska vara tom.

    [Fact]
    public void IAuditTrailEraser_should_not_be_referenced_from_Application()
    {
        var consumers = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOn(AuditTrailEraserFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        consumers.ShouldBeEmpty(
            $"IAuditTrailEraser ska inte konsumeras från Application-lagret " +
            $"(porten anropas av AccountHardDeleter i Infrastructure, ej direkt " +
            $"av orchestrator). Otillåtna: {string.Join(", ", consumers)}");
    }

    [Fact]
    public void IAuditTrailEraser_in_Infrastructure_should_only_be_referenced_by_impl_or_consumer()
    {
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(AuditTrailEraserFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        // Tillåtna: AuditTrailEraser (impl) + AccountHardDeleter (anropare) + DependencyInjection
        var allowed = new[] { "AuditTrailEraser", "AccountHardDeleter", "DependencyInjection" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IAuditTrailEraser i Infrastructure får endast konsumeras av " +
            $"AuditTrailEraser (impl), AccountHardDeleter (anropare) eller DependencyInjection " +
            $"(registrering). Otillåtna: {string.Join(", ", unauthorized)}");
    }

    // ─── IAccountHardDeleter (ADR 0024 D6 — konto hard-delete) ───

    [Fact]
    public void IAccountHardDeleter_in_Application_should_only_be_referenced_by_HardDeleteAccountsJob()
    {
        var consumers = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOn(AccountHardDeleterFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "HardDeleteAccountsJob" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IAccountHardDeleter får endast konsumeras av HardDeleteAccountsJob i " +
            $"Application-lagret. Otillåtna: {string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IAccountHardDeleter_in_Infrastructure_should_only_be_referenced_by_impl_or_DI()
    {
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(AccountHardDeleterFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "AccountHardDeleter", "DependencyInjection" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IAccountHardDeleter i Infrastructure får endast konsumeras av " +
            $"AccountHardDeleter (impl) eller DependencyInjection (registrering). " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }

    // ─── IIpAnonymizer (ADR 0024 D7 — delad PII-redaction-yta) ───
    //
    // Inte audit-bypass-port — porten kan användas brett. Men eftersom maskningen
    // är gemensam yta mellan audit-pipelinen och app-loggen vill vi låsa
    // konsument-listan så framtida tredje konsument går genom medveten review.

    // ─── ISystemEventAuditor (ADR 0035 — system-event audit-pipeline) ───
    //
    // Bypass-port parallell till IAuditTrailEraser. Konsumeras direkt av
    // Hangfire-jobben som inte passerar Mediator-pipelinen och därmed inte
    // fångas av AuditBehavior. Konsumentlistan låst för att förhindra
    // bypass-spridning till command-handlers.

    [Fact]
    public void ISystemEventAuditor_in_Application_should_only_be_referenced_by_system_jobs()
    {
        var consumers = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOn(SystemEventAuditorFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[]
        {
            "SyncPlatsbankenStreamJob",
            "SyncPlatsbankenSnapshotJob",
            "PurgeStaleRawPayloadsJob",
            // TD-13 C5 (ADR 0049 Beslut 4) — backfill-jobbet skriver
            // FieldEncryptionBackfillRun-audit (GDPR Art. 30 accountability),
            // paritet PurgeStaleRawPayloadsJob. Architect-sanktionerad
            // audit-wire 2026-05-19 (C5-design Q5). Ratchet-tillägg.
            "BackfillFieldEncryptionJob"
        };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"ISystemEventAuditor får endast konsumeras av system-jobben " +
            $"(SyncPlatsbankenStreamJob, SyncPlatsbankenSnapshotJob, " +
            $"PurgeStaleRawPayloadsJob, BackfillFieldEncryptionJob) per " +
            $"ADR 0035. Otillåtna: {string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void ISystemEventAuditor_in_Infrastructure_should_only_be_referenced_by_impl_or_DI()
    {
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(SystemEventAuditorFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "SystemEventAuditor", "DependencyInjection" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"ISystemEventAuditor i Infrastructure får endast konsumeras av " +
            $"SystemEventAuditor (impl) eller DependencyInjection (registrering). " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }

    // ─── IRecruiterPiiPurger (ADR 0032 §8 amendment 2026-05-13 — GDPR Art. 17) ───
    //
    // Postgres-specifik jsonb-sökning kapslas i Infrastructure för att hålla
    // Application Npgsql-fri. Konsumentlista låst för att förhindra direkt
    // PII-purge utanför admin-endpoint-flödet.

    [Fact]
    public void IRecruiterPiiPurger_in_Application_should_only_be_referenced_by_RedactRecruiterPiiCommandHandler()
    {
        var consumers = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOn(RecruiterPiiPurgerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "RedactRecruiterPiiCommandHandler" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IRecruiterPiiPurger får endast konsumeras av " +
            $"RedactRecruiterPiiCommandHandler. Otillåtna: " +
            $"{string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IRecruiterPiiPurger_in_Infrastructure_should_only_be_referenced_by_impl_or_DI()
    {
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(RecruiterPiiPurgerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "RecruiterPiiPurger", "DependencyInjection" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IRecruiterPiiPurger i Infrastructure får endast konsumeras av " +
            $"RecruiterPiiPurger (impl) eller DependencyInjection (registrering). " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IIpAnonymizer_in_Infrastructure_should_only_be_referenced_by_known_consumers()
    {
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(IpAnonymizerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        // IpAnonymizer (impl) + RequestContextProvider (audit-pipeline) +
        // AuthAuditLogger (app-logg) + DependencyInjection (registrering).
        var allowed = new[]
        {
            "IpAnonymizer",
            "RequestContextProvider",
            "AuthAuditLogger",
            "DependencyInjection"
        };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IIpAnonymizer i Infrastructure får endast konsumeras av " +
            $"IpAnonymizer (impl), RequestContextProvider (audit-pipeline), " +
            $"AuthAuditLogger (app-logg) eller DependencyInjection (registrering). " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }
}
