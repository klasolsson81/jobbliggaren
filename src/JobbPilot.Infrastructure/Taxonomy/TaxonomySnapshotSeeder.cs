using System.Reflection;
using System.Text.Json;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 (Variant A, MAP-1) — bootstrap-jobb som seedar
/// <c>taxonomy_concepts</c> från den committade embedded
/// <c>taxonomy-snapshot.json</c>. Idempotent + version-medveten: skippar
/// om <see cref="TaxonomySnapshotMeta.TaxonomyVersion"/> redan matchar
/// snapshotens version (skriver inte ~2 700 rader vid varje Api-task-start);
/// re-seedar när snapshot regenererats + committats (version bumpad). Speglar
/// <c>IdempotentAdminRoleSeeder</c>-mönstret (IHostedService, scope,
/// schema-grace-period, LoggerMessage). Off-search-path — rör aldrig
/// sök-/filter-vägen (ADR 0043 Beslut E).
/// </summary>
internal sealed partial class TaxonomySnapshotSeeder(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    ILogger<TaxonomySnapshotSeeder> logger)
    : IHostedService
{
    private const string ResourceName =
        "JobbPilot.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = LoadSnapshot();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var meta = await db.Set<TaxonomySnapshotMeta>()
                .FirstOrDefaultAsync(cancellationToken);

            if (meta is not null && meta.TaxonomyVersion == snapshot.TaxonomyVersion)
            {
                LogUpToDate(logger, snapshot.TaxonomyVersion);
                return;
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            // Advisory-lock: två Api-tasks som startar samtidigt får inte
            // race:a delete+insert (PK-konflikt på meta-raden). Lås släpps
            // vid transaktions-slut (xact-scoped).
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(4307001)", cancellationToken);

            // Re-läs meta inom låset (annan task kan ha seedat medan vi väntade).
            meta = await db.Set<TaxonomySnapshotMeta>()
                .FirstOrDefaultAsync(cancellationToken);
            if (meta is not null && meta.TaxonomyVersion == snapshot.TaxonomyVersion)
            {
                LogUpToDate(logger, snapshot.TaxonomyVersion);
                return;
            }

            await db.Set<TaxonomyConcept>().ExecuteDeleteAsync(cancellationToken);

            var rows = MapRows(snapshot);
            db.Set<TaxonomyConcept>().AddRange(rows);

            if (meta is null)
            {
                db.Set<TaxonomySnapshotMeta>().Add(new TaxonomySnapshotMeta
                {
                    TaxonomyVersion = snapshot.TaxonomyVersion,
                    SeededAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                meta.TaxonomyVersion = snapshot.TaxonomyVersion;
                meta.SeededAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            LogSeeded(logger, rows.Count, snapshot.TaxonomyVersion);
        }
        catch (PostgresException ex)
            when (ex.SqlState == "42P01" && IsSchemaInitGracePeriod(hostEnvironment))
        {
            // 42P01 = undefined_table. I prod kör JobbPilot.Migrate DDL FÖRE
            // Api-tasken — ska aldrig inträffa där. I integration-test-fixturer
            // triggas host-start före migrations (samma catch-22 som
            // IdempotentAdminRoleSeeder). Gate:ad på Dev/Test → fail-loud i prod.
            LogSchemaMissing(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Dev/Test får starta utan schema; Prod/Staging måste bubbla
    /// (CLAUDE.md §3.4). Internal static för direkt unit-test.</summary>
    internal static bool IsSchemaInitGracePeriod(IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment("Test");

    internal static TaxonomySnapshotFile LoadSnapshot()
    {
        var asm = typeof(TaxonomySnapshotSeeder).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded taxonomi-snapshot saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i JobbPilot.Infrastructure.csproj.");
        return JsonSerializer.Deserialize<TaxonomySnapshotFile>(stream)
            ?? throw new InvalidOperationException(
                "taxonomy-snapshot.json deserialiserade till null.");
    }

    internal static List<TaxonomyConcept> MapRows(TaxonomySnapshotFile snapshot)
    {
        var rows = new List<TaxonomyConcept>(
            snapshot.Regions.Count + snapshot.OccupationFields.Count);

        foreach (var r in snapshot.Regions)
        {
            rows.Add(new TaxonomyConcept
            {
                ConceptId = r.ConceptId,
                Kind = TaxonomyConceptKind.Region,
                Label = r.Label,
            });
        }

        foreach (var f in snapshot.OccupationFields)
        {
            rows.Add(new TaxonomyConcept
            {
                ConceptId = f.ConceptId,
                Kind = TaxonomyConceptKind.OccupationField,
                Label = f.Label,
            });

            foreach (var o in f.Occupations)
            {
                rows.Add(new TaxonomyConcept
                {
                    ConceptId = o.ConceptId,
                    Kind = TaxonomyConceptKind.Occupation,
                    Label = o.Label,
                    ParentConceptId = f.ConceptId,
                });
            }
        }

        return rows;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Taxonomi-snapshot seedad: {RowCount} rader, version {Version}.")]
    private static partial void LogSeeded(ILogger logger, int rowCount, string version);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Taxonomi-snapshot redan aktuell (version {Version}) — skippar seed.")]
    private static partial void LogUpToDate(ILogger logger, string version);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Taxonomi-seed skippad: taxonomy_concepts-tabellen finns inte ännu. Kör migrations innan app-start i prod (JobbPilot.Migrate-task).")]
    private static partial void LogSchemaMissing(ILogger logger);
}
