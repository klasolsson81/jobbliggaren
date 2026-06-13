using System.Reflection;
using System.Text.Json;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Taxonomy;

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
        "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    private const string Klass2ResourceName =
        "Jobbliggaren.Infrastructure.Taxonomy.klass2-taxonomy.json";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = LoadSnapshot();
        var klass2 = LoadKlass2();
        var version = CompositeVersion(snapshot, klass2);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var meta = await db.Set<TaxonomySnapshotMeta>()
                .FirstOrDefaultAsync(cancellationToken);

            if (meta is not null && meta.TaxonomyVersion == version)
            {
                LogUpToDate(logger, version);
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
            if (meta is not null && meta.TaxonomyVersion == version)
            {
                LogUpToDate(logger, version);
                return;
            }

            await db.Set<TaxonomyConcept>().ExecuteDeleteAsync(cancellationToken);

            var rows = MapRows(snapshot, klass2);
            db.Set<TaxonomyConcept>().AddRange(rows);

            if (meta is null)
            {
                db.Set<TaxonomySnapshotMeta>().Add(new TaxonomySnapshotMeta
                {
                    TaxonomyVersion = version,
                    SeededAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                meta.TaxonomyVersion = version;
                meta.SeededAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            LogSeeded(logger, rows.Count, version);
        }
        catch (PostgresException ex)
            when (ex.SqlState == "42P01" && IsSchemaInitGracePeriod(hostEnvironment))
        {
            // 42P01 = undefined_table. I prod kör Jobbliggaren.Migrate DDL FÖRE
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
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
        return JsonSerializer.Deserialize<TaxonomySnapshotFile>(stream)
            ?? throw new InvalidOperationException(
                "taxonomy-snapshot.json deserialiserade till null.");
    }

    // ADR 0043-amendment 2026-06-13 — frusen Klass 2 (anställningsform +
    // omfattning). Separat embedded resource (CTO BESLUT 1 Variant B).
    internal static Klass2TaxonomyFile LoadKlass2()
    {
        var asm = typeof(TaxonomySnapshotSeeder).Assembly;
        using var stream = asm.GetManifestResourceStream(Klass2ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Klass 2-taxonomi saknas: {Klass2ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
        return JsonSerializer.Deserialize<Klass2TaxonomyFile>(stream)
            ?? throw new InvalidOperationException(
                "klass2-taxonomy.json deserialiserade till null.");
    }

    // Komposit-idempotens-nyckel: båda resursernas versioner. När Klass 2
    // adderas (eller bumpas) ändras nyckeln → re-seed triggas på redan-seedade
    // DB:er (meta lagrar t.ex. "30" → "30+klass2-1"). Bump endera versionen
    // för att tvinga om-seed.
    internal static string CompositeVersion(
        TaxonomySnapshotFile snapshot, Klass2TaxonomyFile klass2)
        => $"{snapshot.TaxonomyVersion}+klass2-{klass2.Version}";

    internal static List<TaxonomyConcept> MapRows(
        TaxonomySnapshotFile snapshot, Klass2TaxonomyFile klass2)
    {
        // Kapacitets-hint: regioner + kommuner + yrkesområden + yrken +
        // yrkesgrupper + Klass 2 (anställningsform + omfattning).
        var rows = new List<TaxonomyConcept>(
            snapshot.Regions.Count
            + snapshot.Regions.Sum(r => r.Municipalities?.Count ?? 0)
            + snapshot.OccupationFields.Count
            + snapshot.OccupationFields.Sum(f =>
                f.Occupations.Count + (f.OccupationGroups?.Count ?? 0))
            + klass2.EmploymentTypes.Count
            + klass2.WorktimeExtents.Count);

        foreach (var r in snapshot.Regions)
        {
            rows.Add(new TaxonomyConcept
            {
                ConceptId = r.ConceptId,
                Kind = TaxonomyConceptKind.Region,
                Label = r.Label,
            });

            // ADR 0043-amendment 2026-06-08 — kommun (parent = län, 1:1).
            foreach (var m in r.Municipalities ?? [])
            {
                rows.Add(new TaxonomyConcept
                {
                    ConceptId = m.ConceptId,
                    Kind = TaxonomyConceptKind.Municipality,
                    Label = m.Label,
                    ParentConceptId = r.ConceptId,
                });
            }
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

            // ADR 0043-amendment 2026-06-08 — yrkesgrupp/ssyk-level-4
            // (parent = yrkesområde, 1:1). Primärt yrke-filter (ADR 0067 Beslut 1).
            foreach (var g in f.OccupationGroups ?? [])
            {
                rows.Add(new TaxonomyConcept
                {
                    ConceptId = g.ConceptId,
                    Kind = TaxonomyConceptKind.OccupationGroup,
                    Label = g.Label,
                    ParentConceptId = f.ConceptId,
                });
            }
        }

        // ADR 0043-amendment 2026-06-13 — Klass 2: anställningsform + omfattning.
        // PLATTA/föräldralösa (ingen ParentConceptId) — till skillnad mot kommun/
        // yrkesgrupp. Frusen embedded källa (CTO BESLUT 1 Variant B).
        foreach (var e in klass2.EmploymentTypes)
        {
            rows.Add(new TaxonomyConcept
            {
                ConceptId = e.ConceptId,
                Kind = TaxonomyConceptKind.EmploymentType,
                Label = e.Label,
            });
        }

        foreach (var w in klass2.WorktimeExtents)
        {
            rows.Add(new TaxonomyConcept
            {
                ConceptId = w.ConceptId,
                Kind = TaxonomyConceptKind.WorktimeExtent,
                Label = w.Label,
            });
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
        Message = "Taxonomi-seed skippad: taxonomy_concepts-tabellen finns inte ännu. Kör migrations innan app-start i prod (Jobbliggaren.Migrate-task).")]
    private static partial void LogSchemaMissing(ILogger logger);
}
