using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAdExtraction;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — the LOCAL extraction backfill
/// (<see cref="BackfillJobAdExtractedTermsJob"/>) against a real Postgres
/// (Testcontainers; the STORED generated <c>extracted_lexemes</c> predicate that
/// drives idempotency only exists on the real engine). Self-contained fixture
/// (own container) + the REAL extractor wired through DI per the job's per-item
/// scope model.
///
/// Pins:
/// <list type="bullet">
/// <item>seeded NULL-extraction rows are all populated (every <c>extracted_lexemes
/// IS NULL</c> row drops to 0);</item>
/// <item>idempotent restart — a second run sees 0 NULL rows and extracts 0;</item>
/// <item><b>THE KEY DIVERGENCE</b> from the Klass2/refetch backfill:
/// <c>IJobSource</c> is NEVER called (this is a pure local re-projection — title +
/// description are already stored, no JobTech GET). Asserted via a substitute
/// whose <c>ReceivedCalls()</c> must be empty.</item>
/// </list>
///
/// RED until the Domain VO + the F4-4 EF mapping/migration + the job ship.
/// </summary>
public sealed class BackfillJobAdExtractedTermsJobTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _provider = default!;
    private IJobSource _jobSource = default!;
    private ISystemEventAuditor _auditor = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _jobSource = Substitute.For<IJobSource>();
        _auditor = Substitute.For<ISystemEventAuditor>();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_postgres.GetConnectionString(),
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());
        // IAppDbContext resolves to the SAME scoped AppDbContext (production mapping).
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // The REAL deterministic extractor (parity the integration suite).
        var stemmer = new SnowballStemmer();
        var analyzer = new LocalTextAnalyzer(stemmer);
        services.AddSingleton<Jobbliggaren.Application.Common.Abstractions.TextAnalysis.IStemmer>(stemmer);
        // F4-15 (ADR 0076 Decision 6) — shared SkillTaxonomyIndex (3rd ctor arg).
        services.AddSingleton<IJobAdKeywordExtractor>(
            new JobAdKeywordExtractor(analyzer, stemmer, new SkillTaxonomyIndex(analyzer)));

        // Collaborators. The IJobSource substitute is the assertion subject — it
        // must remain untouched (local re-projection, NO JobTech re-fetch).
        services.AddSingleton(_jobSource);
        services.AddSingleton(_auditor);
        services.AddSingleton<IDateTimeProvider>(
            new FixedClock(new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero)));
        services.AddSingleton(Options.Create(new BackfillJobAdExtractedTermsOptions
        {
            PerItemDelayMs = 0,
            MaxItemsPerRun = 1_000_000,
            ProgressLogEvery = 1000,
        }));
        // Register the OPEN generic ILogger<> → NullLogger<> so the job's
        // ILogger<BackfillJobAdExtractedTermsJob> ctor dependency resolves.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<BackfillJobAdExtractedTermsJob>();

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

    private static readonly BackfillExtractionRunOptions RunOptions =
        new(PerItemDelayMs: 0, MaxItemsPerRun: 1_000_000, ProgressLogEvery: 1000);

    // ---------------------------------------------------------------
    // Seeding — insert JobAds WITHOUT extraction (extracted_terms NULL), so the
    // STORED extracted_lexemes is NULL → the backfill predicate picks them up.
    // ---------------------------------------------------------------

    private async Task<int> SeedUnextractedAdsAsync(int count, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IDateTimeProvider clock = new FixedClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        for (var i = 0; i < count; i++)
        {
            var company = Company.Create("Klarna").Value;
            var external = ExternalReference.Create(
                JobSource.Platsbanken, Guid.NewGuid().ToString("N")).Value;
            // Text rich enough to extract at least one keyword (so extraction is
            // non-empty for a real assertion that the column is populated).
            var jobAd = JobAd.Import(
                $"Systemutvecklare nummer {i}", company,
                "Vi söker en utvecklare med erfarenhet av ekonomi och ledning.",
                "https://example.com/jobb/" + i, external, "{\"id\":\"x\"}",
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), clock).Value;
            // Deliberately DO NOT call SetExtractedTerms → extracted_terms stays NULL.
            db.JobAds.Add(jobAd);
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<int> CountNullLexemeRowsAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobAds
            .Where(j => EF.Property<string?>(j, "ExtractedLexemes") == null)
            .CountAsync(ct);
    }

    // ===============================================================
    // All NULL-extraction rows are populated
    // ===============================================================

    [Fact]
    public async Task RunAsync_PopulatesEveryUnextractedRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeded = await SeedUnextractedAdsAsync(5, ct);
        (await CountNullLexemeRowsAsync(ct)).ShouldBe(seeded, "förutsättning: alla rader NULL.");

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            var counts = await job.RunAsync(RunOptions, ct);

            counts.Seen.ShouldBe(seeded);
            counts.Extracted.ShouldBe(seeded);
            counts.Errors.ShouldBe(0);
        }

        (await CountNullLexemeRowsAsync(ct)).ShouldBe(0,
            "efter backfill ska inga extracted_lexemes IS NULL-rader återstå.");
    }

    [Fact]
    public async Task RunAsync_WritesPopulatedExtractedLexemesPerRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUnextractedAdsAsync(3, ct);

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            await job.RunAsync(RunOptions, ct);
        }

        using var verifyScope = _provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.JobAds.AsNoTracking()
            .Select(j => new
            {
                Terms = j.ExtractedTerms,
                Lexemes = EF.Property<string?>(j, "ExtractedLexemes"),
            })
            .ToListAsync(ct);

        rows.ShouldAllBe(r => r.Terms != null, "varje rad ska ha extracted_terms efter backfill.");
        rows.ShouldAllBe(r => r.Lexemes != null, "varje rad ska ha STORED extracted_lexemes.");
    }

    // ===============================================================
    // Idempotent restart — second run sees 0 NULL rows, extracts 0
    // ===============================================================

    [Fact]
    public async Task RunAsync_SecondRun_IsIdempotent_ExtractsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUnextractedAdsAsync(4, ct);

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            await job.RunAsync(RunOptions, ct);
        }

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            var second = await job.RunAsync(RunOptions, ct);

            // Predicate is extracted_lexemes IS NULL → after the first run nothing
            // qualifies (even '[]' extractions are non-null). Restart-safe.
            second.Seen.ShouldBe(0, "andra körningen ska inte se några NULL-rader.");
            second.Extracted.ShouldBe(0);
        }
    }

    // ===============================================================
    // THE KEY DIVERGENCE — IJobSource is NEVER called (no JobTech re-fetch)
    // ===============================================================

    [Fact]
    public async Task RunAsync_NeverCallsJobSource_LocalReprojectionOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUnextractedAdsAsync(5, ct);

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            await job.RunAsync(RunOptions, ct);
        }

        // The defining contract of this backfill (dotnet-architect decision 5):
        // title + description are already stored, so extraction is a LOCAL
        // re-projection — no JobTech GET, no rate-limit window. The substitute must
        // be entirely untouched.
        _jobSource.ReceivedCalls().ShouldBeEmpty(
            "BackfillJobAdExtractedTermsJob får ALDRIG anropa IJobSource — det är en " +
            "lokal re-projektion, inte en re-fetch (skillnaden mot Klass2/refetch-backfillen).");
    }

    [Fact]
    public async Task RunAsync_RecordsBackfillExtractionAuditEvent()
    {
        // Accountability (GDPR Art. 30): the run writes a JobAdsSynced audit row
        // with JobType "backfill-extraction" (reuses the existing audit concept).
        var ct = TestContext.Current.CancellationToken;
        await SeedUnextractedAdsAsync(2, ct);

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            await job.RunAsync(RunOptions, ct);
        }

        await _auditor.Received(1).RecordAsync(
            Arg.Is<JobAdsSynced>(e => e.JobType == "backfill-extraction"),
            Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Already-extracted rows are not touched (predicate skips non-NULL)
    // ===============================================================

    [Fact]
    public async Task RunAsync_SkipsAlreadyExtractedRows()
    {
        var ct = TestContext.Current.CancellationToken;

        // One already-extracted ad (extracted_terms = '[]', extracted), one unextracted.
        Guid extractedId;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IDateTimeProvider clock = new FixedClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
            var company = Company.Create("Klarna").Value;
            var external = ExternalReference.Create(JobSource.Platsbanken, Guid.NewGuid().ToString("N")).Value;
            var already = JobAd.Import(
                "Redan extraherad", company, "Beskrivning", "https://example.com/jobb/done",
                external, "{\"id\":\"x\"}",
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), clock).Value;
            already.SetExtractedTerms(ExtractedTerms.Empty); // extracted (non-null)
            db.JobAds.Add(already);
            await db.SaveChangesAsync(ct);
            extractedId = already.Id.Value;
        }
        await SeedUnextractedAdsAsync(1, ct);

        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillJobAdExtractedTermsJob>();
            var counts = await job.RunAsync(RunOptions, ct);

            // Only the single unextracted ad is seen/extracted; the '[]' one is skipped.
            counts.Seen.ShouldBe(1, "den redan-extraherade '[]'-raden ska inte tas av predikatet.");
            counts.Extracted.ShouldBe(1);
        }

        // The already-extracted ad still has its empty extraction (untouched).
        using var verifyScope = _provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await verifyDb.JobAds.AsNoTracking()
            .FirstAsync(j => j.Id == new JobAdId(extractedId), ct);
        reloaded.ExtractedTerms.ShouldNotBeNull();
        reloaded.ExtractedTerms!.IsEmpty.ShouldBeTrue();
    }

    // Local fixed clock — self-contained, no cross-namespace test helper.
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
