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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #842 Tier A (re-bind R7/D10) — the one-off contact-scrub backfill
/// (<see cref="BackfillRecruiterContactScrubJob"/>), the local re-projection that reaches the
/// DE-LISTED tail the nightly sync never rewrites, against real Postgres (Testcontainers).
/// </summary>
/// <remarks>
/// <b>THE ONE SANCTIONED HAND-SEED (V20/#843).</b> Every other test in this suite builds state
/// through the production write path, because a test that constructs a state production cannot
/// construct proves nothing about production. This job is the exception the rule itself names: its
/// ENTIRE SUBJECT is the ~93k rows imported BEFORE the aggregate invariant landed — PRE-INVARIANT
/// rows the funnel can no longer create (a scrubbed body is a fixed point; a fresh Import always
/// scrubs). So the legacy state — a recruiter address sitting in <c>description</c>/<c>raw_payload</c>
/// with <c>contacts = NULL</c> — is seeded by a raw SQL UPDATE that bypasses the aggregate, exactly
/// as the task instruction sanctions for this job alone. The clean ad and the surrounding assertions
/// still go through the real aggregate and the real extractor.
/// </remarks>
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

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private ServiceProvider _provider = default!;
    private ISystemEventAuditor _auditor = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _auditor = Substitute.For<ISystemEventAuditor>();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
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

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
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
            Arg.Is<JobAdsSynced>(e => e.JobType == "backfill-contact-scrub"),
            Arg.Any<CancellationToken>());
    }

    // ================================================================================
    // Seeding — the ONE sanctioned hand-seed (see the class remarks). Clean ads are imported
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
        // Archived row also loses its Active status. Raw SQL, bypassing the aggregate — the one
        // sanctioned exception.
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
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), clock).Value;
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
