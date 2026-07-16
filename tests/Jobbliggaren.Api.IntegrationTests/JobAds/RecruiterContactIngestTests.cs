using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
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
using DomainApplicationId = Jobbliggaren.Domain.Applications.ApplicationId;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #842 Tier A (ADR 0106 PR2, CTO re-bind R1) — the recruiter-contact scrub and its erasure
/// machinery, proven END TO END through the production write path (V20/#843): WireMock serves
/// real JobTech JSON (declared <c>application_contacts</c> + free-text recruiter spans) → the REAL
/// <see cref="PlatsbankenJobSource"/> (hence the REAL <see cref="JobTechPayloadSanitizer"/>) → the
/// REAL <see cref="UpsertExternalJobAdCommandHandler"/> (hence the real aggregate scrub + real
/// keyword extractor) → REAL Postgres 18 with the real migrations, generated columns and GIN index.
/// Not one column is hand-seeded.
/// </summary>
/// <remarks>
/// This class is where the design's FTS locks <b>L2</b> (the address never enters
/// <c>extracted_terms</c>) and <b>L3</b> (the address is not FTS-findable) live — the arch test
/// <c>RecruiterContactFtsLockTests</c> defers to it by name. It also carries the DB-level erasure
/// pins Tier A creates: the <c>contacts</c>-ALONE channel, the <c>snapshot_contacts</c>-ALONE
/// surgical arm, the tombstone <c>contacts</c> clear, and the archived-ad write-gate.
/// </remarks>
public sealed class RecruiterContactIngestTests : IAsyncLifetime
{
    // ── Ad A — THE FUNNEL AD (L2+L3, F-A). A declared contact whose identifier lives ONLY in the
    //    contacts column, PLUS a body carrying an åäö email and an NBSP-separated phone (the
    //    relaxed-escaping fix): the body spans are scrubbed, migrated to the carrier, and survive a
    //    resync unchanged. ─────────────────────────────────────────────────────────────────────
    private const string FunnelExternalId = "funnel-1";
    private const string DeclaredName = "Cornelia Björk";
    private const string DeclaredEmail = "cornelia.bjork@bemanning.example";
    private const string DeclaredPhone = "+46 70 987 65 43";

    // The body email uses å/ö in BOTH local part and domain, the phone is NBSP-separated — the two
    // shapes the payload's relaxed-escaping fix exists for. NBSP is written \u00A0 in source (a
    // literal invisible character is banned); a regular string literal decodes the escape.
    private const string BodyEmail = "hråkan@östberg.example";
    private const string BodyPhoneNbsp = "073\u00A0042\u00A090\u00A030";
    private const string BodyPhoneDigits = "0730429030";

    private static readonly string FunnelBody =
        $"Vi söker en backend-utvecklare till vårt team. För frågor om tjänsten, kontakta oss på "
        + $"{BodyEmail} eller ring {BodyPhoneNbsp}.";

    // ── Ad B — the contacts-ALONE erasure pin: identifier lives ONLY in the declared contact
    //    (dropped from raw_payload by the sanitizer, absent from title/description/company). ─────
    private const string ContactsAloneExternalId = "contacts-alone-2";
    private const string ContactsAloneName = "Gudrun Silfverberg";
    private const string ContactsAloneEmail = "gudrun.silfverberg@rekryt.example";

    // ── Ad C — the snapshot_contacts-ALONE pin. Its identifier in the TITLE is orthogonal to its
    //    declared contact, so erasing the ad by the title name leaves the frozen contact standing
    //    in applications.snapshot_contacts as its sole surviving carrier. ─────────────────────────
    private const string SnapshotSrcExternalId = "snapshot-src-3";
    private const string SnapshotTitleName = "Wilhelmina Åkerström";
    private const string SnapshotContactName = "Sixten Palmgren";
    private const string SnapshotContactEmail = "sixten.palmgren@byra.example";

    // ── Ad D — the tombstone contacts-clear counterfactual. ──────────────────────────────────────
    private const string TombstoneExternalId = "tombstone-4";
    private const string TombstoneContactEmail = "torsten.wikman@konsult.example";

    // ── Ad E — the archived-ad write-gate (b1 §1). A declared contact AND a body contact, so the
    //    resync must NOT repopulate either onto a non-Active ad. ──────────────────────────────────
    private const string WriteGateExternalId = "writegate-5";
    private const string WriteGateDeclaredEmail = "elvira.sandell@drift.example";
    private const string WriteGateBodyEmail = "brev@drift.example";

