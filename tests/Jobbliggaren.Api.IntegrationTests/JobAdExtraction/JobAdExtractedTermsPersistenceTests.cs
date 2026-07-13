using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAdExtraction;

/// <summary>
/// Fas 4 STEG 4 (F4-4, dotnet-architect Variant A/3a) — the <c>ExtractedTerms</c>
/// jsonb VO persistence + the STORED generated <c>extracted_lexemes</c> companion
/// + its GIN index, against a real Postgres (Testcontainers — ALDRIG EF-InMemory:
/// the generated column + jsonb <c>?|</c> overlap only exist on the real engine).
/// Self-contained fixture (own container) mirroring
/// <c>OccupationCodeDeriverIntegrationTests</c>.
///
/// Pins the F4-6-facing contract:
/// <list type="bullet">
/// <item>VO jsonb round-trips with structural equality;</item>
/// <item><c>extracted_lexemes</c> is populated from <c>extracted_terms</c> by
/// Postgres (<c>jsonb_path_query_array($[*].Lexeme)</c>) and a raw
/// <c>extracted_lexemes ?| array[…]</c> query returns the ad for a known lexeme
/// (the overlap shape the matching engine uses);</item>
/// <item>NULL <c>extracted_terms</c> ⟺ NULL <c>extracted_lexemes</c> (the backfill
/// idempotency predicate);</item>
/// <item>extracted-to-empty (<c>'[]'</c>) ⇒ <c>extracted_lexemes</c> is a non-null
/// EMPTY jsonb array (extracted, distinct from never-extracted NULL).</item>
/// </list>
///
/// RED until the Domain VO + the F4-4 EF mapping/migration ship.
/// </summary>
public sealed class JobAdExtractedTermsPersistenceTests : IAsyncLifetime
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
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // pg_trgm is required by the trigram-index migration (parity ApiFactory).
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewDb() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    // ---------------------------------------------------------------
    // Seed helpers.
    // ---------------------------------------------------------------

    private static JobAd NewJobAd(string title = "Backend-utvecklare", string description = "Beskrivning")
    {
        var company = Company.Create("Klarna").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, Guid.NewGuid().ToString("N")).Value;
        IDateTimeProvider clock = new FixedClock(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        return JobAd.Import(
            title, company, description, "https://example.com/jobb/1",
            external, "{\"id\":\"x\"}", TestFacets.FromPayload("{\"id\":\"x\"}"),
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), clock).Value;
    }

    private static ExtractedTerms SampleTerms() =>
        ExtractedTerms.From(
        [
            new ExtractedTerm(
                Lexeme: "1TC7_x8s_V7V", Display: "JavaScript",
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Title,
                MatchedOn: "JavaScript", ConceptId: "1TC7_x8s_V7V", Weight: 3),
            new ExtractedTerm(
                Lexeme: "samordn", Display: "samordnare",
                Kind: ExtractedTermKind.Keyword, Source: ExtractedTermSource.Description,
                MatchedOn: "samordnare", ConceptId: null, Weight: 2),
        ]);

    // ===============================================================
    // Round-trip — VO jsonb survives save + reload with structural equality
    // ===============================================================

    [Fact]
    public async Task ExtractedTerms_RoundTrip_PreservesStructuralEquality()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = SampleTerms();

        Guid id;
        await using (var db = NewDb())
        {
            var jobAd = NewJobAd();
            jobAd.SetExtractedTerms(terms);
            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(ct);
            id = jobAd.Id.Value;
        }

        await using (var db = NewDb())
        {
            var reloaded = await db.JobAds.AsNoTracking()
                .FirstAsync(j => j.Id == new JobAdId(id), ct);

            reloaded.ExtractedTerms.ShouldNotBeNull();
            reloaded.ExtractedTerms!.ShouldBe(terms,
                "jsonb-VO:n ska round-trippa med strukturell likhet (From-normalisering på läs).");
            // Field-level spot checks (evidence + concept-id survive the jsonb hop).
            var skill = reloaded.ExtractedTerms.Terms.First(t => t.Kind == ExtractedTermKind.Skill);
            skill.ConceptId.ShouldBe("1TC7_x8s_V7V");
            skill.MatchedOn.ShouldBe("JavaScript");
        }
    }

    // ===============================================================
    // STORED extracted_lexemes — populated from extracted_terms by Postgres
    // ===============================================================

    [Fact]
    public async Task ExtractedLexemes_StoredColumn_IsPopulatedFromExtractedTerms()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid id;
        await using (var db = NewDb())
        {
            var jobAd = NewJobAd();
            jobAd.SetExtractedTerms(SampleTerms());
            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(ct);
            id = jobAd.Id.Value;
        }

        await using (var db = NewDb())
        {
            // Read the STORED generated shadow column via EF.Property.
            var lexemesJson = await db.JobAds.AsNoTracking()
                .Where(j => j.Id == new JobAdId(id))
                .Select(j => EF.Property<string?>(j, "ExtractedLexemes"))
                .FirstAsync(ct);

            lexemesJson.ShouldNotBeNull(
                "extracted_lexemes ska populeras (STORED generated jsonb_path_query_array).");
            // The jsonb array projects the .Lexeme of every term.
            lexemesJson!.ShouldContain("1TC7_x8s_V7V");
            lexemesJson.ShouldContain("samordn");
        }
    }

    [Fact]
    public async Task ExtractedLexemes_AnyOfOverlapQuery_ReturnsAdForKnownLexeme()
    {
        // The F4-6 overlap shape: extracted_lexemes ?| array[…cv lexemes…]. A raw
        // parameterized query for a KNOWN lexeme must return the seeded ad.
        var ct = TestContext.Current.CancellationToken;

        Guid id;
        await using (var db = NewDb())
        {
            var jobAd = NewJobAd();
            jobAd.SetExtractedTerms(SampleTerms());
            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(ct);
            id = jobAd.Id.Value;
        }

        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync(ct);

        // Matching lexeme set → ad returned.
        (await OverlapMatchExistsAsync(conn, id, ["1TC7_x8s_V7V", "ekonomi"], ct))
            .ShouldBeTrue("extracted_lexemes ?| array[matchande lexem] ska returnera annonsen.");
        // Non-matching lexeme set → ad NOT returned (the operator actually filters).
        (await OverlapMatchExistsAsync(conn, id, ["ingen", "matchning", "alls"], ct))
            .ShouldBeFalse("icke-överlappande lexem-set ska inte returnera annonsen.");
    }

    // ===============================================================
    // NULL extracted_terms ⟺ NULL extracted_lexemes (backfill predicate)
    // ===============================================================

    [Fact]
    public async Task NullExtractedTerms_ImpliesNullExtractedLexemes()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid id;
        await using (var db = NewDb())
        {
            var jobAd = NewJobAd(); // ExtractedTerms left NULL — never extracted
            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(ct);
            id = jobAd.Id.Value;
        }

        await using (var db = NewDb())
        {
            var row = await db.JobAds.AsNoTracking()
                .Where(j => j.Id == new JobAdId(id))
                .Select(j => new
                {
                    Terms = j.ExtractedTerms,
                    Lexemes = EF.Property<string?>(j, "ExtractedLexemes"),
                })
                .FirstAsync(ct);

            row.Terms.ShouldBeNull("aldrig extraherad ⇒ extracted_terms NULL.");
            row.Lexemes.ShouldBeNull(
                "extracted_lexemes IS NULL ⟺ extracted_terms IS NULL (backfill-idempotens-predikatet).");
        }
    }

    // ===============================================================
    // extracted-to-empty ('[]') ⇒ extracted_lexemes non-null EMPTY array
    // ===============================================================

    [Fact]
    public async Task EmptyExtraction_YieldsNonNullEmptyExtractedLexemes()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid id;
        await using (var db = NewDb())
        {
            var jobAd = NewJobAd();
            jobAd.SetExtractedTerms(ExtractedTerms.Empty); // extracted, nothing matched
            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(ct);
            id = jobAd.Id.Value;
        }

        await using (var db = NewDb())
        {
            var lexemesJson = await db.JobAds.AsNoTracking()
                .Where(j => j.Id == new JobAdId(id))
                .Select(j => EF.Property<string?>(j, "ExtractedLexemes"))
                .FirstAsync(ct);

            // Extracted (not NULL) but empty: an empty jsonb array, NOT null. This is
            // what makes the backfill predicate (lexemes IS NULL) skip this row.
            lexemesJson.ShouldNotBeNull(
                "extraherad-till-tom ⇒ extracted_lexemes är icke-null (extracted_terms = '[]').");
            lexemesJson!.Replace(" ", string.Empty).ShouldBe("[]",
                "jsonb_path_query_array över en tom term-array ger en tom jsonb-array.");
        }
    }

    // ---------------------------------------------------------------
    // Raw overlap helper — the F4-6 matching-engine overlap. Uses the FUNCTION
    // form jsonb_exists_any(target, text[]) which is EXACTLY equivalent to the
    // `?|` operator the GIN index serves, but avoids Npgsql's `?`→positional-
    // parameter escaping pitfall (the bare `?|` operator would require `??|`).
    // Parameterized; never string concatenation.
    // ---------------------------------------------------------------
    private static async Task<bool> OverlapMatchExistsAsync(
        NpgsqlConnection conn, Guid id, string[] lexemes, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT EXISTS (SELECT 1 FROM job_ads " +
            "WHERE id = @id AND jsonb_exists_any(extracted_lexemes, @lexemes));";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("lexemes", lexemes);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    // Local fixed clock — keeps this suite self-contained (no cross-namespace
    // test-helper dependency).
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
