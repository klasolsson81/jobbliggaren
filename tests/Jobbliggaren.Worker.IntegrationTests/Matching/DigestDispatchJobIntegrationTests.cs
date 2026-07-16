using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// ADR 0080 Vag 4 PR-4b — Testcontainers integration test for <see cref="DigestDispatchJob"/> +
/// <see cref="DigestDispatchWorker"/> against REAL Postgres + the fixture's ConsoleEmailSender
/// (registered because the test env is "Test" — a real send is never attempted). This proves the
/// Worker DI resolves the NEW digest jobs (the Worker runs ValidateOnBuild=false / TD-103, so a
/// missing dependency would only surface at Hangfire-invocation — this test IS that invocation) and
/// that the claim-then-drain transition commits against real Postgres (the consent predicate + the
/// Grade+Pending filter + the JobAd join run against the real jsonb preferences column / generated
/// shadow columns, all hidden by EF-InMemory). Mirrors
/// <see cref="BackgroundMatchingJobIntegrationTests"/> seeding.
/// <para>
/// <b>Email recipient:</b> <see cref="IUserAccountService.GetEmailAsync"/> reads
/// <see cref="UserManager{TUser}"/>, so the test seeds a real <see cref="ApplicationUser"/> and
/// registers the <see cref="JobSeeker"/> with that user's id — otherwise the digest would find no
/// recipient and leave the row Queued (the no-email path is unit-pinned separately).
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class DigestDispatchJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // A fixed digest run instant (the cadence run time; ~06:00 UTC after the 03:20 scan).
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunWeeklyAsync_ResolvesFromDI_AndDrainsConsentingWeeklyUsersStrongMatch()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed a real Identity user (so GetEmailAsync resolves a recipient) and a consenting Weekly
        // JobSeeker keyed by that user id.
        var (userId, _) = await SeedConsentingWeeklyUserAsync(ct);
        var jobAdId = await SeedActiveAdAsync("Backend-utvecklare", "Acme AB", ct);
        await SeedStrongPendingMatchAsync(userId, jobAdId, ct);

        // Resolve the Hangfire wrapper from the fixture SP (parity Worker/Program.cs) and run the
        // weekly entry point end-to-end — proves the whole DI chain (DigestDispatchWorker →
        // DigestDispatchJob → IEmailSender + IUserAccountService + IOptions<DigestDispatchOptions>)
        // resolves and runs without throwing.
        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        });

        // The Strong row transitioned Pending → Sent (claimed + drained against real Postgres).
        var match = await GetMatchAsync(userId, jobAdId, ct);
        match.ShouldNotBeNull("den seedade Strong-matchen ska finnas");
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent,
            "en consenting Weekly-users Strong-match ska digesteras och markeras Sent");
    }

    /// <summary>
    /// #864 — the TRANSLATION oracle for the digest's lifecycle gate. The display joins now carry
    /// <c>where j.Status == JobAdStatus.Active</c> and the total comes from a separate
    /// <c>CountAsync</c> over that same gated query. Both cross the <c>JobAdStatus</c> SmartEnum
    /// <c>HasConversion</c>, and EF-InMemory (where the email-content specs live) evaluates that
    /// comparison in LINQ-to-objects — so a shape that does NOT translate to SQL would be green there
    /// and throw in production. Only a relational provider can tell us. This test is that provider.
    /// <para>
    /// It also pins the DRAIN half against real Postgres: the archived match is excluded from the
    /// email but MUST still transition Pending → Sent. The gate is on the DISPLAY set, never on the
    /// CLAIM set — gate the claim and an archived row stays Pending and is re-processed on every
    /// digest run, forever. The email CONTENT (items, TotalCount) is asserted in
    /// <c>DigestDispatchJobTests</c>, where the sender is observable; this fixture uses
    /// ConsoleEmailSender.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RunWeeklyAsync_ArchivedAdMatch_IsExcludedFromDisplay_ButStillDrained()
    {
        var ct = TestContext.Current.CancellationToken;

        var (userId, _) = await SeedConsentingWeeklyUserAsync(ct);
        var liveAdId = await SeedActiveAdAsync("Aktiv roll", "Live AB", ct);
        var archivedAdId = await SeedActiveAdAsync("Arkiverad roll", "Gone AB", ct);
        await SeedStrongPendingMatchAsync(userId, liveAdId, ct);
        await SeedStrongPendingMatchAsync(userId, archivedAdId, ct);
        await ArchiveAdAsync(archivedAdId, ct);

        // If the gated join or the count query failed to translate, this throws (it does not throw
        // under InMemory — that is the whole point of running it here).
        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        });

        // NON-VACUITY: the ACTIVE match drains — so the run really did dispatch a digest, rather than
        // taking the "nothing presentable" early-return that would make the next assertion hollow.
        var live = await GetMatchAsync(userId, liveAdId, ct);
        live.ShouldNotBeNull();
        live.NotificationStatus.ShouldBe(NotificationStatus.Sent,
            "den aktiva matchen ska digesteras och markeras Sent");

        // The archived match is NOT emailed (content-asserted in the unit suite) but IS drained.
        var archived = await GetMatchAsync(userId, archivedAdId, ct);
        archived.ShouldNotBeNull();
        archived.NotificationStatus.ShouldBe(NotificationStatus.Sent,
            "den arkiverade matchen måste dräneras — grinden sitter på VISNINGS-mängden, inte på " +
            "claim-mängden; annars ligger raden kvar Pending och om-processas varje körning");
    }

    [Fact]
    public async Task RunDailyAsync_ResolvesFromDI_AndDoesNotThrow_WhenNoDueUsers()
    {
        var ct = TestContext.Current.CancellationToken;

        // No daily-cadence consenting users seeded → the daily run is a clean no-op. The point is the
        // DI resolution + a green run (DigestDispatchJob/Worker registered + IOptions validated).
        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunDailyAsync(ct);
        });
    }

    // ─────────────────────────── Seeding helpers (parity BackgroundMatchingJobIntegrationTests)

    private async Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedConsentingWeeklyUserAsync(
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var email = $"digest-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        var created = await userManager.CreateAsync(user, "DigestPass123!");
        created.Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");

        var jobSeeker = JobSeeker.Register(user.Id, "Digest Seed", clock).Value;
        jobSeeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, clock);
        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);
        return (user.Id, jobSeeker.Id);
    }

    private async Task<JobAdId> SeedActiveAdAsync(string title, string company, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = $"ext-{Guid.NewGuid():N}";
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.FromPayload($"{{\"id\":\"{externalId}\"}}"),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: new FixedClock(Now), declaredContacts: []).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task SeedStrongPendingMatchAsync(Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var match = UserJobAdMatch.Create(
            userId, jobAdId, NotifiableMatchGrade.Strong, [], new FixedClock(Now)).Value;
        db.UserJobAdMatches.Add(match);
        await db.SaveChangesAsync(ct);
    }

    // Archives via the DOMAIN transition production performs (ExpireJobAdsJob writes this same status).
    // Never a fabricated column value — that fiction (#843) is exactly what kept #864 alive.
    private async Task ArchiveAdAsync(JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.FirstAsync(j => j.Id == jobAdId, ct);
        ad.Archive(new FixedClock(Now)).IsSuccess.ShouldBeTrue("seed: annonsen ska gå att arkivera");
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserJobAdMatch?> GetMatchAsync(Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
