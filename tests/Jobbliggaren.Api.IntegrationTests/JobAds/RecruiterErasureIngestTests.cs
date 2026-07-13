using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Refit;
using Shouldly;
using Testcontainers.PostgreSql;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// GDPR Art. 17 (#842, ADR 0106 Tier B) — erasure proven END TO END, through the production write
/// path and nothing else.
/// </summary>
/// <remarks>
/// <b>#843, the binding rule this file exists to obey.</b> The old test for this feature
/// hand-seeded <c>raw_payload</c> with an <c>employer.contact_email</c> key straight through
/// <c>JobAd.Import</c> — a key the ingest sanitizer's default-deny allowlist <i>guarantees</i> is
/// stripped in production, and which the wire POCO cannot even emit. It asserted against a state
/// production cannot reach, stayed green for two releases, and that greenness is precisely what
/// hid an Art. 17 path that erased nothing on every request. <b>A test that can construct a state
/// production cannot construct proves nothing about production.</b>
/// <para>
/// So: WireMock serves real JobTech JSON → the REAL <see cref="PlatsbankenJobSource"/> (hence the
/// REAL <see cref="JobTechPayloadSanitizer"/>) → the REAL
/// <see cref="UpsertExternalJobAdCommandHandler"/> (hence the real aggregate and the real keyword
/// extractor) → REAL Postgres 18 with the real migrations, real generated columns and the real GIN
/// index. Not one column is hand-written.
/// </para>
/// <para>
/// <b>And the assertions are OUTCOMES, not columns.</b> "We nulled the fields we thought of" is the
/// claim that failed here before. What is asserted instead: the recruiter's address is not in ANY
/// column of the row (enumerated from <c>information_schema</c>, so a column added tomorrow is
/// covered), the ad is not full-text findable, and a RE-SYNC through the same funnel does not
/// resurrect it.
/// </para>
/// </remarks>
public sealed class RecruiterErasureIngestTests : IAsyncLifetime
{
    // A recruiter, in the shape the real corpus actually holds her (evidence pack §9: "kontakta
    // ansvarig rekryterare Magnus Fagerberg, <mail> 073042903" — a named person, an address and a
    // mobile, in free text). Fictional values; the corpus's are real, which is the point of #842.
    private const string RecruiterName = "Magnus Fagerberg";
    private const string RecruiterEmail = "magnus.fagerberg@rekryteringsbyran.se";
    private const string RecruiterPhone = "0730429030";
    private const string ExternalId = "erasure-e2e-1";

    private static readonly string AdBody =
        "Vi söker en backend-utvecklare till vårt team i Göteborg. "
        + $"För frågor om tjänsten, kontakta ansvarig rekryterare {RecruiterName} "
        + $"på {RecruiterEmail} eller {RecruiterPhone}.";

