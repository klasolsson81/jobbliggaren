using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.Auditing;
using NetArchTest.Rules;
using Shouldly;

namespace JobbPilot.Architecture.Tests;

/// <summary>
/// ADR 0032-amendment 2026-05-23 — snapshot-retention. Låser konsumtionslistan
/// för <c>IJobAdSnapshotMissTracker</c>-porten + den nya Infrastructure-
/// entiteten <c>JobAdSnapshotMiss</c>. Mönster paritet
/// <see cref="AuditingLayerTests"/> (IAccountHardDeleter / IRecruiterPiiPurger
/// / ISystemEventAuditor).
/// </summary>
public class JobAdRetentionLayerTests
{
    private const string MissTrackerFqn =
        "JobbPilot.Application.JobAds.Abstractions.IJobAdSnapshotMissTracker";

    [Fact]
    public void IJobAdSnapshotMissTracker_in_Application_should_only_be_referenced_by_sync_and_retention_jobs()
    {
        // SnapshotJob konsumerar ApplyAsync (efter komplett snapshot);
        // RetainJob konsumerar ArchiveJobAdsWithMissCountAtLeastAsync.
        // Andra Application-typer ska inte beröra miss-tabellen.
        var consumers = Types.InAssembly(typeof(JobbPilot.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOn(MissTrackerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[]
        {
            "SyncPlatsbankenSnapshotJob",
            "RetainPlatsbankenJobAdsJob"
        };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IJobAdSnapshotMissTracker får endast konsumeras av " +
            $"SyncPlatsbankenSnapshotJob (ApplyAsync) eller " +
            $"RetainPlatsbankenJobAdsJob (ArchiveJobAdsWithMissCountAtLeastAsync) " +
            $"per ADR 0032-amendment 2026-05-23. Otillåtna: " +
            $"{string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IJobAdSnapshotMissTracker_in_Infrastructure_should_only_be_referenced_by_impl_or_DI()
    {
        var consumers = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveDependencyOn(MissTrackerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        var allowed = new[] { "JobAdSnapshotMissTracker", "DependencyInjection" };
        var unauthorized = consumers.Where(c => !allowed.Contains(c)).ToList();

        unauthorized.ShouldBeEmpty(
            $"IJobAdSnapshotMissTracker i Infrastructure får endast konsumeras av " +
            $"JobAdSnapshotMissTracker (impl) eller DependencyInjection (registrering). " +
            $"Otillåtna: {string.Join(", ", unauthorized)}");
    }

    [Fact]
    public void IJobAdSnapshotMissTracker_should_not_be_referenced_directly_by_Worker()
    {
        // Worker konsumerar retention-jobben (Application-orchestrators) via
        // Hangfire AddOrUpdate<TWorker> som delegerar till TJob. Direkt
        // dependency på IJobAdSnapshotMissTracker från Worker-typer är otillåtet —
        // det skulle bryta orchestrator-mönstret (ADR 0023).
        var consumers = Types.InAssembly(typeof(WorkerSystemUser).Assembly)
            .That()
            .HaveDependencyOn(MissTrackerFqn)
            .GetTypes()
            .Select(t => t.Name)
            .ToList();

        consumers.ShouldBeEmpty(
            $"Worker får inte referera IJobAdSnapshotMissTracker direkt — gå via " +
            $"RetainPlatsbankenJobAdsJob / SyncPlatsbankenSnapshotJob i Application. " +
            $"Otillåtna: {string.Join(", ", consumers)}");
    }

    [Fact]
    public void JobAdSnapshotMiss_entity_is_internal_to_Infrastructure()
    {
        // Paritet UserDataKey (TD-13 C2): Infrastructure-entitet får ALDRIG
        // exponeras via IAppDbContext eller Application-typer. Den är
        // bookkeeping, inte ubiquitous language (Evans 2003 §5).
        var entity = typeof(JobbPilot.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMiss);
        entity.Assembly.ShouldBe(typeof(AppDbContext).Assembly,
            "JobAdSnapshotMiss måste bo i Infrastructure-assemblyn.");
    }

    [Fact]
    public void IAppDbContext_should_not_expose_JobAdSnapshotMiss_DbSet()
    {
        // ISP-skydd (Martin 2017 kap. 10) — porten ska inte växa med
        // Infrastructure-bookkeeping-typer. Verifiera att IAppDbContext inte
        // har en DbSet<JobAdSnapshotMiss>-property.
        var dbContextPort = typeof(JobbPilot.Application.Common.Abstractions.IAppDbContext);
        var properties = dbContextPort.GetProperties();
        var leakedDbSet = properties.FirstOrDefault(p =>
            p.PropertyType.IsGenericType
            && p.PropertyType.GenericTypeArguments[0].Name == "JobAdSnapshotMiss");

        leakedDbSet.ShouldBeNull(
            "IAppDbContext får inte exponera DbSet<JobAdSnapshotMiss> — entiteten " +
            "är Infrastructure-bookkeeping bakom IJobAdSnapshotMissTracker (ADR 0032-" +
            "amendment 2026-05-23, paritet UserDataKey TD-13 C2).");
    }
}
