using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
        // 10 digits in the "55xxxxxxxx" space, unique per call via a fresh Guid hash. USUALLY a
        // legal-entity org.nr, but NOT guaranteed non-personnummer-shaped: the D8 zero-pads, so the
        // third digit can be a leading 0/1, making the value pnr-shaped (IsPersonnummerShaped true). The
        // hit/count tests tolerate that (both scan arms hit the same ad); an AT-REST assertion needs the
        // guaranteed non-pnr form UniqueAbOrgNr() below instead.
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

    // ─────────────────────────── BrandGroup (#311 PR-5, ADR 0087 D4)

    [Fact]
    public async Task RunAsync_MemberFollowedBothDirectlyAndViaGroup_CreatesTwoHits_MultimapOracle()
    {
        // THE multimap oracle (D3b): a member org.nr is watched by BOTH a direct EMPLOYER follow AND a
        // BRAND_GROUP follow. Each is an independent watch, so the ad yields TWO hits
        // (UNIQUE(UserId, JobAdId, CompanyWatchId) — D3c: two honest rows, the digest lists the ad twice).
        // MUTATION: revert abWatchesByOrgNr from a multimap to a single-valued dict and the second
        // AddWatch silently overwrites → one hit → RED.
        var ct = TestContext.Current.CancellationToken;
        var memberOrgNr = UniqueAbOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        var directWatchId = await SeedWatchAsync(userId, memberOrgNr, ct);
        var groupWatchId = await SeedBrandGroupWatchAsync(userId, "volvo", ct);
        var adId = await SeedAdWithOrgNrAsync(memberOrgNr, "Volvo Cars AB", ct);

        await RunJobAsync(ct, StubProvider(("volvo", [memberOrgNr])));

        var hits = await GetHitsAsync(userId, ct);
        hits.Count.ShouldBe(2, "a direct follow AND a group follow of the same ad are two independent hits");
        hits.ShouldAllBe(h => h.JobAdId == adId);
        hits.Select(h => h.CompanyWatchId).ShouldBe([directWatchId, groupWatchId], ignoreOrder: true);
    }

    [Fact]
    public async Task RunAsync_GroupWatch_HitsMembersOnly_NotNonMembersNorOtherGroups()
    {
        // Asymmetric seed (the count-only oracle's 2+1 convention, generalised to 1+1+1): a member ad
        // MUST hit; a non-member AB ad MUST NOT; another group's member MUST NOT (this user does not
        // follow that group). Both cardinal failures — "matches nothing" and "matches everything" — fall.
        var ct = TestContext.Current.CancellationToken;
        var member = UniqueAbOrgNr();
        var nonMember = UniqueAbOrgNr();
        var otherGroupMember = UniqueAbOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        var groupWatchId = await SeedBrandGroupWatchAsync(userId, "volvo", ct);
        var memberAdId = await SeedAdWithOrgNrAsync(member, "Volvo AB", ct);
        await SeedAdWithOrgNrAsync(nonMember, "Random AB", ct);
        await SeedAdWithOrgNrAsync(otherGroupMember, "Scania AB", ct);

        await RunJobAsync(ct, StubProvider(("volvo", [member]), ("scania", [otherGroupMember])));

        var hits = await GetHitsAsync(userId, ct);
        hits.Count.ShouldBe(1);
        hits[0].JobAdId.ShouldBe(memberAdId);
        hits[0].CompanyWatchId.ShouldBe(groupWatchId);
    }

    [Fact]
    public async Task RunAsync_GroupWithTwoMembers_CreatesAHitForEachMemberAd()
    {
        // A group expands to ALL its members: two member ads → two hits, both keyed to the ONE group
        // watch. (The hub's summed #447/#452 counts over these members are a List-handler concern, PR-5
        // Commit 5 — here we pin only that the scan expands the full member set.)
        var ct = TestContext.Current.CancellationToken;
        var m1 = UniqueAbOrgNr();
        var m2 = UniqueAbOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        var groupWatchId = await SeedBrandGroupWatchAsync(userId, "volvo", ct);
        var ad1 = await SeedAdWithOrgNrAsync(m1, "Volvo Cars AB", ct);
        var ad2 = await SeedAdWithOrgNrAsync(m2, "Volvo Trucks AB", ct);

        await RunJobAsync(ct, StubProvider(("volvo", [m1, m2])));

        var hits = await GetHitsAsync(userId, ct);
        hits.Count.ShouldBe(2);
        hits.Select(h => h.JobAdId).ShouldBe([ad1, ad2], ignoreOrder: true);
        hits.ShouldAllBe(h => h.CompanyWatchId == groupWatchId);
    }

    [Fact]
    public async Task RunAsync_GroupWatchWithSlugAbsentFromCatalogue_CreatesNoHit_AndDoesNotThrow()
    {
        // An orphaned slug (the group was later removed from the catalogue) resolves to zero members: the
        // watch honestly matches nothing, the scan NEVER throws (per-user isolation must survive stale
        // reference data), and the watermark still advances. Also the null-org.nr no-NRE pin: a group
        // watch's org.nr is NULL, so a partition that dereferenced it would throw here.
        var ct = TestContext.Current.CancellationToken;
        var member = UniqueAbOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedBrandGroupWatchAsync(userId, "removed-group", ct);
        await SeedAdWithOrgNrAsync(member, "Some AB", ct);

        // The catalogue does NOT contain "removed-group".
        await RunJobAsync(ct, StubProvider(("volvo", [member])));

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty();
        (await GetSeekerAsync(userId, ct)).LastCompanyWatchScanAt.ShouldBe(Now,
            "the watermark advances even when a group slug is orphaned — no exception aborted the scan");
    }

    [Fact]
    public async Task RunAsync_GroupWatchWithGeoFilter_AppliesTheFilterPerWatch()
    {
        // D3d: the per-watch ort filter applies to a group watch identically. A group narrowed to KommunA
        // admits the member ad in KommunA and suppresses the member ad in KommunB.
        var ct = TestContext.Current.CancellationToken;
        var member = UniqueAbOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedBrandGroupWatchWithGeoFilterAsync(userId, "volvo", [KommunA], [], ct);
        var inA = await SeedAdWithOrgNrAndLocationAsync(member, "In A", KommunA, LanA, ct);
        await SeedAdWithOrgNrAndLocationAsync(member, "In B", KommunB, LanB, ct);

        await RunJobAsync(ct, StubProvider(("volvo", [member])));

        var hits = await GetHitsAsync(userId, ct);
        hits.Count.ShouldBe(1);
        hits[0].JobAdId.ShouldBe(inA);
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

    // ─────────────────────────── #551 PR-B D6 — the remote/distans axis at scan time

    [Fact]
    public async Task RunAsync_RemoteFilter_CreatesHitForRemoteAd_RejectsNonRemote()
    {
        // A watch narrowed to the REMOTE axis alone (a valid remote-only spec) produces a hit ONLY for
        // a remote (location-less) ad. A non-remote ad in KommunA is rejected — the remote-only spec
        // must not admit every ad (the D6 early-return-accounts-for-remote fix), and it does not
        // widen to ort. Proves the ad's remote flag (PR-A column) flows through the scan projection
        // into AdmitsLocation's remote disjunct.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchWithGeoFilterAsync(userId, orgNr, [], [], ct, remote: true);
        var remoteAd = await SeedAdWithOrgNrAndLocationAsync(
            orgNr, "Distans Bygg AB", municipalityConceptId: null, regionConceptId: null, ct, remote: true);
        await SeedAdWithOrgNrAndLocationAsync(orgNr, "A Bygg AB", KommunA, LanA, ct); // on-site, not remote

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Select(h => h.JobAdId).ShouldBe(
            [remoteAd],
            "endast den remote-klassade annonsen ska ge en hit — den icke-remote KommunA-annonsen " +
            "avvisas av det aktiva remote-only-filtret (D6: tidiga return:en får inte släppa igenom allt)");
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

    // ─────────────────────────── #544 (ADR 0090 D5) — pnr-shaped org.nr → HMAC token at rest

    [Fact]
    public async Task FollowViaExecutor_TokenisesPersonnummerShapedOrgNrAtRest_AbStaysPlaintext()
    {
        // At-rest witness. Following an enskild-firma (personnummer-shaped) org.nr through the REAL
        // executor path (FollowCompanyCommandHandler → CompanyWatchFollowExecutor) must store a keyed
        // HMAC token in company_watches.organization_number — NEVER the 10-digit personnummer. An AB
        // org.nr is public and stays plaintext (the scan's SQL IN matches it directly). Read straight
        // from Postgres past the EF value-converter so this proves the ON-DISK form.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var pnr = UniquePersonnummerShapedOrgNr();
        var ab = UniqueAbOrgNr();

        var pnrWatchId = await FollowViaExecutorAsync(userId, pnr, ct);
        var abWatchId = await FollowViaExecutorAsync(userId, ab, ct);

        var storedPnr = await RawOrgNrByWatchIdAsync(pnrWatchId, ct);
        var storedAb = await RawOrgNrByWatchIdAsync(abWatchId, ct);

        storedPnr.ShouldNotBeNull();
        storedPnr.ShouldNotBe(pnr, "a personnummer-shaped org.nr must NEVER be stored plaintext at rest");
        storedPnr.Length.ShouldBe(64, "HMAC-SHA256 token = 64 lowercase hex chars");
        storedPnr.ShouldBe(TokenOf(pnr),
            "the at-rest value is exactly HMAC(watch pepper, pnr) — deterministic + scan-matchable");

        storedAb.ShouldBe(ab, "a legal-entity (AB) org.nr is public data and stays plaintext at rest");
    }

    [Fact]
    public async Task RunAsync_HitsEnskildFirma_WhenFollowIsTokenisedAtRest()
    {
        // The HMAC scan arm end-to-end. A tokenised enskild follow (token at rest) + a pnr-shaped ad →
        // the scan admits the ad via the pnr-shape prefilter and matches it via the IN-MEMORY
        // tokenizer.Tokenize(ad.org.nr) == storedToken probe. Distinct from the legacy-plaintext
        // RunAsync_ScansPersonnummerShapedOrgNr test: here the follow is stored as a TOKEN, so this
        // pins the #544 token arm (not the dual-probe legacy arm).
        var ct = TestContext.Current.CancellationToken;
        var pnr = UniquePersonnummerShapedOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        var watchId = await FollowViaExecutorAsync(userId, pnr, ct);

        // Guard: the follow really is a token at rest (else this would pass for the wrong reason).
        (await RawOrgNrByWatchIdAsync(watchId, ct))!.Length.ShouldBe(64,
            "the enskild follow must be tokenised at rest for this test to exercise the HMAC scan arm");

        var jobAdId = await SeedAdWithOrgNrAsync(pnr, "Enskild Firma Karlsson", ct);

        await RunJobAsync(ct);

        var hit = (await GetHitsAsync(userId, ct)).ShouldHaveSingleItem();
        hit.JobAdId.ShouldBe(jobAdId);
        hit.CompanyWatchId.Value.ShouldBe(watchId);
    }

    [Fact]
    public async Task FollowViaExecutor_RefollowEnskildAfterUnfollow_KeepsExactlyOnePhysicalRow()
    {
        // Refollow dedup on a TOKENISED key. follow → unfollow (soft-delete) → follow the same enskild
        // firma: the executor's token dual-probe (w.OrganizationNumber == storedToken || == plaintext)
        // finds the soft-deleted token row and RESURRECTS it — never a second physical row. FORK B1
        // "exactly one physical row per (user, org.nr) ever", holding across the plaintext→token change.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var pnr = UniquePersonnummerShapedOrgNr();

        var firstId = await FollowViaExecutorAsync(userId, pnr, ct);
        await UnfollowDirectAsync(userId, ct);
        var secondId = await FollowViaExecutorAsync(userId, pnr, ct);

        secondId.ShouldBe(firstId, "the executor resurrects the SAME token row (dual-probe), never a second");
        (await PhysicalWatchCountAsync(userId, ct)).ShouldBe(1,
            "exactly one physical row per (user, enskild org.nr) — token dual-probe resurrect, not duplicate");
    }

    [Fact]
    public async Task RunAsync_PnrShapePrefilter_AdmitsBothBoundaryThirdDigits_TheSupersetPin()
    {
        // THE CARDINAL-SIN SUPERSET PIN. The scan's SQL pnr-shape prefilter (Length==10 AND 3rd digit
        // "0"/"1") MUST admit EVERY ad whose org.nr IsPersonnummerShaped() is true — a too-narrow
        // prefilter silently drops an enskild ad its follower asked for (a watch that matches nothing
        // forever, invisible in every log). IsPersonnummerShaped on a 10-digit value is `3rd digit < 2`,
        // i.e. 0 OR 1. Seed pnr-shaped ads at BOTH boundary third-digits; a tokenised enskild follow of
        // each MUST hit. Drop the "1" arm from the SQL prefilter and the third-digit-1 ad vanishes here.
        var ct = TestContext.Current.CancellationToken;
        var (userId, _) = await SeedConsentingUserAsync(ct);

        var pnrDigit0 = UniquePnrShapedOrgNrWithThirdDigit('0');
        var pnrDigit1 = UniquePnrShapedOrgNrWithThirdDigit('1');
        OrganizationNumber.FromTrusted(pnrDigit0).IsPersonnummerShaped().ShouldBeTrue("fixture must be pnr-shaped");
        OrganizationNumber.FromTrusted(pnrDigit1).IsPersonnummerShaped().ShouldBeTrue("fixture must be pnr-shaped");

        var watch0 = await FollowViaExecutorAsync(userId, pnrDigit0, ct);
        var watch1 = await FollowViaExecutorAsync(userId, pnrDigit1, ct);
        var ad0 = await SeedAdWithOrgNrAsync(pnrDigit0, "Enskild Noll", ct);
        var ad1 = await SeedAdWithOrgNrAsync(pnrDigit1, "Enskild Ett", ct);
        // A pnr-shaped ad the user does NOT follow: a prefilter candidate that must fall through (no hit).
        await SeedAdWithOrgNrAsync(UniquePnrShapedOrgNrWithThirdDigit('0'), "Enskild Utan Följare", ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Select(h => h.JobAdId).ShouldContain(ad0, "3rd-digit-0 pnr ad must be admitted by the prefilter and hit");
        hits.Select(h => h.JobAdId).ShouldContain(ad1, "3rd-digit-1 pnr ad must be admitted by the prefilter and hit");
        hits.Count.ShouldBe(2, "exactly the two FOLLOWED pnr ads hit — the unfollowed pnr candidate falls through");
        hits.Select(h => h.CompanyWatchId.Value).ShouldBe([watch0, watch1], ignoreOrder: true);
    }

    // Follows an org.nr through the REAL executor path (public FollowCompanyCommandHandler →
    // internal CompanyWatchFollowExecutor). The executor decides the at-rest key (token for pnr-shaped,
    // plaintext for AB) — the seam #544 changed. Returns the resulting watch id.
    private async Task<Guid> FollowViaExecutorAsync(Guid userId, string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = new FollowCompanyCommandHandler(
            sp.GetRequiredService<AppDbContext>(),
            currentUser,
            new FixedClock(Now),
            sp.GetRequiredService<IDbExceptionInspector>(),
            sp.GetRequiredService<IProtectedIdentityTokenizer>());

        var result = await handler.Handle(new FollowCompanyCommand(orgNr), ct);
        result.IsSuccess.ShouldBeTrue($"follow via executor should succeed for {orgNr}");
        return result.Value;
    }

    // Reads organization_number straight from Postgres for one watch, PAST the EF value-converter, so
    // the assertion is against the ON-DISK form (a 64-hex token or the 10-digit plaintext).
    private async Task<string?> RawOrgNrByWatchIdAsync(Guid watchId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT organization_number FROM company_watches WHERE id = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = watchId;
        cmd.Parameters.Add(p);
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : raw.ToString();
    }

    // The deterministic HMAC token for an org.nr under the test watch pepper (parity with what the
    // executor stores + what the scan re-derives).
    private string TokenOf(string orgNr)
    {
        using var scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IProtectedIdentityTokenizer>().Tokenize(orgNr);
    }

    // Soft-deletes the user's (single) active watch — an unfollow, without going through the command
    // handler (the refollow, not the unfollow, is what this test exercises).
    private async Task UnfollowDirectAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watch = await db.CompanyWatches.IgnoreQueryFilters()
            .SingleAsync(w => w.UserId == userId && w.DeletedAt == null, ct);
        watch.SoftDelete(new FixedClock(Now));
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> PhysicalWatchCountAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CompanyWatches.IgnoreQueryFilters().CountAsync(w => w.UserId == userId, ct);
    }

    // A 10-digit personnummer-shaped org.nr with an EXACT third digit (0 or 1 — the two values <2 that
    // IsPersonnummerShaped admits). Unique remainder so tests never cross-contaminate on the shared DB.
    private static string UniquePnrShapedOrgNrWithThirdDigit(char thirdDigit) =>
        "90" + thirdDigit + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

    // A GUARANTEED non-personnummer-shaped (AB) org.nr — third digit fixed to '2' (a legal-entity group
    // number is 2–9). NOTE the sibling UniqueLegalOrgNr() above does NOT guarantee this: its "55" + D8
    // form zero-pads, so the third position can be a leading 0/1, making a nominal "AB" org.nr actually
    // pnr-shaped. The existing hit/count tests tolerate that (both scan arms hit); an AT-REST assertion
    // (the value must stay plaintext) does not, so it needs this guaranteed form.
    private static string UniqueAbOrgNr() =>
        "552" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

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
        CancellationToken ct,
        bool remote = false)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        // #551 PR-B D6 — remote is a union axis on the watch filter (a remote-only spec is valid).
        var filter = WatchFilterSpec.Create(municipalities, regions, onlyMatched: false, remote).Value;
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
        CancellationToken ct,
        bool remote = false)
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

        // #551 PR-B D6 — remote is AF's harvested classification, NOT a raw_payload key: when a
        // test seeds a remote ad it states the flag on the facets (parity the ACL / TestFacets.From),
        // alongside the org.nr + ort the scan filter reads. Non-remote path stays FromPayload.
        var facets = remote
            ? TestFacets.From(
                municipality: municipalityConceptId,
                region: regionConceptId,
                organizationNumber: orgNr,
                remote: true)
            : TestFacets.FromPayload(rawPayload);

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: facets,
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: new FixedClock(Now), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
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
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: createdAt.AddDays(-1),
            expiresAt: createdAt.AddDays(60),
            clock: new FixedClock(createdAt), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task RunJobAsync(CancellationToken ct, IBrandGroupProvider? brandGroups = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var job = new CompanyWatchScanJob(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<IProtectedIdentityTokenizer>(),
            // Default to the fixture's real provider (the EMPTY shipped catalogue); a group test passes a
            // synthetic catalogue so its members are deterministic.
            brandGroups ?? sp.GetRequiredService<IBrandGroupProvider>(),
            new FixedClock(Now),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CompanyWatchScanJob>());
        await job.RunAsync(ct);
    }

    // A synthetic brand-group catalogue built directly (bypassing the loader — the scan only needs
    // slug → member org.nrs). Members must be non-pnr AB org.nrs (UniqueAbOrgNr) to land on the
    // plaintext arm, exactly as the loader would guarantee in production.
    private static StubBrandGroupProvider StubProvider(params (string Slug, string[] Members)[] groups)
    {
        var dict = groups.ToDictionary(
            g => g.Slug,
            g => new BrandGroup(g.Slug, g.Slug + " (koncern)", g.Members),
            StringComparer.Ordinal);
        return new StubBrandGroupProvider(new BrandGroupCatalog("test.v1", dict));
    }

    private sealed class StubBrandGroupProvider(BrandGroupCatalog catalog) : IBrandGroupProvider
    {
        public BrandGroupCatalog Catalog { get; } = catalog;
    }

    private async Task<CompanyWatchId> SeedBrandGroupWatchAsync(Guid userId, string slug, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);
        var watch = CompanyWatch.FollowBrandGroup(userId, BrandGroupId.Create(slug).Value, clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    private async Task<CompanyWatchId> SeedBrandGroupWatchWithGeoFilterAsync(
        Guid userId, string slug, IEnumerable<string> municipalities, IEnumerable<string> regions,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);
        var watch = CompanyWatch.FollowBrandGroup(userId, BrandGroupId.Create(slug).Value, clock).Value;
        var filter = WatchFilterSpec.Create(municipalities, regions, onlyMatched: false).Value;
        watch.SetFilter(filter).IsSuccess.ShouldBeTrue("SetFilter ska lyckas på en aktiv group-watch");
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
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
