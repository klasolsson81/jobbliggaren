using System.Security.Cryptography;
using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Jobbliggaren.Worker.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// ADR 0164 (#2107) — every notification mail sender through its own gate, on real Postgres: the real scan
/// with <see cref="TopMatchMailDispatcher"/>, and both passes of <see cref="DigestDispatchJob"/>
/// (security-auditor V1). The Top and Strong rows are written by the real scan; a follow hit is seeded by
/// <see cref="FollowedCompanyAdHit.Create"/>, the write <c>CompanyWatchScanJob</c> makes. Every switch is set
/// by the aggregate's own methods, except in the legacy-row test, which names its actor where it writes.
/// <para>
/// The clocks are in 2032, after every other class's in this collection, so no other class's Top row is
/// inside the dispatcher's window, and each test has its own day.
/// </para>
/// </summary>
[Collection("Worker")]
public sealed class NotificationMailGateIntegrationTests(WorkerTestFixture fixture)
{
    private readonly string _run = Guid.NewGuid().ToString("N")[..20];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Cron => BackgroundMatchingWorker.Cron;

    private sealed record Recipient(Guid UserId, string Email);
    private sealed record Mails(int TopMatch, int Summary, int FollowedCompany);
    private sealed record Ads(JobAdId Top, JobAdId Strong, JobAdId FollowedCompany);

    private string Group => $"grp-{_run}";
    private string Region => $"reg-{_run}";
    private string Employment => $"emp-{_run}";
    private string Skill => $"skill-{_run}";