    // AD 2 — an ENSKILD FIRMA. Her company name IS her name (which is also why
    // organization_number may be a personnummer — JobTechEmployer says so in-file). She appears in
    // NO free text at all: her only occurrence is `employer.name`, i.e. job_ads.company_name, a
    // column the bound spec's Erase() never touched. The two-channel matcher still finds her,
    // because channel 2 scans raw_payload — so without Company.Erased we would match on her name,
    // erase the ad, tell her it was done, and leave the identical string in the row.
    private const string SoleTraderName = "Ingrid Lindqvist";
    private const string SoleTraderExternalId = "erasure-e2e-2";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private WireMockServer _jobTech = default!;
    private ServiceProvider _provider = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _jobTech = WireMockServer.Start();
        _jobTech
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SnapshotJson()));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IRecruiterErasureMatchQuery, RecruiterErasureMatchQuery>();

        services.AddSingleton<IDateTimeProvider>(new FixedClock());
        services.AddSingleton<IOptions<JobTechOptions>>(Options.Create(new JobTechOptions
        {
            JobSearchBaseUrl = _jobTech.Url!,
            JobStreamBaseUrl = _jobTech.Url!,
            ApiKey = string.Empty,
            RawPayloadRetentionDays = 30,
        }));
        services.AddRefitClient<IJobTechSearchClient>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(_jobTech.Url!));
        services.AddHttpClient<IJobTechStreamClient, JobTechStreamClient>(c =>
            c.BaseAddress = new Uri(_jobTech.Url!));
        services.AddScoped<IJobSource, PlatsbankenJobSource>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        _jobTech.Stop();
        _jobTech.Dispose();
        await _postgres.DisposeAsync();
    }

    /// <summary>The JobTech wire shape, v2 (webpage_url top-level).</summary>
    private static string SnapshotJson() =>
        $$"""
        [{
          "id": "{{ExternalId}}",
          "headline": "Backend-utvecklare",
          "description": { "text": "{{AdBody}}" },
          "employer": { "name": "Rekryteringsbyrån AB", "organization_number": "5561234567" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{ExternalId}}",
          "publication_date": "2026-07-01T10:00:00Z"
        },{
          "id": "{{SoleTraderExternalId}}",
          "headline": "Snickare sökes",
          "description": { "text": "Vi behöver en snickare till ett husprojekt i Uppsala." },
          "employer": { "name": "{{SoleTraderName}}", "organization_number": "5509281234" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{SoleTraderExternalId}}",
          "publication_date": "2026-07-02T10:00:00Z"
        }]
        """;

    // ================================================================================
    // The production write path. NOTHING is hand-seeded.
    // ================================================================================
    private async Task IngestThroughProductionPathAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var jobSource = scope.ServiceProvider.GetRequiredService<IJobSource>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stemmer = new SnowballStemmer();
        var analyzer = new LocalTextAnalyzer(stemmer);
        var handler = new UpsertExternalJobAdCommandHandler(
            db,
            new DbExceptionInspector(),
            new JobAdKeywordExtractor(analyzer, stemmer, new SkillTaxonomyIndex(analyzer)),
            new FixedClock(),
            NullLogger<UpsertExternalJobAdCommandHandler>.Instance);

        await foreach (var item in jobSource.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
        {
            await handler.Handle(
                new UpsertExternalJobAdCommand(JobSource.Platsbanken, item.ExternalId, item), ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The OUTCOME assertion, and the one that matters: the value must not survive in ANY column of
    /// the row. Columns are enumerated from <c>information_schema</c> rather than listed here, so a
    /// column someone adds next month is covered by this test without anyone remembering to add it —
    /// which is exactly the discipline whose absence produced #842.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ColumnsStillContainingAsync(
        AppDbContext db, string needle, CancellationToken ct)
    {
        var columns = await db.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'job_ads'
                  AND data_type IN ('text', 'character varying', 'jsonb')
                """)
            .ToListAsync(ct);

        var offenders = new List<string>();
        foreach (var column in columns)
        {
            // The column name comes from the catalog (not from user input) and is quoted; the VALUE
            // is a bound parameter. The ::text cast is what makes this work over jsonb, where ILIKE
            // has no operator — the same cast the production matcher needs.
            var sql = "SELECT count(*)::int AS \"Value\" FROM job_ads "
                + $"WHERE \"{column}\"::text ILIKE {{0}}";

            var hits = await db.Database
                .SqlQueryRaw<int>(sql, $"%{needle}%")
                .ToListAsync(ct);

            if (hits.Count > 0 && hits[0] > 0)
                offenders.Add(column);
        }

        return offenders;
    }

    private static async Task<int> FtsHitsAsync(AppDbContext db, string term, CancellationToken ct)
    {
        var counts = await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM job_ads
                WHERE search_vector @@ websearch_to_tsquery('swedish'::regconfig, {term})
                """)
            .ToListAsync(ct);

        return counts.Count > 0 ? counts[0] : 0;
    }

    // ================================================================================
    // 1. The exposure is REAL — and the harness reaches it.
    // ================================================================================

    /// <summary>
    /// Guards the guard. If this ever goes green-by-accident (because ingest stopped storing the
    /// body, say), every erasure assertion below would pass vacuously — which is the exact shape of
    /// the bug this issue is about. So the exposure is asserted first, positively.
    /// </summary>
    [Fact]
    public async Task Ingest_through_the_production_path_stores_the_recruiter_and_makes_her_FTS_searchable()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldNotBeEmpty("the address must actually be stored, or the erasure tests below "
                + "would pass vacuously — which is #842 itself.");

        (await FtsHitsAsync(db, RecruiterEmail, ct)).ShouldBe(1,
            "any logged-in user can reverse-look-up the recruiter by her address today. That is "
            + "the exposure Tier B closes.");

        (await FtsHitsAsync(db, RecruiterName, ct)).ShouldBe(1,
            "her NAME is independently FTS-searchable — unreachable by regex and by any structured "
            + "field, which is the whole reason whole-record erasure is the only provable remedy.");
    }

    // ================================================================================
    // 2. Erasure — asserted as an OUTCOME, in every column, not as "the fields we nulled".
    // ================================================================================

    [Fact]
    public async Task Erasing_by_email_removes_her_from_every_column_and_from_FTS()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var response = await EraseAsync(RecruiterEmail, ct);

        response.Outcome.ShouldBe(ErasureOutcome.AdsErased);
        response.Erased.JobAds.ShouldBe(1);
        response.ErasedExternalIds.ShouldBe([ExternalId]);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldBeEmpty("her address must not survive in ANY column of job_ads.");
        (await ColumnsStillContainingAsync(db, RecruiterPhone, ct))
            .ShouldBeEmpty("nor her phone number.");
        (await ColumnsStillContainingAsync(db, RecruiterName, ct))
            .ShouldBeEmpty("nor her NAME — the surface no regex can reach, and the reason Tier B "
                + "exists at all.");

        (await FtsHitsAsync(db, RecruiterEmail, ct)).ShouldBe(0);
        (await FtsHitsAsync(db, RecruiterName, ct)).ShouldBe(0);
    }

    /// <summary>
    /// A NAME-only request. The old command could not even accept one (it returned
    /// NameNotSupportedYet — TD-75), while proving, in the same codebase, that the name was
    /// searchable. This is the case the whole tier is for.
    /// </summary>
    [Fact]
    public async Task Erasing_by_NAME_works_and_is_the_case_no_regex_could_ever_serve()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var response = await EraseAsync(RecruiterName, ct);

        response.Outcome.ShouldBe(ErasureOutcome.AdsErased);
        response.Erased.JobAds.ShouldBe(1);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await ColumnsStillContainingAsync(db, RecruiterName, ct)).ShouldBeEmpty();
    }

    /// <summary>
    /// <b>The enskild firma.</b> Her name appears in NO free text — her only occurrence is
    /// <c>employer.name</c>, which lands in <c>job_ads.company_name</c>. The bound spec's
    /// <c>Erase()</c> did not clear that column, and it is a column the FTS channel cannot even see
    /// (<c>search_vector</c> is built from title and description only).
    /// <para>
    /// She is still found, because channel 2 scans <c>raw_payload</c>, which carries
    /// <c>employer.name</c>. So without <see cref="Company.Erased"/> we would match on her name,
    /// erase her ad, send her an Art. 12(3) confirmation — and leave the identical string sitting in
    /// the row. That is the #842 defect class, reproduced inside its own fix, which is why this test
    /// exists rather than a comment saying we thought about it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_enskild_firmas_name_lives_ONLY_in_company_name_and_is_still_erased()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using (var scope = _provider.CreateScope())
        {
            var seeded = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Precondition: she is NOT in the ad body. If a future refactor puts her there, this
            // test would pass for the wrong reason and stop guarding what it was written to guard.
            var ad = await seeded.JobAds.AsNoTracking()
                .SingleAsync(j => j.External!.ExternalId == SoleTraderExternalId, ct);
            ad.Description.ShouldNotContain(SoleTraderName);
            ad.Company.Name.ShouldBe(SoleTraderName);

            (await FtsHitsAsync(seeded, SoleTraderName, ct)).ShouldBe(0,
                "company_name is outside search_vector — so FTS alone would never find her, and "
                + "channel 2 (raw_payload) is what does.");
        }

        var response = await EraseAsync(SoleTraderName, ct);

        response.Outcome.ShouldBe(ErasureOutcome.AdsErased);
        response.ErasedExternalIds.ShouldBe([SoleTraderExternalId]);

        using var check = _provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();

        (await ColumnsStillContainingAsync(db, SoleTraderName, ct))
            .ShouldBeEmpty("her name must not survive in company_name — an enskild firma's company "
                + "name IS a natural person's name.");
    }

    // ================================================================================
    // 3. DURABILITY — the finding that killed every naive fix (F-A).
    // ================================================================================

    /// <summary>
    /// <b>The single most important test in this PR.</b> The nightly snapshot sync and the
    /// 10-minute stream both funnel into <c>UpsertExternalJobAdCommandHandler</c>, which has no
    /// hash short-circuit and reassigns title/description/url/raw_payload UNCONDITIONALLY. Any
    /// erasure that is merely a row UPDATE is therefore undone within ≤10 minutes for any ad still
    /// listed at Arbetsförmedlingen — and we would have sent her an Art. 12(3) confirmation and
    /// then restored her data overnight.
    /// <para>
    /// This runs the REAL funnel again, against the SAME WireMock payload that still carries her
    /// address, exactly as the 02:00 sync would.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_resync_through_the_real_funnel_does_NOT_resurrect_her()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);
        await EraseAsync(RecruiterEmail, ct);

        // Arbetsförmedlingen still serves the ad, contact block and all. This is the nightly sync.
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldBeEmpty("UpdateFromSource must REFUSE on Erased. If this fails, the erasure is "
                + "undone by the 02:00 sync and every Art. 12(3) confirmation we send is false.");
        (await FtsHitsAsync(db, RecruiterEmail, ct)).ShouldBe(0);

        var ad = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == ExternalId, ct);
        ad.Status.ShouldBe(JobAdStatus.Erased, "the tombstone is terminal.");

        // The tombstone holds an external id, a source and a status — and no personal data. That
        // is what lets us refuse a suppression ledger, which would have stored her email in order
        // to keep erasing it.
        ad.Title.ShouldBeEmpty();
        ad.Description.ShouldBeEmpty();
        ad.RawPayload.ShouldBeNull();
        ad.Company.Name.ShouldBe(Company.Erased.Name);
    }

    // ================================================================================
    // 4. Honest answers — the sentences the old mechanism said and could not mean.
    // ================================================================================

    [Fact]
    public async Task An_identifier_we_hold_nothing_for_reports_NoMatchingDataHeld_and_that_is_now_TRUE()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var response = await EraseAsync("ingen.alls@finnsinte.se", ct, dryRun: true);

        response.Outcome.ShouldBe(ErasureOutcome.NoMatchingDataHeld);
        response.Matched.Total.ShouldBe(0);
        response.Erased.Total.ShouldBe(0);
    }

    [Fact]
    public async Task A_dry_run_reports_what_would_go_and_writes_NOTHING()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var response = await EraseAsync(RecruiterEmail, ct, dryRun: true);

        response.Outcome.ShouldBe(ErasureOutcome.DryRun);
        response.Matched.JobAds.ShouldBe(1);
        response.Erased.Total.ShouldBe(0);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldNotBeEmpty("a dry run that destroys something is not a dry run.");
        (await FtsHitsAsync(db, RecruiterEmail, ct)).ShouldBe(1);
    }

    /// <summary>
    /// The mandatory dry run, enforced in code. Confirming a count that no longer matches reality
    /// is refused — ingest runs every ten minutes, so the match set genuinely moves between looking
    /// and confirming, and a destructive operation must not run on a stale view.
    /// </summary>
    [Fact]
    public async Task Confirming_a_stale_count_is_REFUSED_and_destroys_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = NewEraseHandler(scope, db);

        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(
                RequestId: Guid.NewGuid(),
                Identifier: RecruiterEmail,
                DryRun: false,
                ConfirmedJobAdCount: 7), // the operator saw 7; reality says 1
            ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("EraseRecruiterAds.ConfirmationMismatch");

        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldNotBeEmpty("a refused erasure must destroy nothing.");
    }

    // ================================================================================
    // Helpers
    // ================================================================================

    private static EraseRecruiterAdsCommandHandler NewEraseHandler(IServiceScope scope, AppDbContext db) =>
        new(
            db,
            scope.ServiceProvider.GetRequiredService<IRecruiterErasureMatchQuery>(),
            new FixedClock(),
            NullLogger<EraseRecruiterAdsCommandHandler>.Instance);

    private async Task<EraseRecruiterAdsResponse> EraseAsync(
        string identifier, CancellationToken ct, bool dryRun = false)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = NewEraseHandler(scope, db);

        int? confirmed = null;
        if (!dryRun)
        {
            // The API forces this ordering; the test walks the same road rather than around it.
            var probe = await handler.Handle(
                new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, DryRun: true, null), ct);
            confirmed = probe.Value.Matched.JobAds;
        }

        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, dryRun, confirmed), ct);

        result.IsSuccess.ShouldBeTrue(
            result.IsFailure ? result.Error.Code : string.Empty);

        await db.SaveChangesAsync(ct);
        return result.Value;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    }
}
