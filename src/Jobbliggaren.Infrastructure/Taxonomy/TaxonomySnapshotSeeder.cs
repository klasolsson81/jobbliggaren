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

    private const string SubstitutabilityResourceName =
        "Jobbliggaren.Infrastructure.Taxonomy.occupation-substitutability.json";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = LoadSnapshot();
        var klass2 = LoadKlass2();
        var substitutability = LoadSubstitutability();
        var version = CompositeVersion(snapshot, klass2, substitutability);

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
            await db.Set<TaxonomyRelation>().ExecuteDeleteAsync(cancellationToken);

            var rows = MapRows(snapshot, klass2);
            db.Set<TaxonomyConcept>().AddRange(rows);

            // ADR 0084 — relaterade ssyk-4-grupper (substitutability). Egen tabell,
            // SAMMA transaktion + composite-version-grind → atomisk re-seed (concepts
            // och relations bumpas/skrivs tillsammans, aldrig delvis).
            var relationRows = MapRelationRows(substitutability);
            db.Set<TaxonomyRelation>().AddRange(relationRows);

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

            LogSeeded(logger, rows.Count, relationRows.Count, version);
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

    // ADR 0084 — relaterade yrkesgrupper (substitutability, occupation-name
    // `substitutes` rollat upp till ssyk-4 off-repo via generate-substitutability.mjs).
    // Separat embedded resource (paritet klass2 / Variant A — generatorn rör aldrig
    // den frusna v30-mappen).
    internal static OccupationSubstitutabilityFile LoadSubstitutability()
    {
        var asm = typeof(TaxonomySnapshotSeeder).Assembly;
        using var stream = asm.GetManifestResourceStream(SubstitutabilityResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded substitutability-snapshot saknas: {SubstitutabilityResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
        return JsonSerializer.Deserialize<OccupationSubstitutabilityFile>(stream)
            ?? throw new InvalidOperationException(
                "occupation-substitutability.json deserialiserade till null.");
    }

    // Komposit-idempotens-nyckel: alla tre resursernas versioner. När någon
    // adderas (eller bumpas) ändras nyckeln → re-seed triggas på redan-seedade
    // DB:er (meta lagrar t.ex. "30+klass2-1" → "30+klass2-1+subst-1"). Bump
    // någon version för att tvinga om-seed.
    internal static string CompositeVersion(
        TaxonomySnapshotFile snapshot, Klass2TaxonomyFile klass2,
        OccupationSubstitutabilityFile substitutability)
        => $"{snapshot.TaxonomyVersion}+klass2-{klass2.Version}+subst-{substitutability.Version}";

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

    // ADR 0084 — emitterar taxonomy_relations-rader (ssyk-4 → ssyk-4-kanter) ur den
    // committade substitutability-snapshoten. Filens relationKind (v1 = en typ per
    // fil) mappas EN gång till TaxonomyRelationKind. Helt separat från MapRows som
    // äger taxonomy_concepts (ingen kant rör concept-tabellen).
    internal static List<TaxonomyRelation> MapRelationRows(
        OccupationSubstitutabilityFile substitutability)
    {
        var kind = MapRelationKind(substitutability.RelationKind);
        var rows = new List<TaxonomyRelation>(
            substitutability.Relations.Sum(r => r.RelatedConceptIds.Count));

        foreach (var relation in substitutability.Relations)
        {
            foreach (var relatedConceptId in relation.RelatedConceptIds)
            {
                rows.Add(new TaxonomyRelation
                {
                    SourceConceptId = relation.SourceConceptId,
                    RelatedConceptId = relatedConceptId,
                    Kind = kind,
                });
            }
        }

        return rows;
    }

    // Fail-loud på okänd relationKind — bad data ska aldrig seedas tyst (CLAUDE.md
    // §5: ingen magic string; v1 stödjer endast "substitutability", "related" är en
    // namngiven framtida additiv våg, ADR 0084).
    internal static TaxonomyRelationKind MapRelationKind(string relationKind) => relationKind switch
    {
        "substitutability" => TaxonomyRelationKind.Substitutability,
        _ => throw new InvalidOperationException(
            $"Okänd relationKind '{relationKind}' i occupation-substitutability.json. " +
            "Endast 'substitutability' stöds i v1 (ADR 0084)."),
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Taxonomi-snapshot seedad: {RowCount} concept-rader + {RelationRowCount} relations-rader, version {Version}.")]
    private static partial void LogSeeded(ILogger logger, int rowCount, int relationRowCount, string version);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Taxonomi-snapshot redan aktuell (version {Version}) — skippar seed.")]
    private static partial void LogUpToDate(ILogger logger, string version);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Taxonomi-seed skippad: taxonomy_concepts-tabellen finns inte ännu. Kör migrations innan app-start i prod (Jobbliggaren.Migrate-task).")]
    private static partial void LogSchemaMissing(ILogger logger);
}
