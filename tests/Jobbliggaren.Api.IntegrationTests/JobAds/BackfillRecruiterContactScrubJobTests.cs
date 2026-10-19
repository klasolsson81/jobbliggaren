using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #842 Tier A (re-bind R7/D10) — the one-off contact-scrub backfill
/// (<see cref="BackfillRecruiterContactScrubJob"/>), the local re-projection that reaches the
/// DE-LISTED tail the nightly sync never rewrites, against real Postgres (Testcontainers).
/// </summary>
/// <remarks>
/// <b>A RETIRED-ACTOR SEAM (CLAUDE.md §5 <c>Tests:</c>).</b> Every other test in this suite builds
/// state through the production write path, because a test that constructs a state production
/// cannot construct proves nothing about production. This job's ENTIRE SUBJECT is the ~93k rows
/// imported BEFORE the aggregate invariant landed — PRE-INVARIANT rows the funnel can no longer
/// create (a scrubbed body is a fixed point; a fresh Import always scrubs). So the legacy state — a
/// recruiter address sitting in <c>description</c>/<c>raw_payload</c> with <c>contacts = NULL</c> —
/// is seeded by a raw SQL UPDATE that bypasses the aggregate.
///
/// §5 issues no exceptions, only branches, and this is the retired-actor branch: the actor is
/// "ads imported before #842 Tier A". The branch requires the complement to be pinned — that the
/// CURRENT writer cannot produce this shape — and that pin lives in
/// <c>JobAdContactRedactionTests.Import_scrubs_body_and_payload_and_promotes_the_detected_contact</c>,
/// so it is named here rather than left for a reader to find. Plain text, not a
/// <c>see cref</c>: this project does not reference <c>Jobbliggaren.Domain.UnitTests</c>, so a cref
/// would not resolve — and with no documentation file generated for test projects, CS1574 never
/// fires to say so. A citation that looks machine-checked and is not is precisely the accepted
/// limitation this clause carries. The clean ad and the surrounding assertions still go through
/// the real aggregate and the real extractor.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class BackfillRecruiterContactScrubJobTests : IAsyncLifetime
{
    private const string CleanExternalId = "backfill-clean-1";
    private const string ActiveLegacyExternalId = "backfill-active-legacy-2";
    private const string ArchivedLegacyExternalId = "backfill-archived-legacy-3";

    // The legacy recruiter address, injected via raw SQL into pre-invariant rows.
    private const string LegacyEmail = "rutger@dahlqvist.example";
    private const string LegacyPhone = "070 555 12 34";

    // A Requirement-kind term on the Active legacy ad — taxonomy data (concept-id + label), never
    // free text, unreachable by the detector. It must SURVIVE the re-extraction after the scrub.
    private const string RequirementConceptId = "TESTREQ001";

    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;
    private ISystemEventAuditor _auditor = default!;

    public BackfillRecruiterContactScrubJobTests(SharedPostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _auditor = Substitute.For<ISystemEventAuditor>();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        var stemmer = new SnowballStemmer();
        var analyzer = new LocalTextAnalyzer(stemmer);
        services.AddSingleton<IJobAdKeywordExtractor>(
            new JobAdKeywordExtractor(analyzer, stemmer, new SkillTaxonomyIndex(analyzer)));

        services.AddSingleton(_auditor);
        services.AddSingleton<IDateTimeProvider>(new FixedClock(Now));
        services.AddSingleton(Options.Create(new BackfillRecruiterContactScrubOptions
        {
            PerItemDelayMs = 0,
            MaxItemsPerRun = 1_000_000,
            ProgressLogEvery = 1000,
        }));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<BackfillRecruiterContactScrubJob>();

        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DropDatabaseAsync(_connectionString);
    }

    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    // ================================================================================
    // Dry run — counts report detections, DB is untouched.
    // ================================================================================

    [Fact]
    public async Task Dry_run_reports_the_detections_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedLegacyCorpusAsync(ct);

        ContactScrubBackfillCounts counts;
        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>();
            counts = await job.RunAsync(dryRun: true, ct);
        }

        counts.Seen.ShouldBe(3, "every imported, non-Erased ad is streamed.");
        counts.Scrubbed.ShouldBe(2, "both legacy rows carry a detection; the clean ad does not.");
        counts.Skipped.ShouldBe(1, "the clean ad is probed and skipped — no detection, no write.");

        // The DB is untouched: the dry run mutates only the per-item scope and never SaveChanges.
        using var check = _provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();

        (await DescriptionContainsAsync(db, ActiveLegacyExternalId, "dahlqvist", ct)).ShouldBeTrue(
            "a dry run that scrubbed something is not a dry run.");
        (await ContactsIsNullAsync(db, ActiveLegacyExternalId, ct)).ShouldBeTrue(
            "and it promoted nothing.");
    }

    // ================================================================================
    // Destructive run — scrub + promote for the Active row, scrub + NULL for the Archived row,
    // clean ad untouched, Requirement terms preserved, one audit row (JobType backfill-contact-scrub).
    // ================================================================================

    [Fact]
    public async Task Destructive_run_scrubs_promotes_for_Active_clears_for_Archived_and_preserves_requirement_terms()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedLegacyCorpusAsync(ct);

        ContactScrubBackfillCounts counts;
        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>();
            counts = await job.RunAsync(dryRun: false, ct);
        }

        counts.Scrubbed.ShouldBe(2);
        counts.ContactsPromoted.ShouldBe(2,
            "the Active legacy row promotes its body email + phone; the Archived row promotes "
            + "nothing (write-gate).");

        using var check = _provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();

        // Active legacy ad — scrubbed AND its contacts promoted (it is still Active).
        var active = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == ActiveLegacyExternalId, ct);
        active.Description.ShouldContain(RecruiterContactRedactor.Marker);
        active.Description.ShouldNotContain("dahlqvist");
        active.Contacts.ShouldNotBeNull("the Active row's detected contacts are promoted.");
        active.Contacts!.IsEmpty.ShouldBeFalse();
        (await DescriptionContainsAsync(db, ActiveLegacyExternalId, "dahlqvist", ct)).ShouldBeFalse();

        // Requirement-kind term survives the re-extraction (carried over explicitly).
        active.ExtractedTerms.ShouldNotBeNull();
        active.ExtractedTerms!.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == RequirementConceptId,
            "the employer's structured Requirement term is taxonomy data — it must be carried over, "
            + "not dropped, or matching degrades for every de-listed ad permanently.");

        // Archived legacy ad — scrubbed, but contacts stays NULL (write-gate: non-Active).
        var archived = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == ArchivedLegacyExternalId, ct);
        archived.Description.ShouldContain(RecruiterContactRedactor.Marker);
        archived.Contacts.ShouldBeNull(
            "a de-listed/archived ad keeps contacts NULL — no follow-up purpose, so nothing to hold.");

        // Clean ad — probed and left entirely untouched.
        var clean = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == CleanExternalId, ct);
        clean.Description.ShouldBe("Neutral annonstext utan kontaktspår.",
            "the clean ad's body is untouched — probe-first, no detection.");

        // Accountability (GDPR Art. 30): exactly one destructive audit row.
        await _auditor.Received(1).RecordAsync(
            Arg.Is<JobAdsSynced>(e => e != null && e.JobType == "backfill-contact-scrub"),
            Arg.Any<CancellationToken>());
    }

    // ================================================================================
    // #2098 — the same job also strips a personnummer from the stored Title/Description. Its own
    // corpus (the counts above are pinned to the contact corpus). The legacy rows are a
    // RETIRED-ACTOR seed like the two seeded below: ads imported before #2098, including the
    // extracted_terms that Import derived from the unmasked text. The current writer can no longer
    // produce this shape; the complement is pinned in JobAdPersonnummerRedactionTests
    // (Import_masks_a_personnummer_in_title_and_description_in_both_spellings and
    // A_resync_carrying_a_personnummer_is_masked_too — plain text, this project does not
    // reference Domain.UnitTests).
    // ================================================================================

    private const string PersonnummerExternalId = "backfill-personnummer-4";
    private const string ZeroLeadingPersonnummerExternalId = "backfill-personnummer-zero-5";
    private const string Personnummer = "811218-9876";
    // Born 2005, so it starts with 0 and is phone-shaped to the contact detector.
    private const string ZeroLeadingPersonnummer = "050505-1235";

    [Fact]
    public async Task Destructive_run_masks_a_personnummer_in_title_and_description_and_the_search_vector_follows()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var seed = _provider.CreateScope())
        {
            var seedDb = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.JobAds.Add(ImportClean(CleanExternalId, new FixedClock(Now)));
            seedDb.JobAds.Add(ImportClean(PersonnummerExternalId, new FixedClock(Now)));
            seedDb.JobAds.Add(ImportClean(ZeroLeadingPersonnummerExternalId, new FixedClock(Now)));
            await seedDb.SaveChangesAsync(ct);

            await SeedLegacyPersonnummerAsync(seedDb, PersonnummerExternalId, Personnummer, ct);
            await SeedLegacyPersonnummerAsync(
                seedDb, ZeroLeadingPersonnummerExternalId, ZeroLeadingPersonnummer, ct);
        }

        // Counterfactual: the seeded state IS findable in the search index and sits in the derived
        // terms, so the 0s below prove the scrub and not a tokenizer that never indexed the number.
        using (var before = _provider.CreateScope())
        {
            var db = before.ServiceProvider.GetRequiredService<AppDbContext>();
            (await FtsHitsAsync(db, "811218", ct)).ShouldBe(1);
            (await ExtractedColumnsContainAsync(db, PersonnummerExternalId, "9876", ct)).ShouldBeTrue(
                "the legacy extracted_terms/extracted_lexemes carry the number's digit tokens.");
        }

        ContactScrubBackfillCounts counts;
        using (var scope = _provider.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>();
            counts = await job.RunAsync(dryRun: false, ct);
        }

        counts.Scrubbed.ShouldBe(2, "only the two personnummer rows carry a detection.");
        counts.ContactsPromoted.ShouldBe(0,
            "a personnummer is removed, never promoted into contacts as a phone.");

        using var check = _provider.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await checkDb.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == PersonnummerExternalId, ct);
        ad.Title.ShouldBe("Städare ******-****");
        ad.Description.ShouldBe("Enskild firma ******-**** söker städare.");
        (await FtsHitsAsync(checkDb, "811218", ct)).ShouldBe(0,
            "the stored search_vector regenerates from the masked text.");
        (await ExtractedColumnsContainAsync(checkDb, PersonnummerExternalId, "9876", ct)).ShouldBeFalse(
            "the job re-extracts from the masked text.");
        (await ExtractedColumnsContainAsync(checkDb, PersonnummerExternalId, "811218", ct)).ShouldBeFalse();

        var zeroLeading = await checkDb.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == ZeroLeadingPersonnummerExternalId, ct);
        zeroLeading.Description.ShouldBe("Enskild firma ******-**** söker städare.");
        (zeroLeading.Contacts is null || zeroLeading.Contacts.IsEmpty).ShouldBeTrue(
            "a 0-leading personnummer is phone-shaped; it must be masked, not moved into contacts.");

        // Fixed point: a re-run (or the run after a crash) touches nothing.
        using var rerun = _provider.CreateScope();
        var again = await rerun.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>()
            .RunAsync(dryRun: false, ct);
        again.Scrubbed.ShouldBe(0);
    }

    private const string CompanyNameCarrierExternalId = "backfill-company-name-6";
    private const string CompanyNameWithPersonnummer = "Svensson 811218 - 9876";

    [Fact]
    public async Task Destructive_run_masks_a_personnummer_whose_only_carrier_is_the_company_name_of_a_delisted_ad()
    {
        // #2123. The probe (JobAd.ApplyContactScrubBackfill) counts Company.Name beside Title and
        // Description, and this is the arm that reaches the de-listed tail: an ad that has left the
        // feed is never upserted again, so the nightly sync never re-masks its name.
        //
        // A RETIRED-ACTOR SEAM (CLAUDE.md §5 Tests:). The actor is "the importer before #2123", which
        // stored employer.name verbatim; its row is written by the raw UPDATE below. The complement,
        // that the CURRENT writer cannot produce the shape, is pinned in
        // JobAdPersonnummerRedactionTests.A_personnummer_in_the_company_name_is_masked_in_place_on_import
        // Title and description stay clean, so the name is the only detection in the table.
        var ct = TestContext.Current.CancellationToken;
        using (var seed = _provider.CreateScope())
        {
            var seedDb = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var delisted = ImportClean(CompanyNameCarrierExternalId, new FixedClock(Now));
            delisted.Archive(new FixedClock(Now)).IsSuccess.ShouldBeTrue();
            seedDb.JobAds.Add(delisted);
            await seedDb.SaveChangesAsync(ct);

            await seedDb.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE job_ads SET company_name = {CompanyNameWithPersonnummer}
                WHERE external_id = {CompanyNameCarrierExternalId}
                """, ct);
        }

        ContactScrubBackfillCounts dry;
        using (var scope = _provider.CreateScope())
        {
            dry = await scope.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>()
                .RunAsync(dryRun: true, ct);
        }

        dry.Scrubbed.ShouldBe(1, "the company name is the only detection, so the probe must count it.");
        using (var untouched = _provider.CreateScope())
        {
            (await CompanyNameAsync(untouched.ServiceProvider.GetRequiredService<AppDbContext>(),
                CompanyNameCarrierExternalId, ct)).ShouldBe(CompanyNameWithPersonnummer);
        }

        ContactScrubBackfillCounts live;
        using (var scope = _provider.CreateScope())
        {
            live = await scope.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>()
                .RunAsync(dryRun: false, ct);
        }

        live.Scrubbed.ShouldBe(1);
        using (var check = _provider.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            (await CompanyNameAsync(db, CompanyNameCarrierExternalId, ct)).ShouldBe("Svensson ****** - ****");
            (await ContactsIsNullAsync(db, CompanyNameCarrierExternalId, ct)).ShouldBeTrue(
                "an archived ad holds no contacts, and a personnummer is never promoted into one.");
        }

        using var rerun = _provider.CreateScope();
        (await rerun.ServiceProvider.GetRequiredService<BackfillRecruiterContactScrubJob>()
            .RunAsync(dryRun: false, ct)).Scrubbed.ShouldBe(0);
    }

    private static async Task<string?> CompanyNameAsync(
        AppDbContext db, string externalId, CancellationToken ct) =>
        (await db.Database
            .SqlQueryRaw<string?>(
                "SELECT company_name AS \"Value\" FROM job_ads WHERE external_id = {0}", externalId)
            .ToListAsync(ct)).Single();

    // The retired actor: Import derived extracted_terms from the UNMASKED text with the real
    // extractor, so the seed does the same.
    private async Task SeedLegacyPersonnummerAsync(
        AppDbContext db, string externalId, string personnummer, CancellationToken ct)
    {
        var title = $"Städare {personnummer}";
        var description = $"Enskild firma {personnummer} söker städare.";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE job_ads SET title = {title}, description = {description}
            WHERE external_id = {externalId}
            """, ct);

        db.ChangeTracker.Clear();
        var ad = await db.JobAds.SingleAsync(j => j.External!.ExternalId == externalId, ct);
        var extractor = _provider.GetRequiredService<IJobAdKeywordExtractor>();
        ad.SetExtractedTerms(ExtractedTerms.From(
            extractor.Extract(new JobAdExtractionInput(ad.Title, ad.Description)).Terms));
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> ExtractedColumnsContainAsync(
        AppDbContext db, string externalId, string needle, CancellationToken ct) =>
        (await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM job_ads WHERE external_id = {0} "
                + "AND (extracted_terms::text LIKE {1} OR extracted_lexemes::text LIKE {1})",
                externalId, $"%{needle}%")
            .ToListAsync(ct)).Single() > 0;

    private static async Task<int> FtsHitsAsync(AppDbContext db, string term, CancellationToken ct) =>
        (await db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM job_ads
                WHERE search_vector @@ websearch_to_tsquery('swedish'::regconfig, {term})
                """)
            .ToListAsync(ct)).Single();

    // ================================================================================
    // Seeding — the sanctioned hand-seeds (see the class remarks). Clean ads are imported
    // through the real aggregate; the two legacy rows are then dropped to pre-invariant state
    // (address in body/payload, contacts NULL) by raw SQL that bypasses the aggregate.
    // ================================================================================

    private async Task SeedLegacyCorpusAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        // Three clean imports through the real production write path.
        db.JobAds.Add(ImportClean(CleanExternalId, clock));
        var activeLegacy = ImportClean(ActiveLegacyExternalId, clock);
        // A pre-existing Requirement-kind term on the Active legacy ad (taxonomy data).
        activeLegacy.SetExtractedTerms(ExtractedTerms.From(
        [
            new ExtractedTerm(
                RequirementConceptId, "Testkrav", ExtractedTermKind.Requirement,
                ExtractedTermSource.MustHave, "Testkrav", RequirementConceptId, 1.0),
        ]));
        db.JobAds.Add(activeLegacy);
        db.JobAds.Add(ImportClean(ArchivedLegacyExternalId, clock));
        await db.SaveChangesAsync(ct);

        // Drop the two legacy rows to PRE-INVARIANT state: the address back in the body/payload,
        // contacts NULL — the shape a fresh Import can no longer produce (it always scrubs). The
        // Archived row also loses its Active status. Raw SQL, bypassing the aggregate: the actor is
        // "ads imported before #842 Tier A", and the complement — that the current writer cannot
        // produce this shape — is pinned in JobAdContactRedactionTests (see the class remarks).
        var legacyDescription =
            $"Kontakta {LegacyEmail} eller ring {LegacyPhone} för mer information om tjänsten.";
        // Plain concatenation, not a raw-string literal: the JSON's trailing "}}" collides with the
        // $$"""…""" interpolation delimiters.
        var legacyPayload =
            "{\"description\":{\"text\":\"Kontakta " + LegacyEmail + " eller ring " + LegacyPhone + "\"}}";

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE job_ads
            SET description = {legacyDescription}, raw_payload = {legacyPayload}::jsonb, contacts = NULL
            WHERE external_id = {ActiveLegacyExternalId}
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE job_ads
            SET description = {legacyDescription}, raw_payload = {legacyPayload}::jsonb,
                contacts = NULL, status = 'Archived'
            WHERE external_id = {ArchivedLegacyExternalId}
            """, ct);
    }

    private static JobAd ImportClean(string externalId, IDateTimeProvider clock)
    {
        var company = Company.Create("Legacy AB").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, externalId).Value;
        return JobAd.Import(
            "Backend-utvecklare", company, "Neutral annonstext utan kontaktspår.",
            "https://arbetsformedlingen.se/platsbanken/annonser/" + externalId,
            external, "{\"id\":\"" + externalId + "\"}", TestFacets.None,
            [],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), clock, applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    private static async Task<bool> DescriptionContainsAsync(
        AppDbContext db, string externalId, string needle, CancellationToken ct)
    {
        var hits = await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM job_ads "
                + "WHERE external_id = {0} AND lower(description) LIKE {1}",
                externalId, $"%{needle.ToLowerInvariant()}%")
            .ToListAsync(ct);
        return hits.Count > 0 && hits[0] > 0;
    }

    private static async Task<bool> ContactsIsNullAsync(
        AppDbContext db, string externalId, CancellationToken ct)
    {
        var value = (await db.Database
            .SqlQueryRaw<string?>(
                "SELECT contacts::text AS \"Value\" FROM job_ads WHERE external_id = {0}",
                externalId)
            .ToListAsync(ct)).Single();
        return value is null;
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
