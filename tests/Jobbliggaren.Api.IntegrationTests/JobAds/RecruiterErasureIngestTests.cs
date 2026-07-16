using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedSearches;
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
using DomainApplication = Jobbliggaren.Domain.Applications.Application;
// `ApplicationId` alone is ambiguous with System.ApplicationId, and `Application` with the
// Jobbliggaren.Application namespace — the same two aliases AppDbContext itself carries.
using DomainApplicationId = Jobbliggaren.Domain.Applications.ApplicationId;

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

    // AD 3 — the FTS case. The body writes her SURNAME FIRST ("Fagerberg, Magnus"), which is how a
    // great deal of Swedish ad copy names a contact. She asks us to erase "Magnus Fagerberg". The
    // substring channel compares strings and misses; Postgres's FTS lexemes both forms and hits.
    // Without this ad, the entire FTS clause could be deleted and every other test stayed green.
    private const string ReversedNameQuery = "Magnus Fagerberg";
    private const string ReversedNameExternalId = "erasure-e2e-3";

    // AD 4 — the TITLE-only carrier (round 6, per-channel-column pins). Her name is in the
    // HEADLINE and in no other free-text field, so the row is reachable only through the title
    // (and its search_vector derivation — the STORED vector is built FROM the title, so the two
    // cannot be separated by any seed; the pin is column-level reachability, not arm isolation).
    private const string TitleOnlyName = "Sylvia Nordgren";
    private const string TitleOnlyExternalId = "erasure-e2e-4";

    // AD 5 — the RAW_PAYLOAD-only carrier. The identifier lives in workplace_address.municipality
    // — a free-text NAME field JobTech controls, allowlisted by the sanitizer, and projected ONLY
    // as municipality_concept_id (a code). So the string survives in raw_payload alone: not in
    // title, not in description, not in company_name, and search_vector never sees it. Delete the
    // raw_payload arm and exactly this test goes red — the arm had no single-column pin before.
    private const string RawPayloadOnlyToken = "Vikströmshamn";
    private const string RawPayloadOnlyExternalId = "erasure-e2e-5";

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
          "id": "{{ReversedNameExternalId}}",
          "headline": "Projektledare",
          "description": { "text": "Kontaktperson för tjänsten: Fagerberg, Magnus. Ansök via länken." },
          "employer": { "name": "Nordiska Bygg AB", "organization_number": "5567654321" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{ReversedNameExternalId}}",
          "publication_date": "2026-07-03T10:00:00Z"
        },{
          "id": "{{SoleTraderExternalId}}",
          "headline": "Snickare sökes",
          "description": { "text": "Vi behöver en snickare till ett husprojekt i Uppsala." },
          "employer": { "name": "{{SoleTraderName}}", "organization_number": "5509281234" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{SoleTraderExternalId}}",
          "publication_date": "2026-07-02T10:00:00Z"
        },{
          "id": "{{TitleOnlyExternalId}}",
          "headline": "Rekryteringsansvarig {{TitleOnlyName}}",
          "description": { "text": "Vi söker en kock till vår restaurang i Luleå." },
          "employer": { "name": "Storköket AB", "organization_number": "5566778899" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{TitleOnlyExternalId}}",
          "publication_date": "2026-07-04T10:00:00Z"
        },{
          "id": "{{RawPayloadOnlyExternalId}}",
          "headline": "Diskare",
          "description": { "text": "Diskplockning och enklare beredning i storkök." },
          "employer": { "name": "Kommunala Köket AB", "organization_number": "5560001111" },
          "workplace_address": { "municipality": "{{RawPayloadOnlyToken}}" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{RawPayloadOnlyExternalId}}",
          "publication_date": "2026-07-05T10:00:00Z"
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

        // Tier A (2026-07-16) inverted this guard's email half: the address IS still stored —
        // that is what keeps the erasure tests below non-vacuous — but ONLY in the structured
        // contacts column (the ingest scrub moved it out of every free-text carrier), and it is
        // no longer FTS-reachable. The set equality is the migration claim itself: an address in
        // ANY other column means the scrub regressed; an empty set means the erasure tests go
        // vacuous (#842 itself).
        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldBe(["contacts"],
                "post-Tier-A the address lives in the structured contacts column ALONE.");

        (await FtsHitsAsync(db, RecruiterEmail, ct)).ShouldBe(0,
            "the ingest scrub removes the address from search_vector (FTS lock L3) — the "
            + "reverse-lookup exposure Tier A closes for detected identifiers.");

        // TWO ads name her: the one that spells her "Magnus Fagerberg", and the one that writes
        // "Fagerberg, Magnus". FTS lexemes both to the same terms — which is the entire reason the
        // FTS channel is load-bearing, and it is why this count is 2 and not 1.
        (await FtsHitsAsync(db, RecruiterName, ct)).ShouldBe(2,
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

        // And here is a fact the operator MUST know, so it is a test and not a footnote: erasing by
        // her EMAIL does not erase the ad that only names her. Ad 3 ("Fagerberg, Magnus") carries no
        // address, so it does not match the email — and she is still FTS-searchable through it.
        //
        // This is correct behaviour, not a gap: the identifier is what it is. It is also exactly why
        // the runbook tells the operator to run the dry run once per identifier he holds — her
        // address, her number, AND her name — and why a completed-erasure reply sent after matching
        // only her email would be a false statement.
        (await FtsHitsAsync(db, RecruiterName, ct)).ShouldBe(1,
            "the ad that names her without her address survives an email-only erasure. Run the name "
            + "too — the runbook says so, and this is why.");
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
        response.Erased.JobAds.ShouldBe(2, "both the plain and the surname-first ad name her.");

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

    /// <summary>
    /// <b>The FTS channel, proven to be load-bearing.</b> It was not, before this test: delete the
    /// entire <c>search_vector @@ …</c> clause from the matcher and every other test in this file
    /// stayed green. A channel nobody can observe failing is a channel nobody knows works — the
    /// vacuous-control pattern, one level up, inside the fix for the vacuous control.
    /// <para>
    /// The case that separates them: the ad names her <b>"Fagerberg, Magnus"</b> (surname first — how
    /// half of Swedish ad copy writes a contact), and she asks us to erase <b>"Magnus Fagerberg"</b>.
    /// The substring channel compares strings and MISSES. Postgres's FTS lexemes both forms and HITS.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_FTS_channel_finds_her_when_the_substring_channel_CANNOT()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Precondition: the reversed form is NOT a substring of the ad body. If a future edit
            // makes it one, this test would pass for the wrong reason and stop guarding FTS.
            var ad = await db.JobAds.AsNoTracking()
                .SingleAsync(j => j.External!.ExternalId == ReversedNameExternalId, ct);

            ad.Description.Contains(ReversedNameQuery, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse("if the body literally contains the query, the substring channel "
                    + "would find her and this test would no longer prove anything about FTS.");
        }

        var response = await EraseAsync(ReversedNameQuery, ct);

        response.Outcome.ShouldBe(ErasureOutcome.AdsErased);
        response.ErasedExternalIds.ShouldContain(ReversedNameExternalId,
            "FTS lexemes 'Fagerberg, Magnus' and 'Magnus Fagerberg' to the same terms. Remove the "
            + "FTS clause and this is the test that goes red.");
    }

    /// <summary>
    /// <b>The enskild firma, AFTER <c>raw_payload</c> is gone — which is the state MOST of the corpus
    /// is in.</b>
    /// <para>
    /// <c>PurgeStaleRawPayloadsJob</c> NULLs <c>raw_payload</c> 30 days after publication. The
    /// original matcher reached <c>employer.name</c> ONLY through <c>raw_payload</c>, and
    /// <c>company_name</c> is not in <c>search_vector</c> (which is built from title and description
    /// only). So for every ad older than 30 days — i.e. most of 93 469 ads collected over months —
    /// she would have been answered <i>"we hold no data matching this identifier"</i> while her name
    /// sat in plaintext in a column we scan on every erasure.
    /// </para>
    /// <para>
    /// The earlier enskild-firma test passed only because its <c>raw_payload</c> was fresh. Its own
    /// doc said so out loud — <i>"she is still found, because channel 2 scans raw_payload"</i> — and
    /// that quiet holding precondition is the exact shape of the defect this whole issue is about.
    /// This test removes it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task She_is_STILL_found_after_raw_payload_has_been_purged()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using (var purge = _provider.CreateScope())
        {
            var db = purge.ServiceProvider.GetRequiredService<AppDbContext>();

            // Exactly what PurgeStaleRawPayloadsJob does at 30 days.
            await db.Database.ExecuteSqlRawAsync("UPDATE job_ads SET raw_payload = NULL;", ct);

            var ad = await db.JobAds.AsNoTracking()
                .SingleAsync(j => j.External!.ExternalId == SoleTraderExternalId, ct);

            ad.RawPayload.ShouldBeNull();
            ad.Company.Name.ShouldBe(SoleTraderName, "her name is now ONLY in company_name.");

            (await FtsHitsAsync(db, SoleTraderName, ct)).ShouldBe(0,
                "and company_name is outside search_vector, so FTS cannot reach her either.");
        }

        var response = await EraseAsync(SoleTraderName, ct);

        response.Outcome.ShouldBe(ErasureOutcome.AdsErased,
            "without a company_name channel this is NoMatchInSearchableSurfaces — a false 'we hold nothing "
            + "about you' sent to a named person, for most of the corpus.");
        response.ErasedExternalIds.ShouldContain(SoleTraderExternalId);

        using var check = _provider.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await ColumnsStillContainingAsync(after, SoleTraderName, ct)).ShouldBeEmpty();
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
    // 3b. THE CASCADE — observed running against a POPULATED table, not an empty one.
    // ================================================================================

    /// <summary>
    /// The <c>recent_job_searches</c> cascade, actually observed. It had zero tests: the code ran only
    /// against an empty table, and <b>code that runs against nothing, that nobody has seen work, is
    /// how a vacuous control is born the second time.</b>
    /// <para>
    /// The row is HARD-DELETED, not nulled: <c>q</c> is a derivative of <c>FilterHash</c>, which is the
    /// row's identity, and the aggregate binds that they must never diverge — a nulled <c>q</c>
    /// corrupts the row rather than cleaning it. The user loses nothing; her cap-20 list rebuilds on
    /// her next search.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_recent_search_containing_her_name_is_HARD_DELETED_and_a_saved_search_is_REPORTED()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var seekerId = await SeedSeekerWithSearchesNamingHerAsync(ct);

        // The dry run must SEE both surfaces before anything is destroyed.
        var probe = await EraseAsync(RecruiterName, ct, dryRun: true);
        probe.Matched.RecentJobSearches.ShouldBe(1);
        probe.Matched.SavedSearches.ShouldBe(1);
        probe.Erased.Total.ShouldBe(0);

        var response = await EraseAsync(RecruiterName, ct);

        response.Erased.RecentJobSearches.ShouldBe(1, "the auto-capture row is hard-deleted.");
        response.Matched.SavedSearches.ShouldBe(1);
        response.Erased.SavedSearches.ShouldBe(0,
            "a saved search is the USER's artefact. Her right DOES reach it (Art. 6(1)(f) → Art. "
            + "21(1)), so this is a MECHANISM choice — a human erases it, with that user in the loop "
            + "— and never a refusal. The gap between Matched and Erased is what forces the reply to "
            + "say so.");

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.RecentJobSearches.CountAsync(r => r.JobSeekerId == seekerId, ct))
            .ShouldBe(0, "hard-deleted, not hidden.");

        (await db.SavedSearches.IgnoreQueryFilters().CountAsync(s => s.JobSeekerId == seekerId, ct))
            .ShouldBe(1, "the saved search survives — reported, disclosed, and handed to a human.");
    }

    // ================================================================================
    // 3c. The word-boundary matcher over `recent_job_searches` — the ONE surface matched by
    //     regex rather than by LIKE, and therefore the one where a near-miss is invisible.
    //
    //     `RecruiterErasureMatchQuery.WordBoundaryPattern` builds
    //         (?<![[:alnum:]_])<ARE-escaped identifier>(?![[:alnum:]_])
    //     and matches it with `~*`. Three separate claims live in that one line, and each is
    //     pinned below by an input that goes RED when the claim is broken. Mutation-verified;
    //     see each test's remarks.
    // ================================================================================

    /// <summary>
    /// <b>Claim 1 — the lookarounds do their boundary duty.</b> A recruiter named <i>Anna</i> must
    /// match a search for <c>"anna karlsson"</c> and must NOT match <c>"marianna"</c> or
    /// <c>"johanna"</c>.
    /// </summary>
    /// <remarks>
    /// This is the surface where over-matching is DESTRUCTIVE without ceremony: recent searches are
    /// hard-deleted on confirmation with no per-id review (the ads get an id-by-id confirmation
    /// gate; these rows do not). A bare substring match on "anna" would silently delete every
    /// user's cached search for Johanna, Marianna, Hannah and Annabelle.
    /// <para>
    /// <b>Mutation-verified 2026-07-14:</b> drop the lookarounds from
    /// <c>WordBoundaryPattern</c> (leaving the escaped identifier alone) and this test goes RED on
    /// the marianna/johanna rows. The sibling test below stays GREEN — the two claims are pinned
    /// independently, on purpose.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_recent_search_matches_her_name_as_a_WHOLE_WORD_and_not_inside_marianna()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        // Seeded through RecentJobSearch.Capture — the aggregate's own factory, which derives
        // FilterHash from the criteria. No hand-written columns (#843).
        var seekerId = await SeedRecentSearchesAsync(ct, "anna karlsson", "marianna", "johanna");

        var response = await EraseAsync("anna", ct, dryRun: true);

        // The dry run reports the STRINGS, not a count. A count cannot be reviewed — and these rows
        // are the ones destroyed WITHOUT an id-by-id confirmation, so seeing them is the only
        // review the operator gets.
        response.MatchedRecentSearchTerms.ShouldBe(["anna karlsson"],
            "the identifier must match as a WHOLE WORD. 'marianna' and 'johanna' contain the "
            + "letters a-n-n-a and are NOT her. Without the lookarounds we would hard-delete both, "
            + "and this surface has no confirmation ceremony to catch it.");

        response.Matched.RecentJobSearches.ShouldBe(1);

        // And the terms carry no user ids — the operator identifies nobody in order to review them.
        response.MatchedRecentSearchTerms.ShouldNotContain(
            t => t.Contains(seekerId.Value.ToString(), StringComparison.OrdinalIgnoreCase),
            "ErasureRecentSearchMatch surfaces `q` only. A user id here would leak a THIRD party "
            + "(the job seeker) into the reply for a recruiter's request.");
    }

    /// <summary>
    /// <b>Claim 2 — the identifier is ARE-ESCAPED, and Claim 3 — an identifier that STARTS with a
    /// non-word character still matches.</b> Both are pinned here because both are properties of
    /// the pattern's construction rather than of its boundaries.
    /// </summary>
    /// <remarks>
    /// <b>Claim 2 (escaping).</b> <c>magnus@skill.se</c> must match a row containing it verbatim and
    /// must NOT match <c>magnus@skillXse</c>. That second row is the whole test: an unescaped
    /// <c>.</c> is ARE's any-character wildcard, so <c>magnus@skill.se</c> would match
    /// <c>magnus@skillXse</c> — a DIFFERENT person's search term, hard-deleted. Note that a row like
    /// <c>magnusXskill.se</c> proves nothing at all here (the <c>@</c> is literal either way, so it
    /// fails to match with or without the escape); the discriminating row must differ from the
    /// identifier at the <c>.</c> and NOWHERE else.
    /// <para>
    /// <b>Claim 3 (the vacuity trap, and it is the reason this whole file exists).</b> The bound
    /// remedy named Postgres's <c>\m…\M</c>. <c>\m</c> matches only at a position immediately BEFORE
    /// a word character — so for a phone number, <c>\m\+46701234567\M</c> puts <c>\m</c> in front of
    /// <c>+</c>, where it can NEVER match. The regex returns zero rows. Silently. On every request.
    /// Forever. And we would have told a named person we hold nothing of hers while her number sat
    /// in plaintext in a column we claim to scan. <b>That is #842's exact defect class, and the
    /// prescribed fix for it would have reintroduced it.</b> The lookaround form closes it, and this
    /// assertion is the only thing that knows.
    /// </para>
    /// <para>
    /// <b>Mutation-verified 2026-07-14.</b> (a) <c>WordBoundaryPattern</c> → the <c>\m…\M</c> form:
    /// the <c>+46701234567</c> row goes RED. (b) <c>EscapeAre</c> → identity (no escaping): the
    /// <c>magnus@skillXse</c> row goes RED. The whole-word sibling above stays GREEN under both.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_pattern_ESCAPES_the_identifier_and_still_matches_one_that_starts_with_a_non_word_character()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        await SeedRecentSearchesAsync(
            ct,
            "magnus@skill.se",
            // Differs from the identifier at the '.' and NOWHERE else. An unescaped '.' is ARE's
            // any-character wildcard and matches the 'X'. This row is the escape's only witness.
            "magnus@skillXse",
            "rekryterare +46701234567");

        var byEmail = await EraseAsync("magnus@skill.se", ct, dryRun: true);

        byEmail.MatchedRecentSearchTerms.ShouldBe(["magnus@skill.se"],
            "the '.' must be ESCAPED, not treated as any-char. If 'magnus@skillXse' is in this "
            + "list, the matcher just hard-deleted a different person's search term.");

        var byPhone = await EraseAsync("+46701234567", ct, dryRun: true);

        byPhone.MatchedRecentSearchTerms.ShouldBe(["rekryterare +46701234567"],
            "an identifier that STARTS with a non-word character must still match. With \\m…\\M "
            + "this list is EMPTY — silently, on every request — and she is told we hold nothing "
            + "of hers. That is the vacuous matcher #842 is about, rebuilt by its own remedy.");

        byPhone.Matched.RecentJobSearches.ShouldBe(1);
    }

    // ================================================================================
    // 3d. The channels that had never returned a row.
    //
    //     A channel that has never produced a non-zero count in a test has not been TESTED — it
    //     has been TYPED. Every count below runs against a POPULATED table, because a query that
    //     has only ever been run against an empty one is indistinguishable from a query that
    //     returns nothing. That indistinguishability IS #842.
    // ================================================================================

    /// <summary>
    /// <b>The structural channel — the one that needs no text matching at all.</b>
    /// <c>applications.job_ad_id</c> names every application written TO a matched ad, exactly, with
    /// no regex, no LIKE and no decryption. It is what the reply offers her INSTEAD of scanning the
    /// three DEK-encrypted columns we refuse to build a read-everyone capability for.
    /// </summary>
    /// <remarks>
    /// It is a DISCLOSURE, never a deletion list: the applications belong to job seekers, not to the
    /// recruiter, and erasing them would destroy a third party's data to serve her request. So the
    /// count is reported and the rows are left standing — <c>Matched &gt; 0</c> while
    /// <c>Erased == 0</c>, which is the gap the reply template is forced to say out loud.
    /// </remarks>
    [Fact]
    public async Task An_application_REFERENCING_a_matched_ad_is_counted_and_NEVER_erased()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var applicationId = await SeedApplicationForAdAsync(ExternalId, ct);

        var probe = await EraseAsync(RecruiterEmail, ct, dryRun: true);

        probe.Matched.ApplicationsReferencingMatchedAds.ShouldBe(1,
            "the structural channel must actually RETURN a row. Until this assertion existed the "
            + "query had only ever run against an empty applications table — which is not a test, "
            + "it is a type-check with a database attached.");

        // Tier A (2026-07-16): the snapshot froze the SCRUBBED body, so her ADDRESS is not in
        // snapshot_description any more — it is in the frozen snapshot_contacts (its own surface,
        // erased surgically). Her NAME is still in the frozen body — no scrub reaches a name —
        // and THAT is what the 17(3)(e) retention claim is asserted over.
        probe.Matched.ApplicationSnapshotContacts.ShouldBe(1,
            "the frozen contact block carries her address post-Tier-A; it is matched on its own "
            + "surface and erased surgically, never with the applicant's record.");

        var nameProbe = await EraseAsync(RecruiterName, ct, dryRun: true);
        nameProbe.Matched.ApplicationSnapshots.ShouldBe(1,
            "snapshot_description is a frozen copy of the ad body, so it holds her NAME. We "
            + "search it precisely BECAUSE we do not erase it (Art. 17(3)(e)).");

        var response = await EraseAsync(RecruiterEmail, ct);

        response.Erased.JobAds.ShouldBe(1);
        response.Matched.ApplicationsReferencingMatchedAds.ShouldBe(1);
        response.Erased.ApplicationsReferencingMatchedAds.ShouldBe(0,
            "report-only. The application belongs to a JOB SEEKER; erasing it to serve the "
            + "recruiter's request would destroy a third party's data.");
        response.Erased.ApplicationSnapshots.ShouldBe(0, "Art. 17(3)(e).");
        response.Erased.ApplicationSnapshotContacts.ShouldBe(1,
            "the surgical arm removes the recruiter's frozen contact block while the applicant's "
            + "record stands (b1 (4.4), T2 CTO 2026-07-16).");

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Applications.IgnoreQueryFilters().CountAsync(a => a.Id == applicationId, ct))
            .ShouldBe(1, "the application row must still be there. The ad is a tombstone; the "
                + "application that points at it is untouched.");

        // The DB row, not only the counter: a counter can report an arm that was deleted
        // outright (mutation M8 survived on exactly that before the verdict-counting fix).
        var frozen = await db.Applications.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => new { Contacts = a.AdSnapshot!.Contacts, a.AdSnapshot!.Description })
            .SingleAsync(ct);
        frozen.Contacts.ShouldBeNull(
            "the surgical arm must have CLEARED the frozen contact block in the database.");
        frozen.Description.ShouldNotBeNull(
            "and the applicant's own record must stand — surgical, never whole-record.");
    }

    /// <summary>
    /// <b><c>applications.manual_url</c> — newly searched, and it must actually return a row.</b> A
    /// user who tracks an application manually pastes the link they applied through, and that link
    /// routinely carries the recruiter's name (<c>linkedin.com/in/magnus-fagerberg</c>). The column
    /// was classified as holding recruiter free text and was not scanned.
    /// </summary>
    /// <remarks>
    /// Matched, reported, and NOT erased: the manual entry is the USER's own artefact and a human
    /// settles it with that user in the loop. A mechanism choice, never a refusal of her right.
    /// </remarks>
    [Fact]
    public async Task A_manual_entrys_URL_naming_her_is_MATCHED_and_left_for_a_human()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var applicationId = await SeedManualApplicationAsync(
            "https://linkedin.com/in/magnus-fagerberg", ct);

        var probe = await EraseAsync("magnus-fagerberg", ct, dryRun: true);

        probe.Matched.ManualAdEntries.ShouldBe(1,
            "manual_url is scanned. If this is 0 the column is in the registry, named in the "
            + "reply, and never actually looked at.");

        var response = await EraseAsync("magnus-fagerberg", ct);

        response.Matched.ManualAdEntries.ShouldBe(1);
        response.Erased.ManualAdEntries.ShouldBe(0,
            "the user's own artefact — a human erases it, with that user in the loop.");

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var manual = await db.Applications.IgnoreQueryFilters()
            .Where(a => a.Id == applicationId)
            .Select(a => a.ManualPosting!.Url)
            .SingleAsync(ct);

        manual.ShouldBe("https://linkedin.com/in/magnus-fagerberg",
            "reported, not destroyed. The gap between Matched and Erased is the disclosure.");
    }

    /// <summary>
    /// <b><c>company_watch_criteria.label</c> — newly searched, and it must actually return a
    /// row.</b> A user who labels a watch <i>"Bevakning Magnus Fagerberg"</i> is holding the
    /// recruiter's name in a column that shipped in #560 (PR-1) and that no erasure path had ever
    /// looked at.
    /// </summary>
    /// <remarks>
    /// Same disposition as the manual entry and the saved search: matched, disclosed, and left
    /// standing. A human nulls the label with the affected USER in the loop — we do not silently
    /// rewrite one person's saved work to serve another person's request.
    /// </remarks>
    [Fact]
    public async Task A_company_watch_criterions_LABEL_naming_her_is_MATCHED_and_SURVIVES_the_erasure()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var criterionId = await SeedCompanyWatchCriterionAsync($"Bevakning {RecruiterName}", ct);

        var probe = await EraseAsync(RecruiterName, ct, dryRun: true);

        probe.Matched.CompanyWatchCriteria.ShouldBe(1,
            "company_watch_criteria.label is scanned. If this is 0 the column is classified, "
            + "reported in the reply, and never looked at — a certified-clean surface nobody "
            + "checked, which is the #842 shape exactly.");

        // The destructive run erases the ADS. The criterion must be untouched by it.
        var response = await EraseAsync(RecruiterName, ct);

        response.Erased.JobAds.ShouldBe(2);
        response.Matched.CompanyWatchCriteria.ShouldBe(1);
        response.Erased.CompanyWatchCriteria.ShouldBe(0);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var label = await db.CompanyWatchCriteria.IgnoreQueryFilters()
            .Where(c => c.Id == criterionId)
            .Select(c => c.Label)
            .SingleAsync(ct);

        label.ShouldBe($"Bevakning {RecruiterName}",
            "the criterion — and its label — survive. Report-only: a human nulls it.");
    }

    // ================================================================================
    // 4. Honest answers — the sentences the old mechanism said and could not mean.
    // ================================================================================

    [Fact]
    public async Task An_identifier_we_hold_nothing_for_reports_NoMatchInSearchableSurfaces_and_STILL_discloses_what_we_could_not_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var response = await EraseAsync("ingen.alls@finnsinte.se", ct, dryRun: true);

        response.Outcome.ShouldBe(ErasureOutcome.NoMatchInSearchableSurfaces);
        response.Matched.Total.ShouldBe(0);
        response.Erased.Total.ShouldBe(0);

        // THE POINT. The old word was NoMatchingDataHeld — "we hold no data matching this
        // identifier" — and we could never truthfully mean it, because the DEK-encrypted columns
        // were never searched. The word now says only what we can prove, and the reply CANNOT be
        // sent without naming the surfaces we could not look at, plus a route she can take.
        response.CouldNotSearch.Columns.ShouldContain("applications.cover_letter");
        response.CouldNotSearch.Columns.ShouldContain("application_notes.content");
        response.CouldNotSearch.Columns.ShouldContain("follow_ups.note");
        response.CouldNotSearch.Escalation.ShouldNotBeNullOrWhiteSpace();
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
        // Tier A (2026-07-16): the post-ingest steady state is contacts-only + zero FTS hits
        // (the scrub already moved the address). A dry run must leave BOTH exactly as they were —
        // the name's two FTS hits are the non-vacuous "nothing was destroyed" signal now.
        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldBe(["contacts"], "a dry run that destroys something is not a dry run.");
        (await FtsHitsAsync(db, RecruiterEmail, ct)).ShouldBe(0);
        (await FtsHitsAsync(db, RecruiterName, ct)).ShouldBe(2,
            "her name is untouched by a dry run — and by the scrub (Tier B's population).");
    }

    /// <summary>
    /// The confirmation gate EXISTS: an id the operator confirmed that is not in the current match
    /// set is refused, and nothing is destroyed.
    /// </summary>
    /// <remarks>
    /// <b>This test pins the gate's EXISTENCE. It does NOT pin its POSITION, and it never could —
    /// which is why the test below it had to be written.</b>
    /// <para>
    /// The identifier here is <see cref="RecruiterEmail"/>, which MATCHES an ad, so
    /// <c>matched.Total == 1</c>. The nothing-held early return is guarded by
    /// <c>if (matched.Total == 0)</c> — false here. So even with the two blocks in the WRONG order
    /// (nothing-held first), control simply falls through to the gate and returns the same 409. This
    /// test is green under BOTH orderings. Mutation-verified 2026-07-14: moving the nothing-held
    /// block above the gate leaves it green.
    /// </para>
    /// <para>
    /// A control that cannot fail when the thing it guards is broken is not a control. The ordering
    /// is pinned by
    /// <see cref="Confirming_ads_when_the_identifier_now_matches_NOTHING_is_REFUSED_not_answered_we_hold_nothing"/>,
    /// whose input is the only one that discriminates: an identifier that matches ZERO.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Confirming_an_id_outside_the_match_set_is_REFUSED_gate_EXISTENCE_only()
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
                // The operator confirms an ad that is NOT in the current match set — exactly what a
                // stale dry-run view looks like after ten minutes of ingest.
                ConfirmedJobAdIds: [Guid.NewGuid()]),
            ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("EraseRecruiterAds.ConfirmationMismatch");

        (await ColumnsStillContainingAsync(db, RecruiterEmail, ct))
            .ShouldNotBeEmpty("a refused erasure must destroy nothing.");
    }

    /// <summary>
    /// <b>The gate's POSITION — the one input that discriminates.</b> The operator dry-ran, read
    /// three ads, and confirmed them. Between his reading and his confirming, the corpus moved and
    /// the identifier now matches NOTHING. He must be REFUSED (409). He must NOT be told
    /// <c>NoMatchInSearchableSurfaces</c>.
    /// </summary>
    /// <remarks>
    /// <b>Why the sibling test above cannot cover this.</b> The nothing-held early return is guarded
    /// by <c>if (matched.Total == 0)</c>. Every other test in this file confirms ids against an
    /// identifier that matches SOMETHING, so that guard is false and control reaches the
    /// confirmation gate no matter which of the two blocks comes first. The ordering is
    /// unobservable — until <c>matched.Total == 0</c>. Then, and only then, do the two orderings
    /// produce different answers:
    /// <list type="bullet">
    /// <item><b>Gate first (correct):</b> 409 ConfirmationMismatch — "the ads you reviewed are gone;
    /// look again". A refusal, recorded, with nothing destroyed.</item>
    /// <item><b>Nothing-held first (the defect):</b> 200 OK, <c>NoMatchInSearchableSurfaces</c> — and
    /// the runbook chains that word to a reply telling a NAMED PERSON we hold nothing about her. She
    /// would be sent a false statement of absence, generated out of a discrepancy the system
    /// swallowed, at the exact moment the operator's picture and reality are furthest apart.</item>
    /// </list>
    /// That is the whole bug class #842 exists to end, reachable inside its own fix. This test is
    /// the only thing standing on it.
    /// </remarks>
    [Fact]
    public async Task Confirming_ads_when_the_identifier_now_matches_NOTHING_is_REFUSED_not_answered_we_hold_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = NewEraseHandler(scope, db);

        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(
                RequestId: Guid.NewGuid(),

                // Matches NOTHING — no ad, no recent search, no saved search, nothing. matched.Total
                // is 0, so the nothing-held branch is ARMED. This is the discriminating input.
                Identifier: "ingen.alls@finnsinte.se",
                DryRun: false,

                // ...and yet the operator is confirming ads. His view and reality have diverged
                // completely. A destructive request built on a view this stale must be REFUSED.
                ConfirmedJobAdIds: [Guid.NewGuid()]),
            ct);

        result.IsFailure.ShouldBeTrue(
            "confirming ads against a corpus that now matches ZERO must be a 409 refusal. If this "
            + "returns Success, the nothing-held branch runs BEFORE the confirmation gate and we "
            + "answered 'we hold no data about you' to a request that contradicted itself.");

        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("EraseRecruiterAds.ConfirmationMismatch");
    }

    // ================================================================================
    // Helpers
    // ================================================================================

    /// <summary>
    /// A user who searched for the recruiter BY NAME — which is exactly what §1.5 proves is possible:
    /// the FTS index makes her reverse-lookupable, so her name lands in another person's search
    /// history. Seeded through the aggregates' own factories (no hand-written columns).
    /// </summary>
    private async Task<JobSeekerId> SeedSeekerWithSearchesNamingHerAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var criteria = SearchCriteria.Create(
            null, null, null, null, null, null, RecruiterName, JobAdSortBy.Relevance).Value;

        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, currentCount: 0, now: clock.UtcNow));

        db.SavedSearches.Add(
            SavedSearch.Create(seeker.Id, "Bevakning", criteria, notificationEnabled: false, clock).Value);

        await db.SaveChangesAsync(ct);
        return seeker.Id;
    }

    /// <summary>
    /// A job seeker with one <c>recent_job_searches</c> row per <paramref name="queries"/> entry,
    /// each built by <see cref="RecentJobSearch.Capture"/> — the aggregate's own factory, which is
    /// what derives <c>FilterHash</c> from the criteria. Not one column is hand-written (#843).
    /// </summary>
    private async Task<JobSeekerId> SeedRecentSearchesAsync(
        CancellationToken ct, params string[] queries)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        foreach (var q in queries)
        {
            var criteria = SearchCriteria.Create(
                null, null, null, null, null, null, q, JobAdSortBy.Relevance).Value;

            db.RecentJobSearches.Add(
                RecentJobSearch.Capture(seeker.Id, criteria, currentCount: 0, now: clock.UtcNow));
        }

        await db.SaveChangesAsync(ct);
        return seeker.Id;
    }

    /// <summary>
    /// An application written TO an ingested ad, seeded exactly the way
    /// <c>CreateApplicationFromJobAdCommandHandler</c> does it: project the ad's fields, capture an
    /// <see cref="AdSnapshot"/>, <see cref="DomainApplication.CreateFromJobAd"/>, then transition to
    /// Submitted. No hand-written columns, no fabricated snapshot.
    /// </summary>
    /// <remarks>
    /// <c>coverLetter</c> is null on purpose. It is the DEK-encrypted column
    /// (<c>HeldButNotSearchable</c>), no field-encryption interceptor is registered in this harness,
    /// and NOTHING in this file may come to depend on one — the erasure path does not scan that
    /// column and a test that needed it would be testing a surface the feature deliberately refuses.
    /// </remarks>
    private async Task<DomainApplicationId> SeedApplicationForAdAsync(string externalId, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        var seeker = JobSeeker.Register(Guid.NewGuid(), "Sökande", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var ad = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == externalId, ct);

        var snapshot = AdSnapshot.Capture(
            ad.Title,
            ad.Company.Name,
            municipalityConceptId: null,
            ad.Url,
            ad.Source.Value,
            ad.PublishedAt,
            ad.ExpiresAt,
            ad.Description, // the sanitised JobAd.Description — never raw_payload (ADR 0086 D5)
                            // #842 Tier A: the frozen contact block mirrors the production capture projection
                            // (CreateApplicationFromJobAdCommandHandler projects j.Contacts) — the ingested ad
                            // carries the promoted contacts, and the snapshot freezes them.
            contacts: ad.Contacts,
            clock.UtcNow);

        var application = DomainApplication
            .CreateFromJobAd(seeker.Id, ad.Id, snapshot, coverLetter: null, clock).Value;

        application.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();

        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        return application.Id;
    }

    /// <summary>
    /// A MANUALLY tracked application (no <c>JobAdId</c>), seeded through
    /// <see cref="ManualPosting.Create"/> + <see cref="DomainApplication.Create"/> — the same two
    /// factories <c>CreateApplicationCommandHandler</c> calls. The URL is validated by the value
    /// object (http(s), absolute), so a link the domain would reject cannot be smuggled in here.
    /// </summary>
    private async Task<DomainApplicationId> SeedManualApplicationAsync(string url, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        var seeker = JobSeeker.Register(Guid.NewGuid(), "Sökande", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var manual = ManualPosting
            .Create("Backend-utvecklare", "Skill AB", url, expiresAt: null).Value;

        var application = DomainApplication
            .Create(seeker.Id, jobAdId: null, coverLetter: null, manual, clock).Value;

        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        return application.Id;
    }

    /// <summary>
    /// A criteria-based company watch (#560) whose LABEL names the recruiter. Seeded through
    /// <see cref="CompanyWatchCriteriaSpec.Create"/> + <see cref="CompanyWatchCriterion.Create"/>;
    /// the label is normalised and length-checked by the aggregate, exactly as in production.
    /// </summary>
    private async Task<CompanyWatchCriterionId> SeedCompanyWatchCriterionAsync(
        string label, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        // Both axes are required by the spec (Fork B1): one SNI leaf, one kommun.
        var spec = CompanyWatchCriteriaSpec.Create(["62010"], ["1480"]).Value;
        var criterion = CompanyWatchCriterion.Create(Guid.NewGuid(), spec, label, clock).Value;

        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);

        return criterion.Id;
    }

    /// <summary>
    /// The uploaded CV's FILE NAME is plaintext free text, and it is SEARCHED. A channel that has
    /// never returned a row in a test has not been tested — it has been typed.
    /// </summary>
    /// <remarks>
    /// This column sat in <c>NotRecruiterData</c> — <i>"structurally cannot hold a recruiter's
    /// personal data"</i> — while the SAME AGGREGATE masks personnummer OUT of it (#465), a guard
    /// bolted on precisely because users put arbitrary text into filenames. The registry's strongest
    /// claim, contradicted by a control living ten lines away.
    /// <para>
    /// It also nearly escaped classification ENTIRELY: <c>parsed_resumes</c> was excluded wholesale
    /// by a table list, one level above the column registry.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_uploaded_CVs_FILE_NAME_naming_her_is_MATCHED_and_left_for_a_human()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        await SeedParsedResumeNamedAfterHerAsync($"Ansokan till {RecruiterName}.pdf", ct);

        var probe = await EraseAsync(RecruiterName, ct, dryRun: true);

        probe.Matched.ResumeMetadata.ShouldBe(1,
            "parsed_resumes.source_file_name is PLAINTEXT and is scanned. If this is 0 the column is "
            + "classified, reported in the reply, and never actually looked at — a certified-clean "
            + "surface nobody checked, which is the #842 shape exactly.");

        var result = await EraseAsync(RecruiterName, ct);

        result.Erased.ResumeMetadata.ShouldBe(0,
            "report-only: a job does not silently rename a user's own uploaded file. A HUMAN erases "
            + "it, with that user in the loop.");
        result.Matched.ResumeMetadata.ShouldBe(1, "and the gap between matched and erased IS the "
            + "disclosure the reply template carries.");

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var surviving = await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM parsed_resumes
                WHERE source_file_name LIKE '%Fagerberg%'
                """)
            .ToListAsync(ct);

        surviving[0].ShouldBe(1, "the file name survives the automatic run — reported, not rewritten.");
    }

    /// <summary>
    /// Seeded through <c>ParsedResume.Create</c> — the real factory, no hand-written columns (#843).
    /// </summary>
    /// <remarks>
    /// <c>raw_text</c> is Form-A DEK-encrypted in production. This harness registers no encryption
    /// interceptors, so it lands here as plaintext — <b>and that is fine, because nothing in this
    /// test reads it.</b> The assertion is about <c>source_file_name</c>, which is plaintext in
    /// production too. A test that asserted against the CV BODY here would be asserting against a
    /// state production cannot construct, which is the defect this whole file exists to avoid.
    /// </remarks>
    private async Task SeedParsedResumeNamedAfterHerAsync(string fileName, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var parsed = ParsedResume.Create(
            JobSeekerId.New(),
            fileName,
            "application/pdf",
            ResumeLanguage.Sv,
            ParsedResumeContent.Empty,
            "rå CV-text",
            ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
            PersonnummerScanOutcome.None,
            [],
            new FixedClock()).Value;

        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(ct);
    }

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

        IReadOnlyList<Guid>? confirmed = null;
        if (!dryRun)
        {
            // The API forces this ordering; the test walks the same road rather than around it —
            // dry run first, REVIEW the ads it returns, then confirm them by id.
            var probe = await handler.Handle(
                new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, DryRun: true, null), ct);
            confirmed = [.. probe.Value.Matches.Select(m => m.JobAdId)];
        }

        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, dryRun, confirmed), ct);

        result.IsSuccess.ShouldBeTrue(
            result.IsFailure ? result.Error.Code : string.Empty);

        await db.SaveChangesAsync(ct);
        return result.Value;
    }

    // ================================================================================
    // 5. The tombstone's SHAPE — the fitness function over Erase() itself.
    // ================================================================================

    /// <summary>
    /// What an erased ad must look like, column by column. <b>The KEYS are cross-checked against
    /// <see cref="ErasureCascadeRegistry"/>: a <c>job_ads</c> column classified <c>Erased</c> with no
    /// entry here BREAKS THE BUILD, and an entry here for a column no longer classified <c>Erased</c>
    /// breaks it too.</b> The claim and the proof cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Values are compared as <c>::text</c>, so <c>null</c> below means SQL NULL.
    /// </remarks>
    private static readonly Dictionary<string, string?> ErasedTombstoneShape = new(StringComparer.Ordinal)
    {
        ["title"] = string.Empty,
        ["description"] = string.Empty,
        ["url"] = string.Empty,
        ["company_name"] = "[raderad]",
        ["raw_payload"] = null,

        // THE ONE THIS TEST WAS WRITTEN FOR. A sole trader's organisation number IS her
        // personnummer (CLAUDE.md §5 — the highest-priority guard in the product).
        ["organization_number"] = null,

        // ExtractedTerms.Empty, not null: null means "never extracted" and would re-queue the
        // tombstone into BackfillJobAdExtractedTermsJob. The STORED extracted_lexemes shadow follows.
        ["extracted_terms"] = "[]",
        ["extracted_lexemes"] = "[]",

        // STORED generated from title + description, which are now empty.
        ["search_vector"] = string.Empty,

        // #842 Tier A — the structured contact carrier is the recruiter's most direct PII on the
        // row; Erase() clears it explicitly (retention normally already has, but Erase() can run
        // against a still-Active ad).
        ["contacts"] = null,
    };

    /// <summary>
    /// <b>Erase() had no column-level test. It had been TYPED.</b> This is the fitness function that
    /// makes the tombstone's shape a property of the system rather than a coincidence.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists, and it is not hypothetical.</b> Until #841, <c>organization_number</c> was
    /// a STORED GENERATED column derived from <c>raw_payload</c>. <c>Erase()</c> nulls
    /// <c>raw_payload</c>, so Postgres nulled the org.nr for free — and the cascade registry
    /// certified the column <c>Erased</c> on the strength of that. <b>Erase() did not know the column
    /// existed.</b>
    /// <para>
    /// #841 materialised it into an ordinary, ingest-written column, and left the instruction in a
    /// comment in <c>JobAdConfiguration</c>: <i>"Any Art. 17 erasure path must now clear this column
    /// EXPLICITLY; it will not vanish on its own."</i> Nothing enforced it. Two lanes crossed, and an
    /// Art. 17 erasure silently stopped erasing a personnummer while the registry said it had and the
    /// reply template told a named data subject so.
    /// </para>
    /// <para>
    /// <b>A comment is not a control.</b> A control that works by accident is not a control either —
    /// it is a coincidence you have not yet been billed for. This test is the bill.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_tombstone_is_EMPTY_in_every_column_the_registry_certifies_as_erased()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        // The list is DERIVED from the registry, so the claim and the proof cannot drift.
        var certifiedErased = ErasureCascadeRegistry.Columns
            .Where(kv => kv.Value == ErasureColumnDisposition.Erased)
            .Select(kv => kv.Key)
            .Where(k => k.StartsWith("job_ads.", StringComparison.Ordinal))
            .Select(k => k["job_ads.".Length..])
            .ToHashSet(StringComparer.Ordinal);

        certifiedErased.ShouldContain("organization_number",
            "if this column ever leaves the Erased bucket, an enskild firma's PERSONNUMMER survives "
            + "an Art. 17 erasure. It does not leave the bucket.");

        certifiedErased.ShouldBe(ErasedTombstoneShape.Keys.ToHashSet(StringComparer.Ordinal),
            ignoreOrder: true,
            "the registry certifies a set of job_ads columns as destroyed by the erasure, and this "
            + "test asserts a shape for each. They must be the SAME set.\n"
            + "  certified Erased by the registry: "
            + string.Join(", ", certifiedErased.Order(StringComparer.Ordinal)) + "\n"
            + "  asserted by ErasedTombstoneShape:  "
            + string.Join(", ", ErasedTombstoneShape.Keys.Order(StringComparer.Ordinal)) + "\n\n"
            + "Added a column to the Erased bucket? Say what erased LOOKS LIKE for it. Certifying a "
            + "column as destroyed without asserting it is how a personnummer survived an erasure "
            + "the reply template said had happened.");

        // The sole trader: her company name IS her name, and her org.nr IS a personnummer.
        await EraseAsync(SoleTraderName, ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var (column, expected) in ErasedTombstoneShape.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // The column name comes from the registry (never user input) and is quoted; the id is a
            // bound parameter. ::text normalises jsonb / tsvector / varchar to one comparable form.
            var sql = $"SELECT \"{column}\"::text AS \"Value\" FROM job_ads "
                + "WHERE external_id = {0}";

            var actual = (await db.Database
                    .SqlQueryRaw<string?>(sql, SoleTraderExternalId)
                    .ToListAsync(ct))
                .Single();

            actual.ShouldBe(expected,
                $"job_ads.{column} is certified Erased by ErasureCascadeRegistry, and the Art. 12(3) "
                + $"reply tells the data subject it was destroyed. It holds: {actual ?? "NULL"}");
        }

        // And the belt: her personnummer-shaped org.nr is not anywhere else in the row either.
        var offenders = await ColumnsStillContainingAsync(db, "5509281234", ct);
        offenders.ShouldBeEmpty(
            "the sole trader's organisation number is her PERSONNUMMER. It survived the erasure in: "
            + string.Join(", ", offenders));
    }

    // ================================================================================
    // 6. THE OUTCOME WORD CANNOT LIE.
    //
    //    EraseRecruiterAdsResponse.Outcome is DERIVED from the counts — it is not a constructor
    //    argument anybody can get wrong. The runbook keys its reply template on that one word, so
    //    it is the single value in the whole response that must not be able to make a false
    //    statement to a named person. A derivation is only a theorem if something evaluates it on
    //    the cases that used to lie. These are those cases.
    // ================================================================================

    /// <summary>
    /// <b>The sentence that used to be false.</b> A recruiter whose ONLY trace in the system is a
    /// job seeker's cached search term is a real case — the FTS index makes her reverse-lookupable,
    /// so her name lands in other people's search history whether or not she is in an ad. Nothing of
    /// hers is in an ad; a cache row is deleted; the outcome is <c>CascadeErasedOnly</c>.
    /// </summary>
    /// <remarks>
    /// While <c>Outcome</c> was a constructor parameter, this case produced <c>AdsErased</c> with
    /// <c>Erased.JobAds == 0</c> (the total summed the cascade). The runbook chains <c>AdsErased</c>
    /// to <i>"vi har tagit bort hela annonsen"</i> — a false statement, generated by the system,
    /// signed by us, sent to her.
    /// </remarks>
    [Fact]
    public async Task Deleting_only_a_cache_row_reports_CascadeErasedOnly_and_never_AdsErased()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        // A DIFFERENT recruiter. She appears in no ad — her only trace is a user's cached search.
        await SeedRecentSearchesAsync(ct, "anna karlsson");

        var response = await EraseAsync("Anna Karlsson", ct);

        response.Matched.JobAds.ShouldBe(0, "she is in no ad. If she were, this test would be "
            + "asserting AdsErased's precondition instead of CascadeErasedOnly's.");
        response.Erased.JobAds.ShouldBe(0);
        response.Erased.RecentJobSearches.ShouldBe(1);

        response.Outcome.ShouldBe(ErasureOutcome.CascadeErasedOnly,
            "AdsErased here would tell her 'vi har tagit bort hela annonsen' when we erased zero "
            + "ads. The word is a pure function of the counts precisely so it cannot say that.");
    }

    /// <summary>
    /// We DO hold matching data and erased NONE of it — the operator reviewed the ads and confirmed
    /// nothing. <c>NothingErased</c>, which is a different sentence from <c>NoMatchInSearchableSurfaces</c>
    /// and only one of the two can honestly be sent as a completed erasure.
    /// </summary>
    [Fact]
    public async Task Matching_something_and_confirming_nothing_reports_NothingErased_not_NoMatch()
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

                // He reviewed the ad and confirmed NOTHING. An empty list is not a stale view — no
                // id "vanished" — so the gate passes and the erase loop simply has nothing to do.
                ConfirmedJobAdIds: []),
            ct);

        result.IsSuccess.ShouldBeTrue();
        var response = result.Value;

        response.Matched.JobAds.ShouldBe(1, "we DO hold an ad naming her.");
        response.Erased.Total.ShouldBe(0);

        response.Outcome.ShouldBe(ErasureOutcome.NothingErased,
            "'we found nothing' and 'we found things and removed none of them' are different "
            + "sentences. Only one of them may be sent to a data subject as a completed erasure.");
    }

    /// <summary>
    /// The positive pole: ads were actually destroyed ⇒ <c>AdsErased</c>. And the cheap, honest
    /// sanity net over the counter — every ingested ad carries an external id, so for these ads
    /// <c>Erased.JobAds</c> must equal <c>ErasedExternalIds.Count</c>. If the counter ever drifts
    /// from the accountability list, the Art. 12(3) number and the list an auditor checks us against
    /// disagree.
    /// </summary>
    [Fact]
    public async Task Erasing_ads_reports_AdsErased_and_the_counter_agrees_with_the_external_id_list()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var response = await EraseAsync(RecruiterName, ct);

        response.Outcome.ShouldBe(ErasureOutcome.AdsErased);
        response.Erased.JobAds.ShouldBeGreaterThan(0);

        response.Erased.JobAds.ShouldBe(response.ErasedExternalIds.Count,
            "every ad in this corpus was INGESTED, so every one of them carries an external id. "
            + "The counter and the accountability list must not be able to disagree.");
    }

    /// <summary>
    /// <b><c>Erased.JobAds</c> counts <see cref="JobAd.Erase"/>'s VERDICT — never the ad's
    /// status.</b> The two look equivalent and are not: <c>Erase()</c> REFUSES <i>because</i> the
    /// status is already <c>Erased</c>, so a refused ad satisfies
    /// <c>Count(j =&gt; j.Status == JobAdStatus.Erased)</c> and would be counted as erased BY US —
    /// the guard undone by the line below it, and the inflated number posted straight into an
    /// Art. 12(3) reply.
    /// </summary>
    /// <remarks>
    /// <b>The race is real, and nothing here fabricates it.</b> The only thing that erases an ad is
    /// this command, so the interleaving is TWO erasure requests overlapping — two operators, or one
    /// operator retrying a request he thinks timed out. <see cref="MatchQueryThatErasesMidFlight"/>
    /// is a scheduling device, not a state device: it delegates every method to the REAL
    /// <c>RecruiterErasureMatchQuery</c> and, after the match returns, lets a SECOND, ordinary
    /// erasure run to completion through <c>JobAd.Erase()</c> + <c>SaveChanges</c> in its own scope.
    /// The state under test is produced entirely by production code. Nothing is hand-written, no
    /// column is poked, and no state is constructed that production cannot construct (#843).
    /// <para>
    /// <b>Mutation-verified 2026-07-14:</b> replace the loop's verdict-counter with
    /// <c>jobAds.Count(j =&gt; j.Status == JobAdStatus.Erased)</c> and this test goes RED —
    /// <c>Erased.JobAds</c> becomes 1 and the outcome flips to <c>AdsErased</c>, i.e. we would have
    /// reported destroying an ad that this request did not destroy.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_ad_erased_by_a_CONCURRENT_request_is_NOT_counted_as_erased_by_this_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        // Step 1 — the operator's dry run, through the REAL query. He reads the ad and confirms it.
        Guid confirmedId;
        using (var probeScope = _provider.CreateScope())
        {
            var probeDb = probeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var probe = await NewEraseHandler(probeScope, probeDb).Handle(
                new EraseRecruiterAdsCommand(Guid.NewGuid(), RecruiterEmail, DryRun: true, null), ct);

            confirmedId = probe.Value.Matches.Single().JobAdId;
        }

        // Step 2 — his destructive call. A SECOND erasure request lands between this one's match and
        // its tracked re-load, and erases the same ad first. Ordinary production code, own scope.
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sabotaged = new MatchQueryThatErasesMidFlight(
            scope.ServiceProvider.GetRequiredService<IRecruiterErasureMatchQuery>(),
            () => EraseTheAdInAnotherRequestAsync(confirmedId, ct));

        var handler = new EraseRecruiterAdsCommandHandler(
            db, sabotaged, new FixedClock(),
            NullLogger<EraseRecruiterAdsCommandHandler>.Instance);

        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(
                Guid.NewGuid(), RecruiterEmail, DryRun: false, [confirmedId]),
            ct);

        await db.SaveChangesAsync(ct);

        result.IsSuccess.ShouldBeTrue();
        var response = result.Value;

        response.Matched.JobAds.ShouldBe(1,
            "the match ran BEFORE the concurrent erase — the ad was still live then. If this is 0 "
            + "the interleaving never happened and the rest of this test proves nothing.");

        response.Erased.JobAds.ShouldBe(0,
            "THIS request erased nothing — Erase() refused with JobAd.AlreadyErased. Counting the "
            + "ad's STATUS instead of Erase()'s verdict reports 1, and that 1 goes into an Art. "
            + "12(3) reply as an ad we destroyed for her.");

        response.ErasedExternalIds.ShouldBeEmpty(
            "and the accountability list stays empty, in agreement with the counter.");

        response.Outcome.ShouldBe(ErasureOutcome.NothingErased,
            "we matched an ad and destroyed none of it. AdsErased here would be a false statement "
            + "about what THIS request did.");

        // The ad IS erased — by the other request. The corpus is right; only OUR count is at issue.
        using var check = _provider.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await ColumnsStillContainingAsync(after, RecruiterEmail, ct)).ShouldBeEmpty();
    }

    /// <summary>
    /// A second, ordinary Art. 17 erasure of <paramref name="jobAdId"/>, run to completion in its
    /// own scope through the real aggregate. This is what a concurrent request does.
    /// </summary>
    private async Task EraseTheAdInAnotherRequestAsync(Guid jobAdId, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = new JobAdId(jobAdId);
        var ad = await db.JobAds.SingleAsync(j => j.Id == id, ct);

        ad.Erase(new FixedClock()).IsSuccess.ShouldBeTrue(
            "the concurrent request must genuinely succeed, or the race under test never occurs.");

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Delegates every channel to the REAL match query, and fires <c>onMatched</c> exactly once,
    /// after <see cref="FindJobAdsAsync"/> has returned — the window between the handler's match and
    /// its tracked re-load. A scheduler, not a stub: it invents no data and answers no question
    /// itself.
    /// </summary>
    private sealed class MatchQueryThatErasesMidFlight(
        IRecruiterErasureMatchQuery inner, Func<Task> onMatched) : IRecruiterErasureMatchQuery
    {
        private bool _fired;

        public async Task<IReadOnlyList<ErasureJobAdMatch>> FindJobAdsAsync(
            string identifier, CancellationToken cancellationToken)
        {
            var matches = await inner.FindJobAdsAsync(identifier, cancellationToken);

            if (!_fired)
            {
                _fired = true;
                await onMatched();
            }

            return matches;
        }

        public Task<IReadOnlyList<ErasureRecentSearchMatch>> FindRecentJobSearchesAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.FindRecentJobSearchesAsync(identifier, cancellationToken);

        public Task<int> CountSavedSearchesAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.CountSavedSearchesAsync(identifier, cancellationToken);

        public Task<IReadOnlyList<Guid>> FindApplicationSnapshotContactsAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.FindApplicationSnapshotContactsAsync(identifier, cancellationToken);

        public Task<int> CountApplicationSnapshotsAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.CountApplicationSnapshotsAsync(identifier, cancellationToken);

        public Task<int> CountManualAdEntriesAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.CountManualAdEntriesAsync(identifier, cancellationToken);

        public Task<int> CountCompanyWatchCriteriaAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.CountCompanyWatchCriteriaAsync(identifier, cancellationToken);

        public Task<int> CountResumeMetadataAsync(
            string identifier, CancellationToken cancellationToken) =>
            inner.CountResumeMetadataAsync(identifier, cancellationToken);

        public Task<int> CountApplicationsReferencingAsync(
            IReadOnlyCollection<Guid> matchedJobAdIds, CancellationToken cancellationToken) =>
            inner.CountApplicationsReferencingAsync(matchedJobAdIds, cancellationToken);
    }

    // ================================================================================
    // 7. THE CHANNEL PINS (round 6) — one row per channel column, matching on THAT
    //    COLUMN ALONE.
    //
    //    ErasureCascadeRegistryTests pins the CLAIM (searched column ⇒ channel ⇒ port
    //    method). No reflection can prove the SQL body touches the columns the channel
    //    claims — round 5's B5-2 (snapshot_url: classified "searched and reported",
    //    never queried, seven guards green) lived exactly in that gap. These tests pin
    //    the QUERY: each seeds a row whose identifier lives in ONE claimed column and
    //    requires a non-zero match. Delete a column from its SQL and exactly one of
    //    these goes red.
    // ================================================================================

    /// <summary>
    /// <b>Round 5's Blocker (B5-1), killed on both of its defects at once.</b> An org.nr
    /// identifier — in the HYPHENATED written form a person actually uses — reaches (a) the
    /// employer-only recent search (<c>q = NULL</c>, the domain's canonical form; round 5's
    /// projection threw exactly this row away after the SQL had found it) and (b) the sole
    /// trader's ad AFTER the raw_payload purge, where <c>organization_number</c> is the ONLY
    /// column still carrying her org.nr — which IS her personnummer.
    /// </summary>
    /// <remarks>
    /// Round 5's arm ran the word-boundary REGEX for a name over a column of ten-digit strings:
    /// zero rows, structurally, forever, and the zero was certified as a search result. The CTO
    /// ruling (2026-07-14) made org.nr a first-class Art. 17 identifier instead: normalised in
    /// Domain, matched EXACTLY. Delete either exact-match arm and this test goes red — the suite
    /// could not say that about the old arm, because every seed sent <c>employer: null</c>.
    /// </remarks>
    [Fact]
    public async Task An_org_nr_identifier_reaches_the_employer_only_search_AND_the_purged_ad()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        // Her org.nr in a user's employer FILTER, with q = NULL — a valid, canonical search.
        using (var seed = _provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new FixedClock();
            var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);

            var employerOnly = SearchCriteria.Create(
                null, null, null, null, null,
                employer: ["5509281234"], q: null, JobAdSortBy.PublishedAtDesc).Value;

            db.RecentJobSearches.Add(
                RecentJobSearch.Capture(seeker.Id, employerOnly, currentCount: 0, now: clock.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        // The purge: after 30 days her org.nr survives ONLY in organization_number (#841
        // materialised it; raw_payload is NULL for most of the corpus).
        using (var purge = _provider.CreateScope())
        {
            var db = purge.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE job_ads SET raw_payload = NULL;", ct);
        }

        // The WRITTEN form, hyphen and all — round 5's arm would never have matched it against
        // the stored "5509281234" even where it could match at all.
        var probe = await EraseAsync("550928-1234", ct, dryRun: true);

        probe.Matched.RecentJobSearches.ShouldBe(1,
            "the employer-only row (q = NULL) must be MATCHED. If this is 0, either the exact "
            + "employer_list arm is gone or the projection is discarding the q-less row again — "
            + "round 5's certified-but-never-deleted defect, both halves.");

        probe.MatchedRecentSearchTerms.ShouldContain("arbetsgivarfilter: 5509281234 (personnummer-format)",
            "the operator reviews WHY a hard-deleted row matched. A q-less row must show the "
            + "matched org.nr — flagged, because this ten-digit value is personnummer-shaped "
            + "(ADR 0087 D8(c): never surfaced un-flagged, even to the operator).");

        probe.Matched.JobAds.ShouldBe(1,
            "her ad must be found via organization_number — raw_payload is NULL, and her org.nr "
            + "appears in no other searchable column. This is the >30-day corpus, i.e. MOST ads.");

        var adMatch = probe.Matches.Single();
        adMatch.MatchedChannel.ShouldBe(ErasureMatchChannel.OrganizationNumber);
        adMatch.MatchedExcerpt.ShouldBe("5509281234 (personnummer-format)",
            "the evidence is the normalised org.nr that matched, flagged as personnummer-shaped.");

        // The destructive run: the ad is erased AND the employer-only row is hard-deleted.
        var result = await EraseAsync("550928-1234", ct);

        result.Erased.RecentJobSearches.ShouldBe(1,
            "found by SQL must mean DELETED — round 5 matched the row and then deleted a filtered "
            + "projection that no longer contained it.");
        result.ErasedExternalIds.ShouldContain(SoleTraderExternalId);

        using var check = _provider.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<AppDbContext>();

        var surviving = await after.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM recent_job_searches
                WHERE '5509281234' = ANY(coalesce(employer_list, ARRAY[]::text[]))
                """)
            .ToListAsync(ct);
        surviving[0].ShouldBe(0, "her personnummer-shaped org.nr must not survive in any "
            + "user's employer filter after an erasure we certified as executed.");

        (await ColumnsStillContainingAsync(after, "5509281234", ct)).ShouldBeEmpty(
            "and the tombstoned ad row holds it nowhere either.");
    }

    /// <summary>
    /// Every snapshot column matches on that column ALONE — <c>snapshot_url</c> included, which is
    /// round 5's B5-2: classified <c>MatchedRetained</c> ("searched and reported") while the query
    /// touched three of four columns, with seven guards green. Remove any column from
    /// <c>CountApplicationSnapshotsAsync</c>'s SQL and exactly one arm of this test goes red.
    /// </summary>
    [Fact]
    public async Task Every_snapshot_column_is_matched_on_that_column_ALONE()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        await SeedSnapshotApplicationAsync(
            company: "Cornelia Vinterqvist Rekrytering", title: "Kock",
            description: "Neutral annonstext.", url: "https://example.se/annons/1", ct);
        await SeedSnapshotApplicationAsync(
            company: "Neutral AB", title: "Rekryterare Ebba Lindelöf",
            description: "Neutral annonstext.", url: "https://example.se/annons/2", ct);
        await SeedSnapshotApplicationAsync(
            company: "Neutral AB", title: "Kock",
            description: "Kontakta Gustav Palmcrantz för frågor.", url: "https://example.se/annons/3", ct);
        await SeedSnapshotApplicationAsync(
            company: "Neutral AB", title: "Kock",
            description: "Neutral annonstext.", url: "https://example.se/rekryterare/johanna-silfverberg", ct);

        (await EraseAsync("Cornelia Vinterqvist", ct, dryRun: true)).Matched.ApplicationSnapshots
            .ShouldBe(1, "snapshot_company alone must carry the match.");
        (await EraseAsync("Ebba Lindelöf", ct, dryRun: true)).Matched.ApplicationSnapshots
            .ShouldBe(1, "snapshot_title alone must carry the match.");
        (await EraseAsync("Gustav Palmcrantz", ct, dryRun: true)).Matched.ApplicationSnapshots
            .ShouldBe(1, "snapshot_description alone must carry the match.");
        (await EraseAsync("johanna-silfverberg", ct, dryRun: true)).Matched.ApplicationSnapshots
            .ShouldBe(1, "snapshot_url alone must carry the match — the column that was 'searched "
                + "and reported' for one whole round while no SQL touched it (B5-2). A name in a "
                + "URL path is the manual_url argument, verbatim.");
    }

    /// <summary>
    /// The manual columns' remaining two single-column pins (<c>manual_url</c> has its own test).
    /// </summary>
    [Fact]
    public async Task Every_manual_column_is_matched_on_that_column_ALONE()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        await SeedManualApplicationWithDetailsAsync(
            title: "Backend-utvecklare", company: "Ferdinand Åkerlund Rekrytering",
            url: "https://example.se/jobb/4", ct);
        await SeedManualApplicationWithDetailsAsync(
            title: "Assistent till Beatrice Ulvaeus", company: "Neutral AB",
            url: "https://example.se/jobb/5", ct);

        (await EraseAsync("Ferdinand Åkerlund", ct, dryRun: true)).Matched.ManualAdEntries
            .ShouldBe(1, "manual_company alone must carry the match.");
        (await EraseAsync("Beatrice Ulvaeus", ct, dryRun: true)).Matched.ManualAdEntries
            .ShouldBe(1, "manual_title alone must carry the match.");
    }

    /// <summary>
    /// A saved search whose NAME names her — criteria neutral — is matched on the name alone.
    /// (The criteria channel has its own seed in the recent/saved-search test above.)
    /// </summary>
    [Fact]
    public async Task A_saved_search_NAME_naming_her_is_matched_on_the_name_ALONE()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using (var seed = _provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new FixedClock();
            var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);

            var neutralCriteria = SearchCriteria.Create(
                null, null, null, null, null, null, "sjuksköterska", JobAdSortBy.Relevance).Value;

            db.SavedSearches.Add(SavedSearch.Create(
                seeker.Id, "Petra Sandelins annonser", neutralCriteria,
                notificationEnabled: false, clock).Value);
            await db.SaveChangesAsync(ct);
        }

        (await EraseAsync("Petra Sandelin", ct, dryRun: true)).Matched.SavedSearches
            .ShouldBe(1, "saved_searches.name alone must carry the match — a user who names a "
                + "saved search after the recruiter holds her name in it.");
    }

    /// <summary>
    /// The ResumeMetadata channel claims FIVE columns across three tables. One row per column,
    /// each matching on that column alone. (<c>parsed_resumes.source_file_name</c> also has its
    /// own end-to-end test above; it is pinned here too so this test IS the channel's claim.)
    /// </summary>
    [Fact]
    public async Task Resume_metadata_is_matched_on_each_of_the_FIVE_columns_ALONE()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var clock = new FixedClock();

        using (var seed = _provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);

            // parsed_resumes.source_file_name — the uploaded file's name.
            db.ParsedResumes.Add(ParsedResume.Create(
                seeker.Id, "CV till Leopold Anckarström.pdf", "application/pdf",
                ResumeLanguage.Sv, ParsedResumeContent.Empty, "rå CV-text",
                ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
                PersonnummerScanOutcome.None, [], clock).Value);

            // resume_files.file_name — the same uploaded file, the sealed-original table. The
            // sealed bytes are opaque to every channel (Form C, HeldButNotSearchable), so the
            // placeholder content asserts nothing production could not construct (#843).
            db.ResumeFiles.Add(ResumeFile.CaptureOriginal(
                seeker.Id, ParsedResumeId.New(), [1, 2, 3], "application/pdf",
                "Ansokan Gunnel Bäckström.pdf", byteSize: 3, pnrFlagged: false, clock).Value);

            // resumes.name — the CV's own name, typed via Rename()/Create.
            db.Resumes.Add(Resume.Create(
                seeker.Id, "Ansökningar till Fabian Cederlöf", "Test Person", clock).Value);

            // resumes.latest_role — the denormalised projection of the LATEST experience's role.
            db.Resumes.Add(Resume.CreateFromParsed(
                seeker.Id, "CV",
                new ResumeContent(
                    new PersonalInfo("Test Person", null, null, null),
                    experiences:
                    [
                        new Experience("Neutral AB", "Underkonsult åt Malin Öqvist",
                            new DateOnly(2024, 1, 1), null, null),
                    ]),
                ParsedResumeId.New(), clock).Value);

            // resumes.top_skills — the denormalised skill-name projection.
            db.Resumes.Add(Resume.CreateFromParsed(
                seeker.Id, "CV",
                new ResumeContent(
                    new PersonalInfo("Test Person", null, null, null),
                    skills: [new Skill("Rekryteringssystemet Tindra Ekwall", null)]),
                ParsedResumeId.New(), clock).Value);

            await db.SaveChangesAsync(ct);
        }

        (await EraseAsync("Leopold Anckarström", ct, dryRun: true)).Matched.ResumeMetadata
            .ShouldBe(1, "parsed_resumes.source_file_name alone must carry the match.");
        (await EraseAsync("Gunnel Bäckström", ct, dryRun: true)).Matched.ResumeMetadata
            .ShouldBe(1, "resume_files.file_name alone must carry the match — the same uploaded "
                + "file, one table over; searching one and not the other is the registry "
                + "disagreeing with itself about identical data.");
        (await EraseAsync("Fabian Cederlöf", ct, dryRun: true)).Matched.ResumeMetadata
            .ShouldBe(1, "resumes.name alone must carry the match.");
        (await EraseAsync("Malin Öqvist", ct, dryRun: true)).Matched.ResumeMetadata
            .ShouldBe(1, "resumes.latest_role alone must carry the match.");
        (await EraseAsync("Tindra Ekwall", ct, dryRun: true)).Matched.ResumeMetadata
            .ShouldBe(1, "resumes.top_skills alone must carry the match (unnest — a LIKE against "
                + "the array's literal text form would match punctuation between elements).");
    }

    /// <summary>
    /// An ad whose TITLE alone names her is found. (The STORED <c>search_vector</c> is derived
    /// from the title, so title and FTS cannot be separated by any seed — this pins column-level
    /// reachability, which is what the channel claims.)
    /// </summary>
    [Fact]
    public async Task An_ad_whose_TITLE_alone_names_her_is_found()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var probe = await EraseAsync(TitleOnlyName, ct, dryRun: true);

        probe.Matched.JobAds.ShouldBe(1, "her name is in the HEADLINE and nowhere else.");
        probe.Matches.Single().Title.ShouldContain(TitleOnlyName);
    }

    /// <summary>
    /// An ad where the identifier survives ONLY in <c>raw_payload</c> — here
    /// <c>workplace_address.municipality</c>, a free-text NAME field JobTech controls, allowlisted
    /// by the sanitizer and projected only as a concept-id code. Before this test, deleting the
    /// raw_payload arm left the whole suite green: every other seed's payload text also existed in
    /// a projected column.
    /// </summary>
    [Fact]
    public async Task A_raw_payload_ONLY_carrier_is_found()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var probe = await EraseAsync(RawPayloadOnlyToken, ct, dryRun: true);

        probe.Matched.JobAds.ShouldBe(1,
            "the token lives in raw_payload alone (a municipality NAME; only its concept-id is "
            + "projected). If this is 0, the raw_payload arm is gone — and it is the ONLY channel "
            + "that reaches allowlisted-but-unprojected payload fields for the <30-day window.");

        var match = probe.Matches.Single();
        match.MatchedChannel.ShouldBe(ErasureMatchChannel.FullTextOrRawPayload);
        match.MatchedExcerpt.ShouldBe(string.Empty,
            "no literal substring exists in title/description/company to window — an excerpt "
            + "from an unrelated body would be evidence of nothing.");
    }

    /// <summary>
    /// <b>The <c>ESCAPE</c> regression, held red (round-5 security M3).</b> An identifier with a
    /// LIKE metacharacter (<c>_</c> — legal and common in email local parts) matches EXACTLY its
    /// own row. The clause is now derived from ONE constant (<c>LikeEscapeSql</c>); mutate it to
    /// <c>''</c> and the escaped <c>\_</c> becomes a literal backslash-underscore that matches
    /// NOTHING (this asserts 1, red) — un-escape the pattern instead and <c>_</c> becomes a
    /// single-char wildcard that ALSO matches the decoy (2, red). Round 4 shipped the first
    /// mutation on 2 of 18 hand-typed lines with a green suite.
    /// </summary>
    [Fact]
    public async Task A_LIKE_metacharacter_identifier_matches_EXACTLY_its_own_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        await SeedManualApplicationWithDetailsAsync(
            title: "Backend-utvecklare", company: "Kontakt anna_k@acme.se",
            url: "https://example.se/jobb/6", ct);
        // The decoy: identical except the metacharacter position holds another character. An
        // unescaped `_` wildcard matches both rows; a broken escape matches neither.
        await SeedManualApplicationWithDetailsAsync(
            title: "Backend-utvecklare", company: "Kontakt annaxk@acme.se",
            url: "https://example.se/jobb/7", ct);

        (await EraseAsync("anna_k@acme.se", ct, dryRun: true)).Matched.ManualAdEntries
            .ShouldBe(1, "`_` must be escaped: ESCAPE '' makes this 0 (literal backslash), an "
                + "unescaped pattern makes it 2 (wildcard eats the decoy). Only a working escape "
                + "makes it exactly 1.");
    }

    /// <summary>
    /// An application whose frozen <see cref="AdSnapshot"/> carries the given field values —
    /// captured through the real factory, exactly as apply-time does from an ad that held those
    /// values (#843: a name in an ad's title/company/description/url at apply time is a state
    /// production constructs daily).
    /// </summary>
    private async Task SeedSnapshotApplicationAsync(
        string company, string title, string description, string url, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        var seeker = JobSeeker.Register(Guid.NewGuid(), "Sökande", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var ad = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == ExternalId, ct);

        var snapshot = AdSnapshot.Capture(
            title, company, municipalityConceptId: null, url, ad.Source.Value,
            ad.PublishedAt, ad.ExpiresAt, description, contacts: null, clock.UtcNow);

        var application = DomainApplication
            .CreateFromJobAd(seeker.Id, ad.Id, snapshot, coverLetter: null, clock).Value;
        application.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();

        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A manually tracked application with caller-chosen <c>manual_title</c>/<c>manual_company</c>
    /// — the same factories as <see cref="SeedManualApplicationAsync"/>, opened up for the
    /// per-column pins.
    /// </summary>
    private async Task SeedManualApplicationWithDetailsAsync(
        string title, string company, string url, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();

        var seeker = JobSeeker.Register(Guid.NewGuid(), "Sökande", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var manual = ManualPosting.Create(title, company, url, expiresAt: null).Value;
        var application = DomainApplication
            .Create(seeker.Id, jobAdId: null, coverLetter: null, manual, clock).Value;

        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    }
}
