using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// Per-user isolation in <see cref="DigestDispatchJob"/>, against REAL Postgres. One context serves
/// the whole run, so a claim whose commit fails leaves that user's rows modified in it; they must not
/// ride into the next user's save and commit there as Queued, with no email ever sent for them.
/// <para>
/// The failed commit is a plpgsql BEFORE UPDATE trigger on the pass's table that raises on the first
/// update of either seeker's rows and never again: a sequence counts the updates, and a sequence
/// value survives the rollback. So the first of the pair the job claims fails, whichever it is, and
/// the second claims, sends and drains. The container is shared across the serial Worker collection,
/// so the trigger, its function and the sequence carry a per-test suffix and are dropped in a
/// <c>finally</c>.
/// </para>
/// </summary>
[Collection("Worker")]
public class DigestDispatchJobPoisonIsolationTests(WorkerTestFixture fixture)
{
    public enum Pass
    {
        Match,
        Follow,
    }

    private readonly WorkerTestFixture _fixture = fixture;

    private readonly string _run = Guid.NewGuid().ToString("N")[..20];

    // Postgres identifiers must start with a letter; the hex suffix may not.
    private string PoisonFn => $"digest_poison_fn_{_run}";
    private string PoisonTrg => $"digest_poison_trg_{_run}";
    private string PoisonSeq => $"digest_poison_seq_{_run}";

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Pass.Match)]
    [InlineData(Pass.Follow)]
    public async Task RunAsync_WhenOneUsersClaimFailsToCommit_ItsRowsStayPendingAndTheNextUserIsSent(Pass pass)
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await SeedSeekerAsync(pass, ct);
        var b = await SeedSeekerAsync(pass, ct);
        var table = pass == Pass.Match ? "user_job_ad_matches" : "followed_company_ad_hits";

        var sentTo = new List<string>();
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                sentTo.Add(ci.ArgAt<string>(0));
                return Task.CompletedTask;
            });
        emailSender.SendFollowedCompanyNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                sentTo.Add(ci.ArgAt<string>(0));
                return Task.CompletedTask;
            });

        await InstallPoisonTriggerAsync(table, a.UserId, b.UserId, ct);
        try
        {
            await RunJobAsync(emailSender, ct);
        }
        finally
        {
            await DropPoisonTriggerAsync(table);
        }

        var sentToThePair = sentTo.Where(to => to == a.Email || to == b.Email).ToList();
        sentToThePair.Count.ShouldBe(1, "the first of the pair fails its claim; only the other is sent");
        var sent = sentToThePair[0] == a.Email ? a : b;
        var failed = sent == a ? b : a;

        const string FailedClaimStays = "the failed claim is rolled back, and no later save commits it";
        if (pass == Pass.Match)
        {
            (await MatchStatusAsync(sent.UserId, ct)).ShouldBe(NotificationStatus.Sent);
            (await MatchStatusAsync(failed.UserId, ct)).ShouldBe(NotificationStatus.Pending, FailedClaimStays);
        }
        else
        {
            (await HitStatusAsync(sent.UserId, ct)).ShouldBe(FollowedCompanyAdHitStatus.Sent);
            (await HitStatusAsync(failed.UserId, ct)).ShouldBe(FollowedCompanyAdHitStatus.Pending, FailedClaimStays);
        }
    }

    // ─────────────────────────── SUT ───────────────────────────

    private async Task RunJobAsync(IEmailSender emailSender, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var job = new DigestDispatchJob(
            sp.GetRequiredService<IAppDbContext>(),
            emailSender,
            sp.GetRequiredService<IUserAccountService>(),
            sp.GetRequiredService<IMatchProfileBuilder>(),
            sp.GetRequiredService<IPerUserJobAdSearchQuery>(),
            new FixedClock(Now),
            sp.GetRequiredService<IOptions<DigestDispatchOptions>>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<DigestDispatchJob>());

        await job.RunAsync(DigestCadence.Weekly, ct);
    }

    // ─────────────────────────── Poison trigger (install / drop) ───────────────────────────

    // DDL with test-generated identifiers and uuids, which cannot be parameterised; plain `string`
    // locals keep EF1002 satisfied (parity BackgroundMatchingJobPoisonIsolationTests).
    private async Task InstallPoisonTriggerAsync(string table, Guid a, Guid b, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        string createSequence = $"CREATE SEQUENCE {PoisonSeq};";
        string createFunction =
            $@"CREATE OR REPLACE FUNCTION {PoisonFn}() RETURNS trigger AS $fn$
BEGIN
    IF nextval('{PoisonSeq}') = 1 THEN
        RAISE EXCEPTION 'digest poison {_run}';
    END IF;
    RETURN NEW;
END;
$fn$ LANGUAGE plpgsql;";
        string createTrigger =
            $@"CREATE TRIGGER {PoisonTrg} BEFORE UPDATE ON {table}
    FOR EACH ROW WHEN (OLD.user_id IN ('{a}'::uuid, '{b}'::uuid))
    EXECUTE FUNCTION {PoisonFn}();";

        await db.Database.ExecuteSqlRawAsync(createSequence, ct);
        await db.Database.ExecuteSqlRawAsync(createFunction, ct);
        await db.Database.ExecuteSqlRawAsync(createTrigger, ct);
    }

    // No CancellationToken: a leaked trigger would fail later tests in the shared collection.
    private async Task DropPoisonTriggerAsync(string table)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string dropTrigger = $"DROP TRIGGER IF EXISTS {PoisonTrg} ON {table};";
        string dropFunction = $"DROP FUNCTION IF EXISTS {PoisonFn}();";
        string dropSequence = $"DROP SEQUENCE IF EXISTS {PoisonSeq};";
        await db.Database.ExecuteSqlRawAsync(dropTrigger);
        await db.Database.ExecuteSqlRawAsync(dropFunction);
        await db.Database.ExecuteSqlRawAsync(dropSequence);
    }

    // ─────────────────────────── Seeding + read-back ───────────────────────────

    private sealed record Seeker(Guid UserId, string Email);

    // A consenting Weekly seeker with a real Identity user and one Pending row for the pass: a Strong
    // match, or a hit on a followed company's Active ad.
    private async Task<Seeker> SeedSeekerAsync(Pass pass, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var email = $"digest-poison-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();

        var seeker = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);
        var jobAd = NewActiveAd($"digest-poison-{Guid.NewGuid():N}", orgNr);
        db.JobAds.Add(jobAd);

        if (pass == Pass.Match)
        {
            seeker.UpdateNotificationConsent(enabled: true, clock);
            db.UserJobAdMatches.Add(UserJobAdMatch.Create(
                user.Id, jobAd.Id, NotifiableMatchGrade.Strong, [], clock).Value);
        }
        else
        {
            seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, clock);
            var watch = CompanyWatch.Follow(user.Id, OrganizationNumber.Create(orgNr).Value, clock).Value;
            db.CompanyWatches.Add(watch);
            db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(user.Id, jobAd.Id, watch.Id, clock).Value);
        }

        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return new Seeker(user.Id, email);
    }

    private static JobAd NewActiveAd(string externalId, string orgNr)
    {
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"employer\":{{\"name\":\"Acme AB\",\"organization_number\":\"{orgNr}\"}}}}";
        return JobAd.Import(
            title: "Isoleringsannons",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: new FixedClock(Now), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
    }

    private async Task<NotificationStatus> MatchStatusAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var match = await db.UserJobAdMatches.AsNoTracking().SingleAsync(m => m.UserId == userId, ct);
        return match.NotificationStatus;
    }

    private async Task<FollowedCompanyAdHitStatus> HitStatusAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hit = await db.FollowedCompanyAdHits.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(h => h.UserId == userId, ct);
        return hit.NotificationStatus;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
