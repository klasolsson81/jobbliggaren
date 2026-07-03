using System.Security.Cryptography;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Auth;

/// <summary>
/// End-to-end smoke-test för <see cref="HardDeleteAccountsJob"/> mot riktig
/// Postgres + AspNet Identity (Testcontainers). Verifierar 3-stegs-algoritmen
/// per ADR 0024 D6: orphan-cleanup → hämta mogna → cascade hard-delete +
/// audit-anonymisering + Identity-DELETE.
///
/// Märkt <c>[Trait("Category", "SmokeTest")]</c>.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class HardDeleteAccountsJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private const int RestoreWindowDays = 30;

    [Fact]
    public async Task RunAsync_HardDeletesEligibleAccount_AnonymizesAudit_RemovesIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1)); // utanför fönstret

        // Setup: Identity-user + JobSeeker (soft-deletad > 30d) + audit-rad
        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);
        var auditAggregateId = Guid.NewGuid();
        await SeedAuditEntryAsync(userId, auditAggregateId, ct);

        // Akt
        await RunJobAsync(now, ct);

        // Verifiera: hard-delete + Identity borta + audit anonymiserad
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(js => js.Id == jobSeekerId, ct);
        jobSeeker.ShouldBeNull("JobSeeker ska vara hard-deletad");

        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        identityUser.ShouldBeNull("Identity-rad ska vara borta efter Steg 2 h");

        var auditEntry = await db.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AggregateId == auditAggregateId, ct);
        auditEntry.ShouldNotBeNull("audit-raden bevaras 90 dagar för accountability");
        auditEntry.UserId.ShouldBeNull("user_id ska anonymiseras");
        auditEntry.IpAddress.ShouldBeNull();
        auditEntry.UserAgent.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_DoesNotHardDeleteAccountsWithin30Days()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var recentDeletedAt = now.AddDays(-10); // inom restore-fönstret

        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(recentDeletedAt, ct);

        await RunJobAsync(now, ct);

        // Verifiera: JobSeeker fortfarande finns (soft-deleted), Identity finns
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(js => js.Id == jobSeekerId, ct);
        jobSeeker.ShouldNotBeNull("nyligen soft-deleted JobSeeker ska BEHÅLLAS inom restore-fönstret");
        jobSeeker.DeletedAt.ShouldNotBeNull();

        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        identityUser.ShouldNotBeNull("Identity-rad ska finnas tills hard-delete-fönstret gått ut");
    }

    [Fact]
    public async Task CleanupIdentityOrphans_RemovesOrphanIdentityRowsWithoutMatchingJobSeeker()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed: Identity-user UTAN matchande JobSeeker (orphan från tidigare körning).
        //
        // #508 grace-fönster: sweepen sopar numera en JobSeeker-lös Identity-user ENDAST
        // om den är ÄLDRE än 1h-fönstret (en färsk orphan presumeras mid-registrering per
        // ADR 0024 två-boundary och sopas aldrig). Detta test är regressions-guarden
        // "åldrade orphans sopas fortfarande" → vi ÅLDRAR därför den seedade orphanen
        // förbi grace-fönstret. CreatedAt sätts explicit FÖRE CreateAsync: kolumnen är
        // ValueGeneratedOnAdd (store-default now()), som bara fyller CLR-sentinelen
        // default(DateTimeOffset) — ett explicit värde insertas verbatim och gör
        // användaren sweepbar. Åldern mäts mot AccountHardDeleters DI-resolvade
        // IDateTimeProvider (riktig systemklocka), därför en real-tid-offset.
        var orphanEmail = $"orphan-{Guid.NewGuid():N}@test.local";
        Guid orphanUserId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = orphanEmail,
                Email = orphanEmail,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2), // äldre än #508 1h-grace → sweepbar
            };
            var result = await userManager.CreateAsync(user, "OrphanPass123!");
            result.Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");
            orphanUserId = user.Id;
        }

        // Akt: kör jobbet (Steg 0 ska plocka upp orphan)
        await RunJobAsync(DateTimeOffset.UtcNow, ct);

        // Verifiera: orphan borta
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var orphan = await userManager.FindByIdAsync(orphanUserId.ToString());
            orphan.ShouldBeNull("orphan Identity-rad ska rensas av Steg 0");
        }
    }

    [Fact]
    public async Task CleanupIdentityOrphans_DoesNotSweepIdentityUserWithinGraceWindow()
    {
        // #508 (epik #482 PR-2) — CTO-bunden RED-test. En Identity-user UTAN JobSeeker
        // som är YNGRE än grace-fönstret (1h) presumeras vara mid-registrering: per
        // ADR 0024 två-boundary commit:as Identity-raden FÖRE JobSeeker-raden, så en
        // helt färsk JobSeeker-lös Identity-user är förväntad och transient — den ska
        // ALDRIG sopas som orphan.
        //
        // RÖD mot nuvarande kod: CleanupIdentityOrphansAsync saknar än så länge ålders-
        // filtret och sopar VARJE JobSeeker-lös Identity-user oavsett ålder → den färska
        // användaren nedan raderas och FindByIdAsync returnerar null. GRÖN efter att
        // grace-filtret (CreatedAt <= clock.UtcNow − 1h) landat.
        var ct = TestContext.Current.CancellationToken;

        // Seed: FÄRSK Identity-user utan JobSeeker. CreatedAt lämnas OSATT ⇒ store-
        // defaulten now() (ValueGeneratedOnAdd) stämplar en tidpunkt inom grace-fönstret
        // ⇒ mid-registrering-presumtionen gäller. Distinkt e-post för test-isolering.
        var freshEmail = $"fresh-orphan-{Guid.NewGuid():N}@test.local";
        Guid freshUserId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = freshEmail, Email = freshEmail };
            var result = await userManager.CreateAsync(user, "FreshOrphanPass123!");
            result.Succeeded.ShouldBeTrue("seed: färsk Identity-user måste skapas");
            freshUserId = user.Id;
        }

        // Akt: kör jobbet (Steg 0 orphan-cleanup).
        await RunJobAsync(DateTimeOffset.UtcNow, ct);

        // Verifiera: den färska användaren finns KVAR.
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await userManager.FindByIdAsync(freshUserId.ToString());
            fresh.ShouldNotBeNull(
                "Identity-user inom grace-fönstret (mid-registrering, ADR 0024 två-boundary) ska ALDRIG sopas");
        }
    }

    [Fact]
    public async Task CleanupIdentityOrphans_DoesNotDeleteReverseOrphan_AndForwardSweepStillRuns()
    {
        // #508 defense-in-depth (non-destruktivt). En REVERSE orphan är en JobSeeker vars
        // UserId saknar motsvarande Identity-user (ADR 0024 två-boundary-glapp åt andra
        // hållet). Grace-sweepen DETEKTERAR + LOGGAR (Warning, count-only) sådana rader men
        // RADERAR dem ALDRIG här — reverse-orphan-radering är en separat concern (#524).
        //
        // Vad testet BEVISAR (och namnet speglar): det bindande beteende-kontraktet —
        // reverse orphan RADERAS ALDRIG, och detektionen STÖR INTE den ordinarie
        // forward-sweepen. Detektionens LOGG-emission (Warning, count-only) asserteras
        // medvetet INTE: en ren fångst av ILogger<AccountHardDeleter> hade krävt antingen en
        // ny testpaketberoende (Microsoft.Extensions.Diagnostics.Testing, §9.2-diskussion) eller
        // en in-memory ILoggerProvider i den DELADE WorkerTestFixture (fixture-kirurgi som
        // övriga Worker-tester inte ska bära) — oproportionerligt för en ren synlighetssignal.
        // Warning-loggen är i stället verifierad via code review (alla tre review-agenter läste
        // LogReverseOrphansDetected). Ett kast i jobbet fäller testet direkt (klausul (a)).
        var ct = TestContext.Current.CancellationToken;

        // Seed A — REVERSE orphan: en JobSeeker med slumpad UserId (ingen Identity-user),
        // INTE soft-deletad (så hard-delete-steget, som bara plockar mogna soft-deletade
        // konton, aldrig rör den).
        var reverseOrphanUserId = Guid.NewGuid();
        JobSeekerId reverseOrphanSeekerId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new FixedClock(DateTimeOffset.UtcNow.AddDays(-1));
            var seeker = JobSeeker.Register(reverseOrphanUserId, "Reverse Orphan", clock).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);
            reverseOrphanSeekerId = seeker.Id;
        }

        // Seed B — legit ÅLDRAD forward orphan: Identity-user utan JobSeeker, CreatedAt 2h
        // bakåt (äldre än 1h-grace ⇒ sweepbar). CreatedAt sätts FÖRE CreateAsync
        // (ValueGeneratedOnAdd insertar värdet verbatim; real-tid-offset mot AccountHardDeleters
        // DI-resolvade systemklocka).
        var forwardEmail = $"fwd-orphan-{Guid.NewGuid():N}@test.local";
        Guid forwardUserId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = forwardEmail,
                Email = forwardEmail,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2), // äldre än #508 1h-grace → sweepbar
            };
            var result = await userManager.CreateAsync(user, "FwdOrphanPass123!");
            result.Succeeded.ShouldBeTrue("seed: åldrad forward-orphan Identity-user måste skapas");
            forwardUserId = user.Id;
        }

        // Akt: (a) inget kast får ske — ett kast fäller testet via oavlyssnad exception.
        await RunJobAsync(DateTimeOffset.UtcNow, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // (b) reverse-orphan JobSeeker finns KVAR — reverse orphans är LOG-ONLY här.
        var reverseSeekerAfter = await verifyDb.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.Id == reverseOrphanSeekerId, ct);
        reverseSeekerAfter.ShouldNotBeNull(
            "reverse orphan (JobSeeker utan Identity-user) ska DETEKTERAS men ALDRIG raderas här (log-only, #524)");

        // (c) den åldrade forward-orphanen sopades — reverse-orphan-detektionen får inte
        // störa den ordinarie forward-sweepen.
        var forwardAfter = await verifyUserManager.FindByIdAsync(forwardUserId.ToString());
        forwardAfter.ShouldBeNull(
            "åldrad forward orphan ska fortfarande sopas — reverse-orphan-detektion får inte störa forward-sweepen");
    }

    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToSavedSearchesAndRecentJobSearches()
    {
        // GDPR Art. 17-cascade (ADR 0060 Mekanik-not 5 + ADR 0024-amend
        // 2026-05-20): SavedSearches och RecentJobSearches saknar databas-FK
        // till JobSeekers (ADR 0011 strongly-typed soft-reference). De måste
        // raderas explicit i HardDeleteAccountAsync — annars orphan-PII (q-
        // fritext, namn-värdig sökterm) blir kvar efter konto-radering.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1));

        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);

        // Seed SavedSearch + RecentJobSearch för seekern
        Guid savedSearchId;
        Guid recentSearchId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new FixedClock(oldDeletedAt.AddDays(-2));
            var criteria = SearchCriteria.Create(
                occupationGroup: ["grp_12345"], municipality: ["sthlm_kn"],
                region: ["stockholm"], employmentType: null, worktimeExtent: null,
                employer: null,
                q: "developer",
                sortBy: JobAdSortBy.PublishedAtDesc).Value;

            var saved = SavedSearch.Create(jobSeekerId, "Mitt sök", criteria, false, clock).Value;
            db.SavedSearches.Add(saved);

            var recent = RecentJobSearch.Capture(jobSeekerId, criteria, 10, clock.UtcNow);
            db.RecentJobSearches.Add(recent);

            await db.SaveChangesAsync(ct);
            savedSearchId = saved.Id.Value;
            recentSearchId = recent.Id.Value;
        }

        await RunJobAsync(now, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var savedAfter = await verifyDb.SavedSearches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == new SavedSearchId(savedSearchId), ct);
        savedAfter.ShouldBeNull("SavedSearch ska cascade-raderas vid hard-delete (GDPR Art. 17)");

        var recentAfter = await verifyDb.RecentJobSearches
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == new RecentJobSearchId(recentSearchId), ct);
        recentAfter.ShouldBeNull("RecentJobSearch ska cascade-raderas vid hard-delete (GDPR Art. 17)");
    }

    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToSavedJobAds()
    {
        // F6 P5 Punkt 2 Del A — SavedJobAd cascade-paritet (ADR 0024-amend
        // 2026-05-23): saved_job_ads saknar databas-FK till job_seekers
        // (ADR 0011 strongly-typed soft-reference). Måste raderas explicit i
        // HardDeleteAccountAsync — annars orphan-rader efter konto-radering.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1));

        var (_, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);

        Guid savedJobAdId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedJobAdJobAdId = new JobAdId(Guid.NewGuid());
            var saved = SavedJobAd.Save(
                jobSeekerId, savedJobAdJobAdId, oldDeletedAt.AddDays(-2));
            db.SavedJobAds.Add(saved);
            await db.SaveChangesAsync(ct);
            savedJobAdId = saved.Id.Value;
        }

        await RunJobAsync(now, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var savedJobAdAfter = await verifyDb.SavedJobAds
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Id == new SavedJobAdId(savedJobAdId), ct);
        savedJobAdAfter.ShouldBeNull(
            "SavedJobAd ska cascade-raderas vid hard-delete (GDPR Art. 17, ADR 0024 amend 2026-05-23)");
    }

    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToParsedResume()
    {
        // GDPR Art. 17-cascade (#370, found by the #268 audit). ParsedResume is the
        // raw-CV staging aggregate (ADR 0074) and an FK-less by-JobSeekerId
        // soft-reference (ADR 0011 — no DB-FK to job_seekers, same pattern as
        // SavedSearches/RecentJobSearches/SavedJobAds/UserJobAdMatch). It must be
        // deleted EXPLICITLY in HardDeleteAccountAsync. Crypto-erasure
        // (DeleteDataKeysAsync) only renders the DEK-encrypted columns
        // (raw_text/parsed_content_enc) unreadable — it does NOT remove the PLAINTEXT
        // columns (source_file_name, frequently the data subject's name; job_seeker_id),
        // so the orphaned ROWS themselves leak PII. This test seeds a GENUINELY
        // encrypted parsed_resumes row (full DEK pipeline, parity
        // ParsedResumeEncryptionTests) for a matured soft-deleted account, asserts it
        // EXISTS pre-run (so the test is not vacuously green), then asserts 0 rows
        // remain for that JobSeekerId after the job.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1));

        var (_, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);

        await SeedParsedResumeForJobSeekerAsync(jobSeekerId, ct);

        // Pre-condition: exactly one parsed_resumes row exists for this JobSeeker
        // (IgnoreQueryFilters — the account is soft-deleted, so its children are
        // filtered out of default queries). Projects Id only — never materialises the
        // encrypted aggregate (no warmed DEK in this scope).
        using (var preScope = _fixture.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var preCount = await preDb.ParsedResumes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.JobSeekerId == jobSeekerId)
                .CountAsync(ct);
            preCount.ShouldBe(1, "seed must persist exactly one parsed_resumes row before the job runs");
        }

        await RunJobAsync(now, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var remaining = await verifyDb.ParsedResumes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.JobSeekerId == jobSeekerId)
            .CountAsync(ct);
        remaining.ShouldBe(0,
            "ParsedResume ska cascade-raderas vid hard-delete (GDPR Art. 17, #370, found by #268 audit) — "
            + "annars orphan:as plaintext source_file_name (PII) + job_seeker_id efter konto-radering");
    }

    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToApplicationResumeAndUserJobAdMatch()
    {
        // GDPR Art. 17 cascade-completeness (#399, behavioral follow-up to the #374
        // build-time fitness function). AccountHardDeleteCascadeFitnessTests proves
        // every FK-less user-owned aggregate is WIRED into HardDeleteAccountAsync
        // (structural); this oracle proves the cascade RUNS for the three not yet
        // asserted here — Application (by JobSeekerId), Resume (by JobSeekerId; its
        // ResumeVersions go via DB-FK CASCADE), and UserJobAdMatch (FK-less by UserId,
        // ADR 0080), plus CompanyWatch (FK-less by UserId, ADR 0087 D3, #311 PR-3) and
        // FollowedCompanyAdHit (FK-less by UserId, ADR 0087 D5, #311 PR-4).
        // Together with the SavedSearches/RecentJobSearches/SavedJobAds/ParsedResume
        // tests above, all 9 CascadeMap aggregates are now covered both structurally
        // (build-time) and behaviorally (here) — symmetric layers.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1));

        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);

        // Application — coverLetter null (the DEK-encrypted column) so the seed write
        // needs no warm owner DEK (parity ApplyToJobAdAsync); job_ad_id is a soft
        // reference, NO FK (ApplicationConfiguration). UserJobAdMatch — DEK-free
        // plaintext concept-ids, keyed by UserId. Both seeded in one scope.
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seedClock = new FixedClock(oldDeletedAt.AddDays(-2));

            var application = DomainApplication.Create(
                jobSeekerId, JobAdId.New(), coverLetter: null,
                manualPosting: null, seedClock).Value;
            db.Applications.Add(application);

            var match = UserJobAdMatch.Create(
                userId, JobAdId.New(), NotifiableMatchGrade.Top, ["csharp"], seedClock).Value;
            db.UserJobAdMatches.Add(match);

            // CompanyWatch — FK-less by UserId (ADR 0087 D3, #311 PR-3), plaintext org.nr.
            var watch = CompanyWatch.Follow(
                userId, OrganizationNumber.Create("5592804784").Value, seedClock).Value;
            db.CompanyWatches.Add(watch);

            // FollowedCompanyAdHit — FK-less by UserId (ADR 0087 D5, #311 PR-4), the company-follow
            // notification-delivery record.
            var hit = FollowedCompanyAdHit.Create(
                userId, JobAdId.New(), watch.Id, seedClock).Value;
            db.FollowedCompanyAdHits.Add(hit);

            await db.SaveChangesAsync(ct);
        }

        // Resume — its initial Master ResumeVersion carries DEK-encrypted content
        // (content_enc, ADR 0049), so the seed warms the owner DEK first.
        await SeedResumeForJobSeekerAsync(jobSeekerId, ct);

        // Pre-condition: one row each exists (so no assertion is vacuously green).
        // IgnoreQueryFilters — the account is soft-deleted; counts project no
        // encrypted column (SQL COUNT), so no warm DEK is needed in this scope.
        using (var preScope = _fixture.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var appCount = await preDb.Applications
                .IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.JobSeekerId == jobSeekerId).CountAsync(ct);
            appCount.ShouldBe(1, "seed must persist exactly one application row before the job runs");

            var resumeCount = await preDb.Resumes
                .IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.JobSeekerId == jobSeekerId).CountAsync(ct);
            resumeCount.ShouldBe(1, "seed must persist exactly one resume row before the job runs");

            var matchCount = await preDb.UserJobAdMatches
                .IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.UserId == userId).CountAsync(ct);
            matchCount.ShouldBe(1, "seed must persist exactly one user_job_ad_match row before the job runs");

            var watchCount = await preDb.CompanyWatches
                .IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.UserId == userId).CountAsync(ct);
            watchCount.ShouldBe(1, "seed must persist exactly one company_watches row before the job runs");

            var hitCount = await preDb.FollowedCompanyAdHits
                .IgnoreQueryFilters().AsNoTracking()
                .Where(h => h.UserId == userId).CountAsync(ct);
            hitCount.ShouldBe(1, "seed must persist exactly one followed_company_ad_hits row before the job runs");
        }

        await RunJobAsync(now, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var applicationsAfter = await verifyDb.Applications
            .IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId).CountAsync(ct);
        applicationsAfter.ShouldBe(0,
            "Application ska hard-raderas vid konto-radering (GDPR Art. 17)");

        var resumesAfter = await verifyDb.Resumes
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId).CountAsync(ct);
        resumesAfter.ShouldBe(0,
            "Resume ska hard-raderas vid konto-radering (GDPR Art. 17); dess ResumeVersions "
            + "(aggregat-interna barn, ingen egen DbSet) följer via DB-FK ON DELETE CASCADE "
            + "(ResumeConfiguration) när parent-raden tas bort");

        var matchesAfter = await verifyDb.UserJobAdMatches
            .IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId).CountAsync(ct);
        matchesAfter.ShouldBe(0,
            "UserJobAdMatch (FK-löst by UserId, ADR 0080) ska hard-raderas vid konto-radering (GDPR Art. 17)");

        var watchesAfter = await verifyDb.CompanyWatches
            .IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.UserId == userId).CountAsync(ct);
        watchesAfter.ShouldBe(0,
            "CompanyWatch (FK-löst by UserId, ADR 0087 D3) ska hard-raderas vid konto-radering (GDPR Art. 17)");

        var hitsAfter = await verifyDb.FollowedCompanyAdHits
            .IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.UserId == userId).CountAsync(ct);
        hitsAfter.ShouldBe(0,
            "FollowedCompanyAdHit (FK-löst by UserId, ADR 0087 D5) ska hard-raderas vid konto-radering (GDPR Art. 17)");
    }

    // ─── Helpers ───

    /// <summary>
    /// Seeds ONE genuinely encrypted <see cref="ParsedResume"/> row for
    /// <paramref name="jobSeekerId"/> against real Postgres. ParsedResume carries
    /// DEK-encrypted columns (<c>raw_text</c> Form A, <c>parsed_content_enc</c> Form B,
    /// ADR 0074 Invariant 3) → the write goes through the field-encryption interceptor,
    /// which fails-closed without a WARM owner DEK. So we warm the owner DEK in-scope
    /// (<see cref="ICurrentDataOwner.SetOwner"/> + <c>GetOrCreateDataKeyAsync</c>) before
    /// <c>SaveChangesAsync</c> — the exact pattern <c>ParsedResumeEncryptionTests</c>
    /// uses. A PII-bearing <c>source_file_name</c> ("CV_Test_Person.pdf") makes the
    /// orphan-leak the test guards against concrete.
    /// </summary>
    private async Task SeedParsedResumeForJobSeekerAsync(JobSeekerId jobSeekerId, CancellationToken ct)
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        using var scope = _fixture.Services.CreateScope();

        // Warm the owner DEK so the field-encryption write interceptor encrypts (not
        // fails-closed). Keyed by JobSeekerId — the data owner of the parsed CV.
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(jobSeekerId);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(jobSeekerId, ct);
        CryptographicOperations.ZeroMemory(dek);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var content = new ParsedResumeContent(
            new ParsedContact("Test Person", "test@example.com", "070-0000000", "Stockholm"),
            profile: "Backend-utvecklare.",
            skills: ["C#", "PostgreSQL"]);

        var confidence = ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["name extracted"]),
        ]);

        var parsed = ParsedResume.Create(
            jobSeekerId,
            "CV_Test_Person.pdf", // plaintext PII column — the orphan-leak this test guards
            "application/pdf",
            ResumeLanguage.Sv,
            content,
            rawText: "Test Person\nBackend-utvecklare",
            confidence,
            PersonnummerScanOutcome.None,
            [],
            clock).Value;

        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds ONE <see cref="Resume"/> (with its initial Master <c>ResumeVersion</c>) for
    /// <paramref name="jobSeekerId"/> against real Postgres. The Master version carries
    /// DEK-encrypted content (<c>content_enc</c> Form B, ADR 0049) → the write goes through
    /// the field-encryption interceptor, which fails-closed without a WARM owner DEK. So we
    /// warm the owner DEK in-scope (<see cref="ICurrentDataOwner.SetOwner"/> +
    /// <c>GetOrCreateDataKeyAsync</c>) before <c>SaveChangesAsync</c> — the same pattern
    /// <see cref="SeedParsedResumeForJobSeekerAsync"/> and <c>ResumeEncryptionTests</c> use.
    /// </summary>
    private async Task SeedResumeForJobSeekerAsync(JobSeekerId jobSeekerId, CancellationToken ct)
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        using var scope = _fixture.Services.CreateScope();

        // Warm the owner DEK so the field-encryption write interceptor encrypts (not
        // fails-closed). Keyed by JobSeekerId — the data owner of the CV.
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(jobSeekerId);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(jobSeekerId, ct);
        CryptographicOperations.ZeroMemory(dek);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var resume = Resume.Create(jobSeekerId, "Mitt CV", "Test Person", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);
    }

    private async Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedSoftDeletedAccountAsync(
        DateTimeOffset deletedAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"hd-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, "HardDeletePass123!");
        result.Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");

        // JobSeeker.Register tar IDateTimeProvider — vi använder en FixedClock
        // för registreringstid och en separat FixedClock för soft-delete.
        var registerClock = new FixedClock(deletedAt.AddDays(-1)); // registrerades före radering
        var seekerResult = JobSeeker.Register(user.Id, "HardDelete Seed", registerClock);
        seekerResult.IsSuccess.ShouldBeTrue();
        var jobSeeker = seekerResult.Value;

        // Soft-delete med fix klocka för att simulera utgånget restore-fönster
        jobSeeker.SoftDelete(new FixedClock(deletedAt));

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);

        return (user.Id, jobSeeker.Id);
    }

    private async Task SeedAuditEntryAsync(Guid userId, Guid aggregateId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = AuditLogEntry.Create(
            occurredAt: DateTimeOffset.UtcNow,
            correlationId: Guid.NewGuid(),
            userId: userId,
            eventType: "Account.Deleted",
            aggregateType: "JobSeeker",
            aggregateId: aggregateId,
            ipAddress: "10.0.0.1",
            userAgent: "TestAgent");

        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    private async Task RunJobAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var hardDeleter = scope.ServiceProvider.GetRequiredService<IAccountHardDeleter>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<HardDeleteAccountsJob>();
        var job = new HardDeleteAccountsJob(hardDeleter, new FixedClock(now), logger);
        await job.RunAsync(ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