    private static readonly string WriteGateBody =
        $"Vi söker en DevOps-ingenjör. Skicka din ansökan till {WriteGateBodyEmail}.";

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

    /// <summary>The JobTech wire shape, v2, with the top-level <c>application_contacts</c> array.</summary>
    private static string SnapshotJson() =>
        $$"""
        [{
          "id": "{{FunnelExternalId}}",
          "headline": "Backend-utvecklare",
          "description": { "text": "{{FunnelBody}}" },
          "employer": { "name": "Techbolaget AB", "organization_number": "5561111111" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{FunnelExternalId}}",
          "publication_date": "2026-07-01T10:00:00Z",
          "application_contacts": [
            { "name": "{{DeclaredName}}", "description": "Rekryterare", "email": "{{DeclaredEmail}}", "telephone": "{{DeclaredPhone}}", "contact_type": null }
          ]
        },{
          "id": "{{ContactsAloneExternalId}}",
          "headline": "Frontend-utvecklare",
          "description": { "text": "Neutral annonstext utan kontaktspår i brödtexten." },
          "employer": { "name": "Webbyrån AB", "organization_number": "5562222222" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{ContactsAloneExternalId}}",
          "publication_date": "2026-07-02T10:00:00Z",
          "application_contacts": [
            { "name": "{{ContactsAloneName}}", "description": "HR", "email": "{{ContactsAloneEmail}}", "telephone": "08-123 45 67", "contact_type": null }
          ]
        },{
          "id": "{{SnapshotSrcExternalId}}",
          "headline": "Rekryterare {{SnapshotTitleName}}",
          "description": { "text": "Neutral annonstext." },
          "employer": { "name": "Bemanning Nord AB", "organization_number": "5563333333" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{SnapshotSrcExternalId}}",
          "publication_date": "2026-07-03T10:00:00Z",
          "application_contacts": [
            { "name": "{{SnapshotContactName}}", "description": "Kontaktperson", "email": "{{SnapshotContactEmail}}", "telephone": "070-222 33 44", "contact_type": null }
          ]
        },{
          "id": "{{TombstoneExternalId}}",
          "headline": "Systemarkitekt",
          "description": { "text": "Neutral annonstext för en arkitektroll." },
          "employer": { "name": "Arkitektbolaget AB", "organization_number": "5564444444" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{TombstoneExternalId}}",
          "publication_date": "2026-07-04T10:00:00Z",
          "application_contacts": [
            { "name": "Torsten Wikman", "description": "Rekryterare", "email": "{{TombstoneContactEmail}}", "telephone": "073-999 88 77", "contact_type": null }
          ]
        },{
          "id": "{{WriteGateExternalId}}",
          "headline": "DevOps-ingenjör",
          "description": { "text": "{{WriteGateBody}}" },
          "employer": { "name": "Drift AB", "organization_number": "5565555555" },
          "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{WriteGateExternalId}}",
          "publication_date": "2026-07-05T10:00:00Z",
          "application_contacts": [
            { "name": "Elvira Sandell", "description": "Rekryterare", "email": "{{WriteGateDeclaredEmail}}", "telephone": "08-987 65 43", "contact_type": null }
          ]
        }]
        """;

    // ================================================================================
    // The production write path. NOTHING is hand-seeded (V20/#843).
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

    // ================================================================================
    // 1. THE FUNNEL TEST — L2 + L3, the promote→scrub invariant, idempotence and the
    //    relaxed-escaping fix, all through the real funnel against real Postgres.
    // ================================================================================

