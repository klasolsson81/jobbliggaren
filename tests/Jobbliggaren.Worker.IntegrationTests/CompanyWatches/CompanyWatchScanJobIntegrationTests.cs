using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — Testcontainers integration tests for <see cref="CompanyWatchScanJob"/>
/// against REAL Postgres. NEVER EF-InMemory: the org.nr membership matches the STORED generated
/// <c>organization_number</c> column (<c>raw_payload-&gt;'employer'-&gt;&gt;'organization_number'</c>) via
/// <c>EF.Property + IN</c> — only Postgres computes that column (hidden by InMemory).
/// <para>
/// <b>7C (bevakning-reconcile RF-7, 2026-07-12 — Klas-ratified; DPIA Part E §E8(1)(c)):</b> the scan
/// creates hits for EVERY user with ≥1 ACTIVE follow with NO consent predicate — hit creation is the
/// requested SERVICE (GDPR Art. 6(1)(b)); the email opt-in
/// (<c>FollowedCompanyNotificationsEnabled</c>, Art. 6(1)(a)) gates only the DISPATCH pass. The
/// <see cref="FollowConsent"/> seed knob therefore does NOT change the number of hits created here —
/// it exists to prove that invariant (see
/// <see cref="RunAsync_CreatesHitsForAllActiveFollowers_RegardlessOfEmailConsent"/>).
/// </para>
/// <para>
/// The job is CONSTRUCTED DIRECTLY (<c>new CompanyWatchScanJob(...)</c>, parity
/// <c>BackgroundMatchingJobIntegrationTests</c>). An injected <see cref="FixedClock"/> makes the
/// cold-start floor + watermark deterministic.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchScanJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 3, 25, 0, TimeSpan.Zero);

    // RF-3 ort-filter concept-ids (JobTech municipality concept-ids). Stable literals are safe here:
    // the scan only considers ads whose org.nr is WATCHED, and each test uses a UNIQUE org.nr, so ort
    // ids never cross-contaminate between tests (unlike org.nr, which must be unique per test).
    private const string KommunA = "kommun-a-cwscan";
    private const string KommunB = "kommun-b-cwscan";

    // F4a (#803) — LÄN concept-ids. A DISJOINT namespace from the kommun ids above: the two geo axes
    // are unioned, never merged, and an ad may carry a län with NO kommun. KommunA lies in LanA.
    private const string LanA = "lan-a-cwscan";
    private const string LanB = "lan-b-cwscan";

    // Per-test-unique org.nrs (the [Collection] shares one Postgres; the scan has no filter knob and
    // matches EVERY active ad whose org.nr is watched, so tests must not cross-contaminate).
    private static string UniqueLegalOrgNr() =>
        // 10 digits, third digit ≥ 2 → a legal-entity org.nr (NOT personnummer-shaped). Random-ish
        // but deterministic per call via a fresh Guid hash mapped into the 55xxxxxxxx space.
        "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task RunAsync_PersistsFollowHit_AndAdvancesWatermark_WhenConsentingUserFollowsMatchingAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        var watchId = await SeedWatchAsync(userId, orgNr, ct);
        var jobAdId = await SeedAdWithOrgNrAsync(orgNr, "Acme Bygg AB", ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        var hit = hits.ShouldHaveSingleItem();
        hit.JobAdId.ShouldBe(jobAdId);
        hit.CompanyWatchId.ShouldBe(watchId);
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);

        var seeker = await GetSeekerAsync(userId, ct);
        seeker.LastCompanyWatchScanAt.ShouldBe(Now, "the watermark advances to the scan instant");
    }

    [Fact]
    public async Task RunAsync_DoesNotPersistHit_ForUnwatchedOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        var watchedOrgNr = UniqueLegalOrgNr();
        var otherOrgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, watchedOrgNr, ct);
        // An ad from an employer the user does NOT follow.
        await SeedAdWithOrgNrAsync(otherOrgNr, "Other AB", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty(
            "an ad whose org.nr is not watched must not produce a follow hit");
    }

    [Fact]
    public async Task RunAsync_CreatesHitsForAllActiveFollowers_RegardlessOfEmailConsent()
    {
        // 7C (bevakning-reconcile RF-7, 2026-07-12 — Klas-ratified; DPIA Part E §E8(1)(c), the
        // hit-creation side): creating a hit is part of the SERVICE the user requested by following the
        // company (GDPR Art. 6(1)(b)), so the scan persists a hit for EVERY active follower with NO
        // consent predicate. The email opt-in (FollowedCompanyNotificationsEnabled, Art. 6(1)(a)) gates
        // ONLY the DigestDispatchJob email pass — never hit creation here. This INVERTS the pre-7C
        // assertion (email consent OFF/withdrawn → no hit) that this test used to make: the scan-time
        // consent gate (ADR 0087 D5) is superseded (explicit supersession #2).
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();

        var (offUserId, _) = await SeedUserAsync(FollowConsent.Off, ct);
        await SeedWatchAsync(offUserId, orgNr, ct);
        var (withdrawnUserId, _) = await SeedUserAsync(FollowConsent.Withdrawn, ct);
        await SeedWatchAsync(withdrawnUserId, orgNr, ct);
        var (onUserId, _) = await SeedUserAsync(FollowConsent.On, ct);
        await SeedWatchAsync(onUserId, orgNr, ct);

        await SeedAdWithOrgNrAsync(orgNr, "Shared AB", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(offUserId, ct)).Count.ShouldBe(1,
            "e-post-consent OFF → hit skapas ändå (7C: hit-skapande är 6(1)(b)-tjänsten, " +
            "consent grindar bara e-postkanalen vid dispatch)");
        (await GetHitsAsync(withdrawnUserId, ct)).Count.ShouldBe(1,
            "återkallad e-post-consent → hit skapas ändå (in-app-hit-skapandet är oberoende av " +
            "e-postkanalens samtycke — E8(1)(c))");
        (await GetHitsAsync(onUserId, ct)).Count.ShouldBe(1, "e-post-consent ON → hit skapas");
    }

    [Fact]
    public async Task RunAsync_RunTwice_DoesNotDuplicateHits_NoDbUpdateException()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, orgNr, ct);
        await SeedAdWithOrgNrAsync(orgNr, "Acme AB", ct);

        await RunJobAsync(ct);
        // Second run against the SAME watermark-advanced state — the dedup UNIQUE + watermark make it
        // a clean no-op (never throws, never a second row).
        await Should.NotThrowAsync(async () => await RunJobAsync(ct));

        (await GetHitsAsync(userId, ct)).Count.ShouldBe(1, "the dedup spine prevents a duplicate hit");
    }

    [Fact]
    public async Task RunAsync_DoesNotScan_UserWithNoActiveFollows()
    {
        // 7C (bevakning-reconcile RF-7 / architect D1, 2026-07-12): the scan's due-set is
        // db.CompanyWatches.Select(w => w.UserId).Distinct() — EVERY user with ≥1 ACTIVE follow. A user
        // with NO active follows is NOT in the due-set → never scanned → the watermark stays NULL (their
        // FIRST follow later gets the cold-start floor, never a post-hoc backfill). This REPURPOSES the
        // pre-7C test, which asserted a followless user's watermark still advanced (that scan-everyone
        // due-set is gone: the DISTINCT-UserId set only contains users that own an active watch).
        var ct = TestContext.Current.CancellationToken;
        var (userId, _) = await SeedConsentingUserAsync(ct);
        // No CompanyWatch seeded → the user is absent from the DISTINCT-UserId due-set.

        await RunJobAsync(ct);

        var seeker = await GetSeekerAsync(userId, ct);
        seeker.LastCompanyWatchScanAt.ShouldBeNull(
            "en användare utan aktiva follows ligger utanför due-set:et (scannas aldrig) → " +
            "vattenmärket förblir null");
        (await GetHitsAsync(userId, ct)).ShouldBeEmpty("ingen scan → ingen hit");
    }

    [Fact]
    public async Task RunAsync_ExcludesSoftDeletedUnfollowedWatch()
    {
        // A watch the user UNFOLLOWED (soft-deleted) must NOT produce a hit even if a new ad from that
        // org.nr appears — the scan relies on the CompanyWatch soft-delete query filter. Regression
        // trap: a future IgnoreQueryFilters() slip would silently re-notify from an unfollowed employer.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedUnfollowedWatchAsync(userId, orgNr, ct);
        await SeedAdWithOrgNrAsync(orgNr, "Ex-Followed AB", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty(
            "an unfollowed (soft-deleted) watch must not produce a follow hit");
    }

    [Fact]
    public async Task RunAsync_ScansPersonnummerShapedOrgNr_LikeAnyOtherWatch()
    {
        // D8: the personnummer guard lives at the SURFACING/LOG boundary, NOT at scan membership. A
        // followed enskild-firma org.nr (personnummer-shaped, third digit < 2) must scan and hit
        // normally — the guard must not accidentally block a legitimate follow.
        var ct = TestContext.Current.CancellationToken;
        var pnrShapedOrgNr = UniquePersonnummerShapedOrgNr();
        OrganizationNumber.FromTrusted(pnrShapedOrgNr).IsPersonnummerShaped().ShouldBeTrue(
            "test fixture must actually be personnummer-shaped");
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, pnrShapedOrgNr, ct);
        await SeedAdWithOrgNrAsync(pnrShapedOrgNr, "Enskild Firma Karlsson", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).Count.ShouldBe(1,
            "a personnummer-shaped (enskild firma) org.nr is scanned like any other watch");
    }

    [Fact]
    public async Task RunAsync_ColdStart_ExcludesAdOlderThanFloor_IncludesAdWithinFloor()
    {
        // A never-scanned user (null watermark) seeds a 7-day ColdStartDays floor: an ad ingested
        // before the floor is NOT a hit; one within it IS. Pins the since = now.AddDays(-ColdStartDays)
        // branch at the boundary.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, orgNr, ct);
        var oldAdId = await SeedAdWithOrgNrAtAsync(orgNr, "Old AB", Now.AddDays(-8), ct);
        var freshAdId = await SeedAdWithOrgNrAtAsync(orgNr, "Fresh AB", Now.AddDays(-3), ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Select(h => h.JobAdId).ShouldContain(freshAdId, "an ad within the 7-day cold-start floor hits");
        hits.Select(h => h.JobAdId).ShouldNotContain(oldAdId, "an ad older than the cold-start floor is excluded");
    }

    // ─────────────────────────── RF-3 per-watch ort filter (bevaknings-reconcile PR-F1)

    [Fact]
    public async Task RunAsync_MunicipalityFilter_AdmitsOnlyAdsInFilteredMunicipality()
    {
        // RF-3=3D (scan-time) / RF-8=8A (never-created): a watch narrowed to the KOMMUN axis alone
        // produces a hit ONLY for an ad in KommunA. The KommunB ad is rejected even though it carries a
        // län (LanB) — the union widens across AXES, never across VALUES, so an unpicked län is no
        // backdoor. The län-only ad (municipality NULL, region LanA) is likewise rejected: the user
        // picked kommuner, and there is no region axis on this spec to admit it (8A). A filtered-out ad
        // produces NO hit row (the geo check is enforced scan-side, per (ad, watch) pair).
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [KommunA], [], ct);
        var adInA = await SeedAdWithOrgNrAndLocationAsync(orgNr, "A Bygg AB", KommunA, LanA, ct);
        await SeedAdWithOrgNrAndLocationAsync(orgNr, "B Bygg AB", KommunB, LanB, ct);
        await SeedAdWithOrgNrAndLocationAsync(orgNr, "Län Bygg AB", null, LanA, ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Select(h => h.JobAdId).ShouldBe(
            [adInA],
            "endast annonsen i den filtrerade kommunen (KommunA) ska ge en hit — KommunB och den " +
            "län-only-annonsen avvisas av det aktiva kommun-filtret, ingen hit-rad skapas");
    }

    // ─────────────────────────── F4a (#803) — the geo UNION at scan time

    [Fact]
    public async Task RunAsync_RegionFilter_CreatesHitForLanOnlyAd_WithNullMunicipality()
    {
        // THE REGRESSION THAT MOTIVATES F4a. An ad tagged at LÄN granularity carries NO municipality
        // concept-id. Before the union, a whole-län pick had to be expanded into its kommun-ids — and
        // this ad, genuinely inside the picked län, matched none of them and NEVER notified the user: a
        // silent miss in a never-miss product, invisible in every log. With the union the region axis
        // admits it. If this test goes red, whole-län watchers have silently stopped hearing about
        // län-only ads.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [], [LanA], ct);
        var lanOnlyAd = await SeedAdWithOrgNrAndLocationAsync(
            orgNr, "Län Bygg AB", municipalityConceptId: null, regionConceptId: LanA, ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Select(h => h.JobAdId).ShouldBe(
            [lanOnlyAd],
            "en län-only-annons (municipality NULL) inne i det valda länet MÅSTE ge en hit — " +
            "annars tystnar hela-länet-bevakningen för exakt de annonser den finns till för");
    }

    [Fact]
    public async Task RunAsync_RegionFilter_AdmitsAdInMunicipalityWithinThatRegion()
    {
        // "Hela LanA" must also admit the kommun-tagged ads inside LanA — their municipality is in no
        // (empty) municipality list, so ONLY the region axis can admit them.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [], [LanA], ct);
        var adInKommunA = await SeedAdWithOrgNrAndLocationAsync(orgNr, "A Bygg AB", KommunA, LanA, ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).Select(h => h.JobAdId).ShouldBe([adInKommunA]);
    }

    [Fact]
    public async Task RunAsync_RegionFilter_ExcludesOtherRegion_AndAdWithNoGeoAtAll()
    {
        // The two rejections a region filter must still make: an ad in ANOTHER län, and an ad tagged
        // with NEITHER axis (8A — an ad that cannot be shown to be inside the selection never produces
        // a hit row; the union widened the admit rule, not the data-minimizing stance).
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [], [LanA], ct);
        await SeedAdWithOrgNrAndLocationAsync(orgNr, "B Bygg AB", KommunB, LanB, ct);
        await SeedAdWithOrgNrAndLocationAsync(orgNr, "Ingen Ort AB", null, null, ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty(
            "annonsen i ett annat län och annonsen helt utan geo-taggning avvisas båda av det " +
            "aktiva län-filtret — ingen hit-rad skapas (8A)");
    }

    [Fact]
    public async Task RunAsync_BothGeoAxes_EitherHitCreatesAHit()
    {
        // Union, not intersection: a hit on EITHER axis is enough. An intersection would reject both of
        // these ads (each satisfies exactly one axis) and starve the digest of ads the user asked for.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [KommunA], [LanB], ct);
        var kommunHit = await SeedAdWithOrgNrAndLocationAsync(orgNr, "A Bygg AB", KommunA, LanA, ct);
        var lanHit = await SeedAdWithOrgNrAndLocationAsync(orgNr, "B Län AB", null, LanB, ct);
        await SeedAdWithOrgNrAndLocationAsync(orgNr, "Utanför AB", KommunB, null, ct);

        await RunJobAsync(ct);

        var hits = (await GetHitsAsync(userId, ct)).Select(h => h.JobAdId).ToList();
        hits.Count.ShouldBe(2);
        hits.ShouldContain(kommunHit, "kommun-träff räcker även när annonsens län inte är valt");
        hits.ShouldContain(lanHit, "län-träff räcker även när annonsen saknar kommun");
    }

    [Fact]
    public async Task RunAsync_NoFilter_AdmitsAdsRegardlessOfMunicipality()
    {
        // Regression guard (RF-2 no-filter = show-all): a watch WITHOUT a filter notifies for every
        // watched-org.nr ad regardless of ort — including a KommunB ad AND a län-only (NULL) ad that an
        // active ort filter WOULD reject. Proves the pre-filter behaviour is unregressed by PR-F1.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, orgNr, ct); // no filter
        var adInB = await SeedAdWithOrgNrAndMunicipalityAsync(orgNr, "B Bygg AB", KommunB, ct);
        var adLanOnly = await SeedAdWithOrgNrAndMunicipalityAsync(orgNr, "Län Bygg AB", municipalityConceptId: null, ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Count.ShouldBe(2, "en bevakning UTAN filter ska ge hit för varje annons oavsett ort");
        hits.Select(h => h.JobAdId).ShouldContain(adInB);
        hits.Select(h => h.JobAdId).ShouldContain(adLanOnly);
    }

    [Fact]
    public async Task RunAsync_GeoFilter_AdvancesWatermark_EvenWhenEveryCandidateFilteredOut()
    {
        // The atomicity/idempotens invariant holds when the ort filter rejects EVERY candidate: the
        // watermark still advances to the scan instant (a later in-filter ad only catches FUTURE ads,
        // never a re-scan of the rejected ones), and no hit is created. The rejected ad IS a candidate
        // (watched org.nr + Active + within window) — only the client-side ort check drops it — so the
        // scan reaches the watermark advance after rejecting it.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [KommunA], [], ct);
        await SeedAdWithOrgNrAndMunicipalityAsync(orgNr, "B Bygg AB", KommunB, ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty("den bortfiltrerade annonsen ger ingen hit");
        var seeker = await GetSeekerAsync(userId, ct);
        seeker.LastCompanyWatchScanAt.ShouldBe(Now,
            "vattenmärket avancerar även när alla kandidat-annonser filtrerats bort (atomiciteten består)");
    }

    // ─────────────────────────── Seeding helpers

    private enum FollowConsent { Off, On, Withdrawn }

    private Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedConsentingUserAsync(CancellationToken ct)
        => SeedUserAsync(FollowConsent.On, ct);

    private async Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedUserAsync(
        FollowConsent consent, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var jobSeeker = JobSeeker.Register(Guid.NewGuid(), "Follow Seed", clock).Value;
        switch (consent)
        {
            case FollowConsent.On:
                jobSeeker.UpdateFollowedCompanyNotificationConsent(true, clock);
                break;
            case FollowConsent.Withdrawn:
                jobSeeker.UpdateFollowedCompanyNotificationConsent(true, clock);
                jobSeeker.UpdateFollowedCompanyNotificationConsent(false, clock);
                break;
            case FollowConsent.Off:
            default:
                break; // default OFF
        }

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);
        return (jobSeeker.UserId, jobSeeker.Id);
    }

    private async Task<CompanyWatchId> SeedWatchAsync(Guid userId, string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var watch = CompanyWatch.Follow(
            userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // Seeds an ACTIVE watch narrowed by a per-watch GEO filter (RF-2 + F4a): the two axes are UNIONED
    // (an ad passes on a kommun hit OR a län hit). onlyMatched stays false — this suite has no
    // profile/scorer, so the geo dimension is isolated here (RF-5 "endast matchade" is proven
    // separately by FilterToMatchingTests).
    private async Task<CompanyWatchId> SeedWatchWithGeoFilterAsync(
        Guid userId,
        string orgNr,
        IEnumerable<string> municipalities,
        IEnumerable<string> regions,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        var filter = WatchFilterSpec.Create(municipalities, regions, onlyMatched: false).Value;
        watch.SetFilter(filter).IsSuccess.ShouldBeTrue("SetFilter ska lyckas på en aktiv watch");
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // Kommun-only ad (no län) — the pre-F4a shape, kept so the existing kommun assertions read cleanly.
    private Task<JobAdId> SeedAdWithOrgNrAndMunicipalityAsync(
        string orgNr, string companyName, string? municipalityConceptId, CancellationToken ct)
        => SeedAdWithOrgNrAndLocationAsync(orgNr, companyName, municipalityConceptId, null, ct);

    // As SeedAdWithOrgNrAsync, but the ad ALSO carries workplace_address.municipality_concept_id and/or
    // .region_concept_id, so the STORED generated `municipality_concept_id` / `region_concept_id`
    // columns auto-populate (the geo filter reads BOTH). Passing null for an axis omits that key; both
    // null omits workplace_address entirely → both columns NULL (an ad with no geo at all). A län-only
    // ad (municipality null, region set) is the shape the F4a union exists for.
    private async Task<JobAdId> SeedAdWithOrgNrAndLocationAsync(
        string orgNr,
        string companyName,
        string? municipalityConceptId,
        string? regionConceptId,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = $"cw-scan-{Guid.NewGuid():N}";

        var addressFields = new List<string>();
        if (municipalityConceptId is not null)
            addressFields.Add($"\"municipality_concept_id\":\"{municipalityConceptId}\"");
        if (regionConceptId is not null)
            addressFields.Add($"\"region_concept_id\":\"{regionConceptId}\"");
        var addressJson = addressFields.Count == 0
            ? string.Empty
            : $",\"workplace_address\":{{{string.Join(",", addressFields)}}}";

        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"employer\":{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}{addressJson}}}";

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: new FixedClock(Now)).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // Seeds a watch then immediately UNFOLLOWS it (soft-delete) — the query filter should hide it.
    private async Task SeedUnfollowedWatchAsync(Guid userId, string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        watch.SoftDelete(clock);
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
    }

    // A personnummer-shaped 10-digit org.nr (third digit < 2 → enskild firma / potential personnummer).
    private static string UniquePersonnummerShapedOrgNr() =>
        "90" + "0" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

    // Imports a public job_ad whose raw_payload carries employer.organization_number, so the STORED
    // generated `organization_number` column auto-populates (mirrors CompanyWatchesTests /
    // ListJobAdsEmployerFilterTests). CreatedAt = Now (deterministic ingest time).
    private Task<JobAdId> SeedAdWithOrgNrAsync(string orgNr, string companyName, CancellationToken ct)
        => SeedAdWithOrgNrAtAsync(orgNr, companyName, Now, ct);

    // As above, but the ad's CreatedAt (ingest time) is set to `createdAt` (for the cold-start window).
    private async Task<JobAdId> SeedAdWithOrgNrAtAsync(
        string orgNr, string companyName, DateTimeOffset createdAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = $"cw-scan-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"employer\":{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: createdAt.AddDays(-1),
            expiresAt: createdAt.AddDays(60),
            clock: new FixedClock(createdAt)).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task RunJobAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var job = new CompanyWatchScanJob(
            sp.GetRequiredService<AppDbContext>(),
            new FixedClock(Now),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CompanyWatchScanJob>());
        await job.RunAsync(ct);
    }

    private async Task<IReadOnlyList<FollowedCompanyAdHit>> GetHitsAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(h => h.UserId == userId)
            .ToListAsync(ct);
    }

    private async Task<JobSeeker> GetSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.JobSeekers.AsNoTracking().FirstOrDefaultAsync(js => js.UserId == userId, ct))!;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
