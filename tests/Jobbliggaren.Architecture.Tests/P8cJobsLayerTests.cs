using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads;
using Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;
using Jobbliggaren.Worker.Hosting;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// F2-P8c arch-isolation per ADR 0032 §3 + §5 + CTO-rond 2026-05-13.
/// Säkerställer att Hangfire-jobben + DbExceptionInspector lever i rätt lager
/// och inte läcker mot fel beroenden.
/// </summary>
public class P8cJobsLayerTests
{
    [Fact]
    public void SyncPlatsbankenStreamJob_resides_in_Application_layer()
    {
        // Job-orchestrator bor i Application (testbart utan Hangfire).
        var jobType = typeof(SyncPlatsbankenStreamJob);
        jobType.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            "SyncPlatsbankenStreamJob ska bo i Jobbliggaren.Application, inte Worker/Infrastructure.");
        jobType.Namespace.ShouldBe("Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken");
    }

    [Fact]
    public void SyncPlatsbankenSnapshotJob_resides_in_Application_layer()
    {
        var jobType = typeof(SyncPlatsbankenSnapshotJob);
        jobType.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            "SyncPlatsbankenSnapshotJob ska bo i Jobbliggaren.Application, inte Worker/Infrastructure.");
        jobType.Namespace.ShouldBe("Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken");
    }

    [Fact]
    public void PurgeStaleRawPayloadsJob_resides_in_Application_layer()
    {
        var jobType = typeof(PurgeStaleRawPayloadsJob);
        jobType.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            "PurgeStaleRawPayloadsJob ska bo i Jobbliggaren.Application, inte Worker/Infrastructure.");
        jobType.Namespace.ShouldBe("Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads");
    }

    [Fact]
    public void SyncPlatsbankenStreamWorker_resides_in_Worker_assembly()
    {
        // Hangfire-attribut-wrapper är Worker-specifik (DisableConcurrentExecution)
        // och får inte läcka in i Application-lagret (Clean Arch).
        var workerType = typeof(SyncPlatsbankenStreamWorker);
        workerType.Assembly.GetName().Name.ShouldBe("Jobbliggaren.Worker");
        workerType.Namespace.ShouldBe("Jobbliggaren.Worker.Hosting");
    }

    [Fact]
    public void SyncPlatsbankenSnapshotWorker_resides_in_Worker_assembly()
    {
        // Snapshot-wrappern (DisableConcurrentExecution(3600), root-cause-fix
        // 2026-05-16) — samma Clean-Arch-regel som stream-wrappern: Hangfire-
        // attribut får inte läcka in i Application-lagret.
        var workerType = typeof(SyncPlatsbankenSnapshotWorker);
        workerType.Assembly.GetName().Name.ShouldBe("Jobbliggaren.Worker");
        workerType.Namespace.ShouldBe("Jobbliggaren.Worker.Hosting");
    }

    [Fact]
    public void UpsertExternalJobAdCommand_is_not_IAuditableCommand()
    {
        // Per CTO-rond 2026-05-13 punkt 1: aggregerad audit per job-run, inte
        // per item. Per-item-audit hade gett ~50k rader/dygn (spam mot GDPR Art. 30).
        typeof(IAuditableCommand).IsAssignableFrom(typeof(UpsertExternalJobAdCommand))
            .ShouldBeFalse(
                "UpsertExternalJobAdCommand får INTE vara IAuditableCommand — " +
                "audit aggregeras per job-run av orchestrator (ADR 0032 §8).");
    }

    [Fact]
    public void ArchiveExternalJobAdCommand_is_not_IAuditableCommand()
    {
        typeof(IAuditableCommand).IsAssignableFrom(typeof(ArchiveExternalJobAdCommand))
            .ShouldBeFalse(
                "ArchiveExternalJobAdCommand får INTE vara IAuditableCommand — " +
                "audit aggregeras per job-run av orchestrator (ADR 0032 §8).");
    }

    [Fact]
    public void IDbExceptionInspector_port_resides_in_Application_Common_Abstractions()
    {
        // ADR 0032 §5 + CLAUDE.md §5.1 (DIP) — Application äger porten,
        // Infrastructure binder mot Npgsql.
        var portType = typeof(IDbExceptionInspector);
        portType.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
        portType.Namespace.ShouldBe("Jobbliggaren.Application.Common.Abstractions");
    }

    [Fact]
    public void DbExceptionInspector_impl_resides_in_Infrastructure_Persistence()
    {
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;
        var implType = infrastructureAsm.GetType("Jobbliggaren.Infrastructure.Persistence.DbExceptionInspector");
        implType.ShouldNotBeNull(
            "DbExceptionInspector-impl ska bo i Jobbliggaren.Infrastructure.Persistence.");
        typeof(IDbExceptionInspector).IsAssignableFrom(implType).ShouldBeTrue();
    }

    [Fact]
    public void IDbExceptionInspector_signature_does_not_reference_Npgsql_types()
    {
        // Porten får INTE läcka leverantörs-typer. Den enda parametern ska vara
        // EF Core:s DbUpdateException — inte PostgresException.
        var portType = typeof(IDbExceptionInspector);
        var method = portType.GetMethod(nameof(IDbExceptionInspector.IsUniqueConstraintViolation));
        method.ShouldNotBeNull();
        var paramTypes = method.GetParameters();
        paramTypes.Length.ShouldBe(1);
        paramTypes[0].ParameterType.ShouldBe(typeof(DbUpdateException),
            "IDbExceptionInspector ska bara ta EF Core:s DbUpdateException, " +
            "aldrig PostgresException eller annan Npgsql-typ.");
    }
}
