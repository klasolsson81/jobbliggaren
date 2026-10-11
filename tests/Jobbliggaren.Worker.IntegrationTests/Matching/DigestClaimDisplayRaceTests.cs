using System.Data.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// #2118 — the Strong digest lists exactly the rows it claims. The matching run writes Strong rows every
/// five minutes and is not serialised against the digest (ADR 0161), so a row can commit while the digest
/// is reading. A row the digest did not claim must not be listed: it stays Pending and is mailed once, by
/// the next digest. The interleaving is the scan's own write, <see cref="UserJobAdMatch.Create"/>, committed
/// right after the digest's first read of <c>user_job_ad_matches</c>.
/// </summary>
[Collection("Worker")]
public sealed class DigestClaimDisplayRaceTests(WorkerTestFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_AStrongRowCommittedDuringTheDigestRead_IsNeitherListedNorClaimed_AndIsMailedNextRun()
    {
        var (userId, email) = await SeedConsentingWeeklyUserAsync();
        var earlyAd = await SeedActiveAdAsync("Tidig roll", "Tidig AB");
        var lateAd = await SeedActiveAdAsync("Sen roll", "Sen AB");
        await AddStrongMatchAsync(userId, earlyAd, Ct);

        var interleave = new CommitAfterFirstMatchRead(userId, ct => AddStrongMatchAsync(userId, lateAd, ct));
        var firstRun = Substitute.For<IEmailSender>();
        await RunDigestAsync(firstRun, interleave);

        interleave.Fired.ShouldBe(1, "the scan's row must commit inside the digest's run");
        var firstMail = MailTo(firstRun, email);
        firstMail.Items.Select(item => item.JobAdId).ShouldBe([earlyAd.Value],
            "a row the digest did not claim must not be listed");
        firstMail.TotalCount.ShouldBe(1);
        (await StatusAsync(userId, earlyAd)).ShouldBe(NotificationStatus.Sent);
        (await StatusAsync(userId, lateAd)).ShouldBe(NotificationStatus.Pending,
            "the late row was not claimed, so it waits for the next digest");

        var secondRun = Substitute.For<IEmailSender>();
        await RunDigestAsync(secondRun, interceptor: null);

        MailTo(secondRun, email).Items.Select(item => item.JobAdId).ShouldBe([lateAd.Value]);
        (await StatusAsync(userId, lateAd)).ShouldBe(NotificationStatus.Sent);
    }

    private async Task RunDigestAsync(IEmailSender sender, IInterceptor? interceptor)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var options = new DbContextOptionsBuilder<AppDbContext>(sp.GetRequiredService<DbContextOptions<AppDbContext>>());
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        await using var db = new AppDbContext(options.Options);
        await new DigestDispatchJob(db, sender, sp.GetRequiredService<IUserAccountService>(),
            sp.GetRequiredService<IMatchProfileBuilder>(), sp.GetRequiredService<IPerUserJobAdSearchQuery>(),
            new FixedClock(Now), sp.GetRequiredService<IOptions<DigestDispatchOptions>>(),
            NullLogger<DigestDispatchJob>.Instance).RunAsync(DigestCadence.Weekly, Ct);
    }

    private static MatchNotificationEmail MailTo(IEmailSender sender, string email) =>
        sender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailSender.SendMatchNotificationEmailAsync)
                           && call.GetArguments()[0] is string recipient && recipient == email)
            .Select(call => (MatchNotificationEmail)call.GetArguments()[1]!)
            .ShouldHaveSingleItem();

    private async Task<(Guid UserId, string Email)> SeedConsentingWeeklyUserAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var email = $"digest-race-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        (await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded.ShouldBeTrue();

        var clock = new FixedClock(Now);
        var jobSeeker = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        jobSeeker.UpdateNotificationConsent(enabled: true, clock);
        var db = sp.GetRequiredService<AppDbContext>();
        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(Ct);
        return (user.Id, email);
    }

    private async Task<JobAdId> SeedActiveAdAsync(string title, string company)
    {
        using var scope = fixture.Services.CreateScope();
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
            clock: new FixedClock(Now), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id;
    }

    private async Task AddStrongMatchAsync(Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserJobAdMatches.Add(UserJobAdMatch.Create(
            userId, jobAdId, NotifiableMatchGrade.Strong, [], new FixedClock(Now)).Value);
        await db.SaveChangesAsync(ct);
    }

    private async Task<NotificationStatus> StatusAsync(Guid userId, JobAdId jobAdId)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.UserJobAdMatches.AsNoTracking()
            .SingleAsync(match => match.UserId == userId && match.JobAdId == jobAdId, Ct)).NotificationStatus;
    }

    /// <summary>
    /// Commits the scan's write once, right after the first read of the user's rows in the match table. The
    /// fixture's database is shared, so the read is recognised by the user's id among its parameters.
    /// </summary>
    private sealed class CommitAfterFirstMatchRead(Guid userId, Func<CancellationToken, Task> commit) : DbCommandInterceptor
    {
        public int Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Fired == 0
                && command.CommandText.Contains("user_job_ad_matches", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == userId))
            {
                Fired++;
                await commit(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