    [Fact]
    public async Task The_funnel_scrubs_the_body_migrates_to_the_carrier_and_a_resync_does_not_restore_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ad = await db.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == FunnelExternalId, ct);

        // (a) description holds the marker and NOT the address.
        ad.Description.ShouldContain(RecruiterContactRedactor.Marker,
            customMessage: "the detected span must be replaced by the marker.");
        ad.Description.ShouldNotContain("östberg");
        ad.Description.ShouldNotContain("hråkan");

        // (a)+(c)+(g) as ONE outcome: the åäö address survives in the row's `contacts` column and
        // NOWHERE else — not description (a), not extracted_terms (L2), not raw_payload (the
        // relaxed-escaping fix, g). ColumnsStillContainingAsync enumerates every text/jsonb column
        // from information_schema, so a column that leaks it tomorrow is caught without editing this
        // test.
        (await ColumnsStillContainingAsync(db, "östberg", ct)).ShouldBe(["contacts"],
            "the migrated address must live ONLY in the bounded, un-indexed carrier — a leak into "
            + "description/extracted_terms/raw_payload is the whole exposure Tier A closes.");
        // The full declared email (unique across the corpus — a bare domain token would collide
        // with another ad's company name, and ColumnsStillContainingAsync scans every row).
        (await ColumnsStillContainingAsync(db, DeclaredEmail, ct)).ShouldBe(["contacts"],
            "the DECLARED contact lives only in the carrier (the sanitizer drops "
            + "application_contacts from raw_payload; it is never in the body).");

        // (b) L3 — the address is not FTS-findable (search_vector is built from the scrubbed body).
        (await FtsHitsAsync(db, "östberg", ct)).ShouldBe(0,
            "L3 (#842): the scrubbed body means the address never enters the reverse-lookup index.");

        // (g) — the NBSP-separated phone AND the åäö email are handled by the relaxed-escaping path.
        // The email is scrubbed everywhere including raw_payload (proved by the offenders ==
        // ["contacts"] checks above: raw_payload does NOT carry it). The NBSP phone is scrubbed
        // from the DESCRIPTION (a raw string with a literal NBSP) and migrated to the carrier:
        ad.Description.Contains(BodyPhoneNbsp, StringComparison.Ordinal).ShouldBeFalse(
            "the NBSP-separated phone must be scrubbed from the description body.");

        // GAP CLOSED (2026-07-16, same session): the redactor's DetectionShadow now reads the
        // six-character LITERAL escape form of NBSP too (every stock JavaScriptEncoder still
        // escapes U+00A0 - measured), so the payload copy is scrubbed as well. These assertions
        // are the flip lines the KNOWN GAP marker promised: no fragment of the NBSP phone may
        // survive in raw_payload in any spelling.
        var payloadAfterScrub = (await ColumnValueAsync(db, FunnelExternalId, "raw_payload", ct))!;
        payloadAfterScrub.Contains(BodyPhoneNbsp, StringComparison.Ordinal).ShouldBeFalse(
            "the NBSP-separated phone must not survive in raw_payload (real-character form).");
        payloadAfterScrub.Contains("073", StringComparison.Ordinal).ShouldBeFalse(
            "no digit fragment of the NBSP phone may survive the payload scrub in any spelling.");

        // (d) the carrier holds the DECLARED entry (name preserved, Origin=Declared) and the two
        // uncovered body hits as ExtractedFromBody (name null — no NER).
        ad.Contacts.ShouldNotBeNull();
        var declared = ad.Contacts!.Contacts.Where(c => c.Origin == AdContactOrigin.Declared).ToList();
        declared.Count.ShouldBe(1);
        declared[0].Name.ShouldBe(DeclaredName);
        declared[0].Email.ShouldBe(DeclaredEmail);

        var promoted = ad.Contacts.Contacts.Where(c => c.Origin == AdContactOrigin.ExtractedFromBody).ToList();
        promoted.ShouldContain(c => c.NormalizedEmail == BodyEmail,
            "the body email is migrated into the carrier as an ExtractedFromBody hit.");
        promoted.ShouldContain(c => c.NormalizedPhone == BodyPhoneDigits,
            "the NBSP phone is detected and migrated (normalized to digits).");
        promoted.ShouldAllBe(c => c.Name == null, "a promoted body hit never guesses a name (no NER).");

        // (e) the scrubbed raw_payload is still valid JSON (the marker carries no JSON-structural
        // character), and it carries the marker in place of the address.
        var rawPayload = await ColumnValueAsync(db, FunnelExternalId, "raw_payload", ct);
        rawPayload.ShouldNotBeNull();
        Should.NotThrow(() => JsonDocument.Parse(rawPayload!),
            "the scrubbed raw_payload must remain parseable jsonb.");
        rawPayload!.ShouldContain(RecruiterContactRedactor.Marker);

        // (f) IDEMPOTENCE — a resync through the same funnel (the 02:00 sync) does not restore the
        // address and leaves the carrier sequence-equal (AdContacts.Equals is SequenceEqual).
        var contactsBefore = ad.Contacts;
        await IngestThroughProductionPathAsync(ct);

        using var after = _provider.CreateScope();
        var db2 = after.ServiceProvider.GetRequiredService<AppDbContext>();
        var reingested = await db2.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == FunnelExternalId, ct);

        (await ColumnsStillContainingAsync(db2, "östberg", ct)).ShouldBe(["contacts"],
            "the resync must not resurrect the address into the body/payload.");
        reingested.Contacts.ShouldBe(contactsBefore,
            "the carrier is idempotent — a nightly re-ingest of the same ad yields a sequence-equal "
            + "value, so the column never churns (AdContacts.From dedup+sort).");
    }

    // ================================================================================
    // 2. THE contacts-ALONE ERASURE PIN — the load-bearing Tier-A channel.
    //    Post-scrub, a detected/declared identifier lives ONLY in job_ads.contacts, so
    //    without the contacts arm the erasure command is vacuous for exactly this data.
    // ================================================================================

    [Fact]
    public async Task An_ad_whose_identifier_lives_ONLY_in_contacts_is_found_via_the_ContactsMatch_channel()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Precondition: the declared contact's identifier survives in contacts ALONE — proof that
        // this pin is about the contacts arm and nothing else.
        (await ColumnsStillContainingAsync(db, ContactsAloneEmail, ct)).ShouldBe(["contacts"],
            "the declared identifier is dropped from raw_payload by the sanitizer and absent from "
            + "title/description/company — contacts is its sole carrier.");

        var probe = await EraseAsync(ContactsAloneEmail, ct, dryRun: true);

        probe.Matched.JobAds.ShouldBe(1,
            "delete the `lower(contacts::text) LIKE` arm from FindJobAdsAsync and this is 0 — the "
            + "erasure becomes vacuous for the exact data Tier A just moved.");

        var match = probe.Matches.Single();
        match.MatchedChannel.ShouldBe(ErasureMatchChannel.ContactsMatch,
            "the load-bearing Tier-A channel gets its OWN reviewable label (T8), never the vague "
            + "FullTextOrRawPayload bucket.");
        match.MatchedExcerpt.ShouldContain(ContactsAloneEmail,
            customMessage: "the excerpt windows the matched contact's own fields — real evidence for "
            + "the one human gate before irreversible destruction.");
        match.MatchedExcerpt.ShouldContain(ContactsAloneName);
    }

    // ================================================================================
    // 3. THE snapshot_contacts-ALONE PIN + the surgical arm.
    //    Apply to an ingested ad (snapshot freezes the contacts), erase the ad whole-record
    //    by an ORTHOGONAL identifier so the frozen contact survives ONLY in
    //    applications.snapshot_contacts, then a new request matches it and clears it SURGICALLY.
    // ================================================================================

    [Fact]
    public async Task A_frozen_snapshot_contact_is_matched_on_snapshot_contacts_ALONE_and_erased_surgically()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        var applicationId = await SeedApplicationCapturingContactsAsync(SnapshotSrcExternalId, ct);

        // Step 1 — erase the AD whole-record by its TITLE name (orthogonal to the frozen contact),
        // so nothing of the recruiter Sixten is touched by it: the ad tombstones, the frozen
        // snapshot_contacts (Sixten) stays.
        await EraseAsync(SnapshotTitleName, ct);

        using (var mid = _provider.CreateScope())
        {
            var db = mid.ServiceProvider.GetRequiredService<AppDbContext>();

            (await SnapshotContactsMatchCountAsync(db, SnapshotContactEmail, ct)).ShouldBe(1,
                "counterfactual: the frozen contact must STILL be in snapshot_contacts after the "
                + "whole-record ad erase — otherwise step 3 proves nothing.");
            (await ColumnsStillContainingAsync(db, SnapshotContactEmail, ct)).ShouldBeEmpty(
                "and it is gone from the tombstoned ad row — the snapshot is now its sole carrier.");
        }

        // Step 3 — a NEW request by the frozen contact's email matches ONLY snapshot_contacts.
        var probe = await EraseAsync(SnapshotContactEmail, ct, dryRun: true);
        probe.Matched.ApplicationSnapshotContacts.ShouldBe(1,
            "the frozen contact is reachable only through its own surface (Ground 1, T2): the ad no "
            + "longer matches, so without this channel a false Art. 12(3) confirmation is sent.");
        probe.Matched.JobAds.ShouldBe(0, "the carrier ad is a tombstone.");

        var response = await EraseAsync(SnapshotContactEmail, ct);
        response.Erased.ApplicationSnapshotContacts.ShouldBe(1,
            "the surgical arm (Application.EraseAdSnapshotContacts) clears exactly the requester's "
            + "own frozen contact block.");

        using var check = _provider.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();

        // snapshot_contacts is now NULL; the applicant's own record (title/company/description) is
        // untouched — the proportionality win the whole re-bind was for.
        var appId = applicationId.Value;
        var snapContacts = (await checkDb.Database.SqlQueryRaw<string?>(
            "SELECT snapshot_contacts::text AS \"Value\" FROM applications WHERE id = {0}", appId)
            .ToListAsync(ct)).Single();
        var snapTitle = (await checkDb.Database.SqlQueryRaw<string?>(
            "SELECT snapshot_title AS \"Value\" FROM applications WHERE id = {0}", appId)
            .ToListAsync(ct)).Single();
        var snapCompany = (await checkDb.Database.SqlQueryRaw<string?>(
            "SELECT snapshot_company AS \"Value\" FROM applications WHERE id = {0}", appId)
            .ToListAsync(ct)).Single();
        var snapDescription = (await checkDb.Database.SqlQueryRaw<string?>(
            "SELECT snapshot_description AS \"Value\" FROM applications WHERE id = {0}", appId)
            .ToListAsync(ct)).Single();

        snapContacts.ShouldBeNull("snapshot_contacts is cleared.");
        snapTitle.ShouldBe($"Rekryterare {SnapshotTitleName}", "snapshot_title is retained (17(3)(e)).");
        snapCompany.ShouldBe("Bemanning Nord AB", "snapshot_company is retained.");
        snapDescription.ShouldNotBeNullOrEmpty("snapshot_description is retained.");
    }

    // ================================================================================
    // 5. THE TOMBSTONE — Erase() clears job_ads.contacts in the DB, with the counterfactual.
    //
    //    The existing registry-derived tombstone-shape test (RecruiterErasureIngestTests,
    //    ErasedTombstoneShape) does NOT yet list `contacts`, so it does not auto-cover this
    //    column — see the test-writer report. This is the focused pin, WITH the "was non-null
    //    before" counterfactual an absence proof needs.
    // ================================================================================

    [Fact]
    public async Task Erase_clears_job_ads_contacts_in_the_database()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using (var before = _provider.CreateScope())
        {
            var db = before.ServiceProvider.GetRequiredService<AppDbContext>();
            var ad = await db.JobAds.AsNoTracking()
                .SingleAsync(j => j.External!.ExternalId == TombstoneExternalId, ct);
            ad.Contacts.ShouldNotBeNull("counterfactual: the ad HELD a contact before the erase — "
                + "absence proves a gate only against a prior presence.");
            ad.Contacts!.IsEmpty.ShouldBeFalse();
        }

        await EraseAsync(TombstoneContactEmail, ct);

        using var scope = _provider.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contactsColumn = (await check.Database
            .SqlQuery<string?>($"""
                SELECT contacts::text AS "Value" FROM job_ads WHERE external_id = {TombstoneExternalId}
                """)
            .ToListAsync(ct)).Single();

        contactsColumn.ShouldBeNull(
            "Erase() must set job_ads.contacts to NULL explicitly — it is classified Erased in the "
            + "registry and the Art. 12(3) reply tells the data subject it was destroyed.");
        (await ColumnsStillContainingAsync(check, TombstoneContactEmail, ct)).ShouldBeEmpty(
            "and the identifier survives in no column of the tombstoned row.");
    }

    // ================================================================================
    // 6. THE ARCHIVED-AD WRITE-GATE (b1 §1) — the failure scenario that killed every naive fix.
    //    Ingest → archive → re-upsert the same still-listed ad through the real funnel. The body
    //    is re-scrubbed, but contacts stays NULL because the ad is no longer Active: the nightly
    //    sync can never repopulate what retention cleared.
    // ================================================================================

    [Fact]
    public async Task A_resync_of_an_archived_ad_re_scrubs_the_body_but_never_repopulates_contacts()
    {
        var ct = TestContext.Current.CancellationToken;
        await IngestThroughProductionPathAsync(ct);

        using (var seeded = _provider.CreateScope())
        {
            var db = seeded.ServiceProvider.GetRequiredService<AppDbContext>();
            var ad = await db.JobAds.AsNoTracking()
                .SingleAsync(j => j.External!.ExternalId == WriteGateExternalId, ct);
            ad.Status.ShouldBe(JobAdStatus.Active);
            ad.Contacts.ShouldNotBeNull("the Active ad holds contacts (declared + promoted body hit).");
            ad.Contacts!.IsEmpty.ShouldBeFalse();
        }

        // Archive through the real command path (JobAd.Archive clears contacts — retention).
        using (var archiveScope = _provider.CreateScope())
        {
            var db = archiveScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = new ArchiveExternalJobAdCommandHandler(
                db, new FixedClock(), NullLogger<ArchiveExternalJobAdCommandHandler>.Instance);

            var result = await handler.Handle(
                new ArchiveExternalJobAdCommand(JobSource.Platsbanken, WriteGateExternalId), ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(ArchiveOutcome.Archived);
            await db.SaveChangesAsync(ct);
        }

        // The 02:00 sync: Arbetsförmedlingen still serves the ad, contact block and body address
        // and all. This is the exact b1 §1 scenario.
        await IngestThroughProductionPathAsync(ct);

        using var scope = _provider.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await check.JobAds.AsNoTracking()
            .SingleAsync(j => j.External!.ExternalId == WriteGateExternalId, ct);

        reloaded.Status.ShouldBe(JobAdStatus.Archived, "archival is not undone by the resync.");
        reloaded.Description.ShouldContain(RecruiterContactRedactor.Marker,
            customMessage: "the body is STILL scrubbed on every rewrite — the scrub runs in all statuses.");

        var contactsColumn = (await check.Database
            .SqlQuery<string?>($"""
                SELECT contacts::text AS "Value" FROM job_ads WHERE external_id = {WriteGateExternalId}
                """)
            .ToListAsync(ct)).Single();
        contactsColumn.ShouldBeNull(
            "contacts stays NULL: the write-gate populates contacts only while Active, so a "
            + "still-listed archived ad's nightly rewrite can never restore what retention cleared.");

        (await ColumnsStillContainingAsync(check, WriteGateDeclaredEmail, ct)).ShouldBeEmpty(
            "the declared contact is not re-frozen onto the archived ad.");
        (await ColumnsStillContainingAsync(check, WriteGateBodyEmail, ct)).ShouldBeEmpty(
            "and the body address is neither in the body (scrubbed) nor promoted back into contacts.");
    }

    // ================================================================================
    // Helpers — mirrored from RecruiterErasureIngestTests, the house harness precedent.
    // ================================================================================

    /// <summary>
    /// The OUTCOME assertion: which text/jsonb columns of the row still contain the needle,
    /// enumerated from <c>information_schema</c> so a column added tomorrow is covered.
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
                ORDER BY column_name
                """)
            .ToListAsync(ct);

        var offenders = new List<string>();
        foreach (var column in columns)
        {
            var sql = "SELECT count(*)::int AS \"Value\" FROM job_ads "
                + $"WHERE \"{column}\"::text ILIKE {{0}}";
            var hits = await db.Database.SqlQueryRaw<int>(sql, $"%{needle}%").ToListAsync(ct);
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

    private static async Task<string?> ColumnValueAsync(
        AppDbContext db, string externalId, string column, CancellationToken ct)
    {
        var sql = $"SELECT \"{column}\"::text AS \"Value\" FROM job_ads WHERE external_id = {{0}}";
        return (await db.Database.SqlQueryRaw<string?>(sql, externalId).ToListAsync(ct)).Single();
    }

    private static async Task<int> SnapshotContactsMatchCountAsync(
        AppDbContext db, string needle, CancellationToken ct)
    {
        var counts = await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM applications "
                + "WHERE snapshot_contacts IS NOT NULL AND lower(snapshot_contacts::text) LIKE {0}",
                $"%{needle.ToLowerInvariant()}%")
            .ToListAsync(ct);
        return counts.Count > 0 ? counts[0] : 0;
    }

    /// <summary>
    /// An application whose frozen <see cref="AdSnapshot"/> captures the ad's REAL contacts —
    /// exactly what <c>CreateApplicationFromJobAdCommandHandler</c> does (project j.Contacts →
    /// AdSnapshot.Capture(..., contacts) → CreateFromJobAd → submit). No hand-written columns (#843).
    /// </summary>
    private async Task<DomainApplicationId> SeedApplicationCapturingContactsAsync(
        string externalId, CancellationToken ct)
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
            ad.Description,
            ad.Contacts, // the post-scrub contacts, frozen — the handler's exact projection (T7)
            clock.UtcNow);

        var application = DomainApplication
            .CreateFromJobAd(seeker.Id, ad.Id, snapshot, coverLetter: null, clock).Value;
        application.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();

        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        return application.Id;
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
            var probe = await handler.Handle(
                new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, DryRun: true, null), ct);
            confirmed = [.. probe.Value.Matches.Select(m => m.JobAdId)];
        }

        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, dryRun, confirmed), ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);

        await db.SaveChangesAsync(ct);
        return result.Value;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    }
}
