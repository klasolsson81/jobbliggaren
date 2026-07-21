using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAdExtraction;

/// <summary>
/// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — the ingest funnel persists
/// <see cref="ExtractedTermKind.Requirement"/> terms in <c>extracted_terms</c> for an
/// ad carrying must_have/nice_to_have skills, on BOTH the Add and the Update path of
/// <see cref="UpsertExternalJobAdCommandHandler"/>. Real Postgres (Testcontainers —
/// the jsonb VO persistence + STORED <c>extracted_lexemes</c> only exist on the real
/// engine; NEVER EF-InMemory). Self-contained fixture mirroring
/// <see cref="JobAdExtractedTermsPersistenceTests"/>; the REAL extractor (its
/// requirement pass) is exercised through the REAL handler (the single write funnel).
///
/// RED until: <c>JobAdImportItem.Requirements</c> + <c>JobAdExtractionInput.
/// Requirements</c> + the extractor's requirement pass + the
/// handler's <c>Extract</c> closure folded into <c>Import</c>/<c>UpdateFromSource</c> (#874) ship.
/// </summary>
public sealed class JobAdRequirementsIngestTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _provider = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_postgres.GetConnectionString(),
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // A must_have + nice_to_have skill requirement on the import item. The concept-ids
    // are arbitrary (pre-linked by JobTech) — the requirement pass maps them directly,
    // no taxonomy lookup.
    private static readonly IReadOnlyList<JobAdRequirement> Requirements =
    [
        new(ExtractedTermSource.MustHave, "Rq01_must_aaa", "C#", 10),
        new(ExtractedTermSource.NiceToHave, "Rq02_nice_bbb", "Azure", 5),
    ];

    private static JobAdImportItem ItemWithRequirements(
        string externalId,
        string title = "Backend-utvecklare",
        string description = "Vi söker en utvecklare med erfarenhet av ekonomi.") =>
        new(
            ExternalId: externalId,
            Title: title,
            CompanyName: "Klarna",
            Description: description,
            Url: "https://example.com/jobs/" + externalId,
            PublishedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            SanitizedRawPayload: "{\"id\":\"" + externalId + "\"}",
            Facets: TestFacets.FromPayload("{\"id\":\"" + externalId + "\"}"),
            Requirements: Requirements,
            DeclaredContacts: []);

    private static UpsertExternalJobAdCommandHandler NewHandler(IAppDbContext db)
    {
        var stemmer = new SnowballStemmer();
        var analyzer = new LocalTextAnalyzer(stemmer);
        // F4-15 (ADR 0076 Decision 6) — shared SkillTaxonomyIndex (3rd ctor arg).
        var extractor = new JobAdKeywordExtractor(analyzer, stemmer, new SkillTaxonomyIndex(analyzer));
        return new UpsertExternalJobAdCommandHandler(
            db,
            new DbExceptionInspector(),
            extractor,
            new FixedClock(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<UpsertExternalJobAdCommandHandler>.Instance);
    }

    // ===============================================================
    // Add path — a new ad carrying must_have skills gets Requirement terms persisted
    // ===============================================================

    [Fact]
    public async Task Upsert_AddPath_PersistsRequirementTermsInExtractedTerms()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "ext-req-add-int";

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = NewHandler(db);
            var result = await handler.Handle(
                new UpsertExternalJobAdCommand(JobSource.Platsbanken, externalId, ItemWithRequirements(externalId)),
                ct);
            result.Value.ShouldBe(UpsertOutcome.Added);
        }

        await AssertRequirementTermsPersistedAsync(externalId, ct);
    }

    // ===============================================================
    // Update path — re-upserting the same external id keeps Requirement terms
    // ===============================================================

    [Fact]
    public async Task Upsert_UpdatePath_PersistsRequirementTermsInExtractedTerms()
    {
        var ct = TestContext.Current.CancellationToken;
        const string externalId = "ext-req-upd-int";

        // First upsert → Added.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = NewHandler(db);
            await handler.Handle(
                new UpsertExternalJobAdCommand(JobSource.Platsbanken, externalId, ItemWithRequirements(externalId)),
                ct);
        }

        // Second upsert with the SAME external id → UNIQUE-collision → Update path.
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = NewHandler(db);
            var result = await handler.Handle(
                new UpsertExternalJobAdCommand(
                    JobSource.Platsbanken, externalId,
                    ItemWithRequirements(externalId, title: "Senior Backend-utvecklare")),
                ct);
            result.Value.ShouldBe(UpsertOutcome.Updated);
        }

        await AssertRequirementTermsPersistedAsync(externalId, ct);
    }

    private async Task AssertRequirementTermsPersistedAsync(string externalId, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobAd = await db.JobAds.AsNoTracking()
            .FirstAsync(j => j.External!.ExternalId == externalId, ct);

        jobAd.ExtractedTerms.ShouldNotBeNull("ingest-extraktionen ska ha satt extracted_terms.");
        var requirementTerms = jobAd.ExtractedTerms!.Terms
            .Where(t => t.Kind == ExtractedTermKind.Requirement)
            .ToList();

        requirementTerms.ShouldContain(
            t => t.ConceptId == "Rq01_must_aaa" && t.Source == ExtractedTermSource.MustHave,
            "must_have-skill ska bli en Requirement-term (Source=MustHave) i extracted_terms.");
        requirementTerms.ShouldContain(
            t => t.ConceptId == "Rq02_nice_bbb" && t.Source == ExtractedTermSource.NiceToHave,
            "nice_to_have-skill ska bli en Requirement-term (Source=NiceToHave).");

        // The STORED extracted_lexemes projects the requirement concept-ids (so F4-6's
        // GIN overlap sees them).
        var lexemesJson = await db.JobAds.AsNoTracking()
            .Where(j => j.External!.ExternalId == externalId)
            .Select(j => EF.Property<string?>(j, "ExtractedLexemes"))
            .FirstAsync(ct);
        lexemesJson.ShouldNotBeNull();
        // requirement-concept_id ska projiseras till STORED extracted_lexemes (F4-6 GIN-overlap).
        lexemesJson!.ShouldContain("Rq01_must_aaa");
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