    // architect Kritiskt (#2107 form round). The actor is every row written before migration
    // AddNotificationMailPauses: its writer's Preferences had no pause keys. Each due set is SQL over the
    // jsonb, where a missing key is NULL, so a consenting row of that shape must be in all three.
    [Fact]
    public async Task EverySender_MailsAConsentingRowWrittenBeforeThePauseKeysExisted()
    {
        var at = Occurrence(new DateTimeOffset(2032, 1, 12, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(at, followConsent: true);
        (await HasAnyPauseKeyAsync(user)).ShouldBeTrue();
        await WriteAsBeforeThePauseKeysAsync(user);
        (await HasAnyPauseKeyAsync(user)).ShouldBeFalse();
        var ads = await SeedAdsAsync(at, user);
        var sender = Substitute.For<IEmailSender>();

        await RunMatchingAsync(at, sender);
        await RunDigestAsync(at, sender);

        MailsTo(sender, user).ShouldBe(new Mails(TopMatch: 1, Summary: 1, FollowedCompany: 1));
        (await MatchAsync(user, ads.Top)).NotificationStatus.ShouldBe(NotificationStatus.Sent);
        (await MatchAsync(user, ads.Strong)).NotificationStatus.ShouldBe(NotificationStatus.Sent);
        (await HitStatusAsync(user, ads.FollowedCompany)).ShouldBe(FollowedCompanyAdHitStatus.Sent);
        (await HasAnyPauseKeyAsync(user)).ShouldBeFalse("the senders read the row in its legacy shape");
    }

    // security-auditor V1 and Q2: the pause stops every mail and nothing else. The scan still writes the
    // Top and Strong rows, because the matching runs on the consent, which a pause leaves standing.
    [Fact]
    public async Task EveryNotificationMailPaused_NoSenderMails_ButTheScanStillWritesTheTopAndStrongRows()
    {
        var at = Occurrence(new DateTimeOffset(2032, 2, 9, 10, 0, 0, TimeSpan.Zero));
        var paused = await RegisterAsync(at, followConsent: true,
            (seeker, clock) => seeker.SetNotificationMail(on: false, clock));
        var control = await RegisterAsync(at, followConsent: true);
        var ads = await SeedAdsAsync(at, paused, control);
        var sender = Substitute.For<IEmailSender>();

        await RunMatchingAsync(at, sender);
        await RunDigestAsync(at, sender);

        MailsTo(sender, paused).ShouldBe(new Mails(0, 0, 0));
        MailsTo(sender, control).ShouldBe(new Mails(1, 1, 1));
        var top = await MatchAsync(paused, ads.Top);
        top.Grade.ShouldBe(NotifiableMatchGrade.Top);
        top.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        var strong = await MatchAsync(paused, ads.Strong);
        strong.Grade.ShouldBe(NotifiableMatchGrade.Strong);
        strong.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        (await HitStatusAsync(paused, ads.FollowedCompany)).ShouldBe(FollowedCompanyAdHitStatus.Pending);
        var stored = (await ReadSeekerAsync(paused)).Preferences;
        stored.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        stored.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task EveryNotificationMailPaused_ThenLiftedAfterTheToppmatchWindow_MailsTheHeldSummaryAndFollowRows_NotTheToppmatch()
    {
        var at = Occurrence(new DateTimeOffset(2032, 6, 14, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(at, followConsent: true,
            (seeker, clock) => seeker.SetNotificationMail(on: false, clock));
        var ads = await SeedAdsAsync(at, user);
        var whilePaused = Substitute.For<IEmailSender>();
        await RunMatchingAsync(at, whilePaused);
        await RunDigestAsync(at, whilePaused);
        MailsTo(whilePaused, user).ShouldBe(new Mails(0, 0, 0));

        var later = Occurrence(at + TopMatchMailDispatcher.DirectMailWindow + TimeSpan.FromDays(1));
        await SwitchAsync(user, later, (seeker, clock) => seeker.SetNotificationMail(on: true, clock));
        var afterLift = Substitute.For<IEmailSender>();
        await RunMatchingAsync(later, afterLift);
        await RunDigestAsync(later, afterLift);

        MailsTo(afterLift, user).ShouldBe(new Mails(TopMatch: 0, Summary: 1, FollowedCompany: 1));
        (await MatchAsync(user, ads.Strong)).NotificationStatus.ShouldBe(NotificationStatus.Sent);
        (await HitStatusAsync(user, ads.FollowedCompany)).ShouldBe(FollowedCompanyAdHitStatus.Sent);
        (await MatchAsync(user, ads.Top)).NotificationStatus.ShouldBe(NotificationStatus.Pending);
    }

    // Klas's case (#2107): the Toppmatch mail on, the summary off. The Toppmatch goes, the summary does not,
    // and the Strong row waits in the app.
    [Fact]
    public async Task ToppmatchMailOn_SummaryOff_TheToppmatchIsMailed_AndTheSummaryIsNot()
    {
        var at = Occurrence(new DateTimeOffset(2032, 3, 8, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(at, followConsent: false,
            (seeker, clock) => seeker.SetMatchSummaryMail(on: false, clock));
        var ads = await SeedAdsAsync(at, user);
        var sender = Substitute.For<IEmailSender>();

        await RunMatchingAsync(at, sender);
        await RunDigestAsync(at, sender);

        MailsTo(sender, user).ShouldBe(new Mails(TopMatch: 1, Summary: 0, FollowedCompany: 0));
        (await MatchAsync(user, ads.Top)).NotificationStatus.ShouldBe(NotificationStatus.Sent);
        var strong = await MatchAsync(user, ads.Strong);
        strong.Grade.ShouldBe(NotifiableMatchGrade.Strong);
        strong.NotificationStatus.ShouldBe(NotificationStatus.Pending);
    }

    [Fact]
    public async Task ToppmatchMailOff_NoToppmatchIsMailed_ButTheSummaryIs()
    {
        var at = Occurrence(new DateTimeOffset(2032, 4, 12, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(at, followConsent: false,
            (seeker, clock) => seeker.SetTopMatchMail(on: false, clock));
        var ads = await SeedAdsAsync(at, user);
        var sender = Substitute.For<IEmailSender>();

        await RunMatchingAsync(at, sender);
        await RunDigestAsync(at, sender);

        MailsTo(sender, user).ShouldBe(new Mails(TopMatch: 0, Summary: 1, FollowedCompany: 0));
        var top = await MatchAsync(user, ads.Top);
        top.Grade.ShouldBe(NotifiableMatchGrade.Top);
        top.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        (await MatchAsync(user, ads.Strong)).NotificationStatus.ShouldBe(NotificationStatus.Sent);
    }

    // The follow mail's only switch is its consent (security-auditor V9), so neither match-mail switch
    // reaches it.
    [Fact]
    public async Task ToppmatchMailAndSummaryOff_TheFollowedCompanyMailStillGoes()
    {
        var at = Occurrence(new DateTimeOffset(2032, 5, 10, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(at, followConsent: true, (seeker, clock) =>
        {
            seeker.SetTopMatchMail(on: false, clock);
            seeker.SetMatchSummaryMail(on: false, clock);
        });
        var ads = await SeedAdsAsync(at, user);
        var sender = Substitute.For<IEmailSender>();

        await RunMatchingAsync(at, sender);
        await RunDigestAsync(at, sender);

        MailsTo(sender, user).ShouldBe(new Mails(TopMatch: 0, Summary: 0, FollowedCompany: 1));
        (await HitStatusAsync(user, ads.FollowedCompany)).ShouldBe(FollowedCompanyAdHitStatus.Sent);
        (await MatchAsync(user, ads.Top)).NotificationStatus.ShouldBe(NotificationStatus.Pending);
        (await MatchAsync(user, ads.Strong)).NotificationStatus.ShouldBe(NotificationStatus.Pending);
    }

    // ─────────────────────────── Runs ───────────────────────────

    private static DateTimeOffset Occurrence(DateTimeOffset after) => CronMinuteField.NextAfter(Cron, after);

    private async Task RunMatchingAsync(DateTimeOffset at, IEmailSender sender)
    {
        var scopes = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var clock = new FixedClock(at);
        await new BackgroundMatchingJob(
                scopes,
                new TopMatchMailDispatcher(scopes, sender, clock, NullLogger<TopMatchMailDispatcher>.Instance),
                clock,
                NullLogger<BackgroundMatchingJob>.Instance)
            .RunAsync(Ct);
    }

    private async Task RunDigestAsync(DateTimeOffset at, IEmailSender sender)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await new DigestDispatchJob(
                sp.GetRequiredService<IAppDbContext>(),
                sender,
                sp.GetRequiredService<IUserAccountService>(),
                sp.GetRequiredService<IMatchProfileBuilder>(),
                sp.GetRequiredService<IPerUserJobAdSearchQuery>(),
                new FixedClock(at),
                sp.GetRequiredService<IOptions<DigestDispatchOptions>>(),
                NullLogger<DigestDispatchJob>.Instance)
            .RunAsync(DigestCadence.Weekly, Ct);
    }

    private static Mails MailsTo(IEmailSender sender, Recipient recipient)
    {
        var calls = sender.ReceivedCalls()
            .Where(call => (string?)call.GetArguments()[0] == recipient.Email)
            .ToList();
        var matchMails = calls
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailSender.SendMatchNotificationEmailAsync))
            .Select(call => (MatchNotificationEmail)call.GetArguments()[1]!)
            .ToList();
        return new Mails(
            matchMails.Count(mail => mail.Kind == MatchNotificationKind.Direct),
            matchMails.Count(mail => mail.Kind == MatchNotificationKind.Digest),
            calls.Count(call =>
                call.GetMethodInfo().Name == nameof(IEmailSender.SendFollowedCompanyNotificationEmailAsync)));
    }

    // ─────────────────────────── Seeding ───────────────────────────

    private async Task<Recipient> RegisterAsync(
        DateTimeOffset at, bool followConsent, Action<JobSeeker, IDateTimeProvider>? switches = null)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var email = $"mail-gate-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        (await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded.ShouldBeTrue();

        var clock = new FixedClock(at.AddHours(-1));
        var seeker = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        var preferences = MatchPreferences.Create([Group], [Region], [Employment], preferredSkills: [Skill]);
        preferences.IsSuccess.ShouldBeTrue();
        seeker.UpdateMatchPreferences(preferences.Value, clock);
        seeker.UpdateNotificationConsent(enabled: true, clock);
        if (followConsent)
            seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, clock);
        switches?.Invoke(seeker, clock);

        var db = sp.GetRequiredService<AppDbContext>();
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(Ct);
        return new Recipient(user.Id, email);
    }

    private async Task SwitchAsync(Recipient recipient, DateTimeOffset at, Action<JobSeeker, IDateTimeProvider> switches)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(stored => stored.UserId == recipient.UserId, Ct);
        switches(seeker, new FixedClock(at));
        await db.SaveChangesAsync(Ct);
    }

    // A Top ad and a Strong ad for this test's profile, and an ad of a company each recipient follows with a
    // Pending hit on it. Strong is Top without the skill signal: the must-have is met and both secondaries
    // are confirmed, but the ad names the skill only as a requirement.
    private async Task<Ads> SeedAdsAsync(DateTimeOffset at, params Recipient[] recipients)
    {
        var ingestedAt = at.AddMinutes(-3);
        var top = await SaveAsync(Import(Group, Region, Employment, MustHaveSkill(), ingestedAt));
        var strong = await SaveAsync(Import(Group, Region, Employment, MustHaveOnly(), ingestedAt));
        var organizationNumber = "559" + RandomNumberGenerator.GetInt32(1_000_000, 10_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var followed = await SaveAsync(Import(
            $"none-{_run}", region: null, employment: null, TestKeywordExtraction.None, ingestedAt, organizationNumber));

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(ingestedAt);
        var number = OrganizationNumber.Create(organizationNumber);
        number.IsSuccess.ShouldBeTrue();
        number.Value.IsPersonnummerShaped().ShouldBeFalse();
        foreach (var recipient in recipients)
        {
            var watch = CompanyWatch.Follow(recipient.UserId, number.Value, clock);
            watch.IsSuccess.ShouldBeTrue();
            var hit = FollowedCompanyAdHit.Create(recipient.UserId, followed, watch.Value.Id, clock);
            hit.IsSuccess.ShouldBeTrue();
            db.CompanyWatches.Add(watch.Value);
            db.FollowedCompanyAdHits.Add(hit.Value);
        }

        await db.SaveChangesAsync(Ct);
        return new Ads(top, strong, followed);
    }

    // The pair JobAdKeywordExtractor writes when the employer states a must-have skill the text also names.
    private JobAdTermExtraction MustHaveSkill() => TestKeywordExtraction.Returning(ExtractedTerms.From(
    [
        new ExtractedTerm(Skill, "Testkompetens", ExtractedTermKind.Skill,
            ExtractedTermSource.Description, "Testkompetens", Skill, 1),
        MustHaveTerm(),
    ]));

    // The requirement pass alone: JobAdKeywordExtractor writes it for any employer-stated must-have, and the
    // skill pass adds nothing when the text does not name the skill.
    private JobAdTermExtraction MustHaveOnly() => TestKeywordExtraction.Returning(ExtractedTerms.From([MustHaveTerm()]));

    private ExtractedTerm MustHaveTerm() => new(Skill, "Testkompetens", ExtractedTermKind.Requirement,
        ExtractedTermSource.MustHave, "Testkompetens", Skill, 1);

    private static JobAd Import(
        string group,
        string? region,
        string? employment,
        JobAdTermExtraction extract,
        DateTimeOffset ingestedAt,
        string? organizationNumber = null)
    {
        var externalId = $"mail-gate-{Guid.NewGuid():N}";
        var fields = new Dictionary<string, object?>
        {
            ["id"] = externalId,
            ["occupation_group"] = new { concept_id = group },
            ["workplace_address"] = region is null ? null : new { region_concept_id = region },
            ["employment_type"] = employment is null ? null : new { concept_id = employment },
        };
        if (organizationNumber is not null)
            fields["employer"] = new { name = "Test AB", organization_number = organizationNumber };
        var payload = JsonSerializer.Serialize(fields);
        var imported = JobAd.Import("Testtjänst", Company.Create("Test AB").Value, "beskrivning",
            $"https://example.test/jobs/{externalId}",
            ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload, facets: TestFacets.FromPayload(payload), declaredContacts: [],
            publishedAt: ingestedAt.AddDays(-1), expiresAt: ingestedAt.AddDays(60),
            clock: new FixedClock(ingestedAt), extractTerms: extract);
        imported.IsSuccess.ShouldBeTrue();
        return imported.Value;
    }

    private async Task<JobAdId> SaveAsync(JobAd ad)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobAds.Add(ad);
        await db.SaveChangesAsync(Ct);
        return ad.Id;
    }

    // The actor: every row written before migration AddNotificationMailPauses, whose writer's Preferences
    // had no pause keys. The current writer emits them (pinned by
    // PreferencesConsentBackcompatTests.CurrentWriter_EmitsEveryPreferencesParameterAsAKey), so that row is
    // this one without them.
    private async Task WriteAsBeforeThePauseKeysAsync(Recipient recipient)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var written = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE job_seekers
            SET preferences = preferences - 'NotificationMailOffAt' - 'TopMatchMailOffAt' - 'MatchSummaryMailOffAt'
            WHERE user_id = {recipient.UserId}
            """,
            Ct);
        written.ShouldBe(1);
    }

    private async Task<bool> HasAnyPauseKeyAsync(Recipient recipient)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQuery<bool>(
                $"""
                SELECT jsonb_exists_any(
                    preferences, ARRAY['NotificationMailOffAt', 'TopMatchMailOffAt', 'MatchSummaryMailOffAt']) AS "Value"
                FROM job_seekers
                WHERE user_id = {recipient.UserId}
                """)
            .SingleAsync(Ct);
    }

    // ─────────────────────────── Read-back ───────────────────────────

    private async Task<UserJobAdMatch> MatchAsync(Recipient recipient, JobAdId ad)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserJobAdMatches.AsNoTracking()
            .SingleAsync(match => match.UserId == recipient.UserId && match.JobAdId == ad, Ct);
    }

    private async Task<FollowedCompanyAdHitStatus> HitStatusAsync(Recipient recipient, JobAdId ad)
    {
        using var scope = fixture.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().FollowedCompanyAdHits
                .AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(hit => hit.UserId == recipient.UserId && hit.JobAdId == ad, Ct))
            .NotificationStatus;
    }

    private async Task<JobSeeker> ReadSeekerAsync(Recipient recipient)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.AsNoTracking()
            .SingleAsync(seeker => seeker.UserId == recipient.UserId, Ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
