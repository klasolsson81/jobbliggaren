using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Domain.Common;
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
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// ADR 0161 (#2106) — the Toppmatch mail end to end on real Postgres: the real scan, scorer and
/// <see cref="TopMatchMailDispatcher"/>, run at consecutive occurrences of
/// <see cref="BackgroundMatchingWorker.Cron"/>. Every ad is built by <c>JobAd.Import</c> with the clock
/// at its ingest time, the same stamp <c>UpsertExternalJobAdCommandHandler</c> gives a new ad.
/// <para>
/// The container is shared with the whole collection, so every test has its own concept ids and
/// counts mails per address. The clocks are set in 2031 so that no other class's rows come before
/// these in the dispatcher's oldest-first order and spend the run's cap of 20 mails.
/// </para>
/// </summary>
[Collection("Worker")]
public sealed class BackgroundMatchingJobTopMatchMailTests(WorkerTestFixture fixture)
{
    private readonly string _run = Guid.NewGuid().ToString("N")[..20];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Cron => BackgroundMatchingWorker.Cron;

    private sealed record Profile(string Group, string Region, string Employment, string Skill);
    private sealed record Recipient(Guid UserId, string Email);

    private Profile Mine => new($"grp-{_run}", $"reg-{_run}", $"emp-{_run}", $"skill-{_run}");
    private Profile Another => new($"grp-{_run}b", $"reg-{_run}b", $"emp-{_run}b", $"skill-{_run}b");

    // ADR 0161 D8: two consecutive runs over the same ads mail each (user, ad) pair once.
    [Fact]
    public async Task RunAsync_TwoConsecutiveRuns_MailEachUserAndAdOnce()
    {
        var first = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 2, 3, 10, 0, 0, TimeSpan.Zero));
        var second = CronMinuteField.NextAfter(Cron, first);
        var userA = await RegisterAsync(Mine, first.AddHours(-1));
        var userB = await RegisterAsync(Mine, first.AddHours(-1));
        var settled = await SaveAsync(ImportTopAd(Mine, first.AddMinutes(-3)));
        var insideTheWindow = await SaveAsync(ImportTopAd(Mine, first.AddMinutes(-1)));
        var sender = Substitute.For<IEmailSender>();

        await RunAtAsync(first, sender);
        await RunAtAsync(second, sender);

        foreach (var user in new[] { userA, userB })
        {
            var mail = MailsTo(sender, user).ShouldHaveSingleItem();
            mail.Kind.ShouldBe(MatchNotificationKind.Direct);
            mail.Items.Select(item => item.JobAdId)
                .ShouldBe([settled.Value, insideTheWindow.Value], ignoreOrder: true);
            foreach (var ad in new[] { settled, insideTheWindow })
            {
                var match = (await MatchesAsync(user, ad)).ShouldHaveSingleItem();
                match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
                match.SentAt.ShouldBe(first);
            }
        }
    }

    // ADR 0161 D2: the watermark trails the clock, so the next run reads an ad ingested inside the
    // settle window again and scores it, and the existing-pair check keeps it to one row and one mail.
    [Fact]
    public async Task RunAsync_AdInsideTheSettleWindow_IsScoredAgainByTheNextRun_WithoutANewRowOrASecondMail()
    {
        var first = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 2, 17, 10, 0, 0, TimeSpan.Zero));
        var second = CronMinuteField.NextAfter(Cron, first);
        var user = await RegisterAsync(Mine, first.AddHours(-1));
        var ad = await SaveAsync(ImportTopAd(Mine, first - BackgroundMatchingJob.IngestSettleWindow / 2));
        var sender = Substitute.For<IEmailSender>();
        using var scorerScope = fixture.Services.CreateScope();
        var scorer = new RecordingScorer(scorerScope.ServiceProvider.GetRequiredService<IMatchScorer>());
        var scopes = new OverridingScopeFactory(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new Dictionary<Type, object> { [typeof(IMatchScorer)] = scorer });

        await RunAtAsync(first, sender, scopes);
        var scoredInTheFirstRun = scorer.BatchesFor(Mine).Count;
        await RunAtAsync(second, sender, scopes);

        scoredInTheFirstRun.ShouldBe(1);
        scorer.BatchesFor(Mine).Count.ShouldBe(2);
        scorer.BatchesFor(Mine)[^1].ShouldContain(ad);
        (await MatchesAsync(user, ad)).ShouldHaveSingleItem().NotificationStatus.ShouldBe(NotificationStatus.Sent);
        MailsTo(sender, user).ShouldHaveSingleItem();
        (await WatermarkAsync(user)).ShouldBe(second - BackgroundMatchingJob.IngestSettleWindow);
    }

    // ADR 0161 D10 pin 2. The ad is ingested a second after a run, the worst case for an ad no run
    // raced, and the user was scanned by that run, since a cold-start user is outside the bound (D3).
    [Fact]
    public async Task RunAsync_AtTheFirstOccurrenceAfterAnAdsIngest_MailsItOnceWithinTheBound()
    {
        var previousRun = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 3, 3, 10, 0, 0, TimeSpan.Zero));
        var ingestedAt = previousRun.AddSeconds(1);
        var nextRun = CronMinuteField.NextAfter(Cron, ingestedAt);
        var user = await RegisterAsync(Mine, previousRun.AddHours(-1));
        await SaveAsync(ImportUnrelatedAd(previousRun.AddMinutes(-4)));
        var sender = Substitute.For<IEmailSender>();
        await RunAtAsync(previousRun, sender);
        (await WatermarkAsync(user)).ShouldBe(previousRun - BackgroundMatchingJob.IngestSettleWindow);

        var ad = await SaveAsync(ImportTopAd(Mine, ingestedAt));
        await RunAtAsync(nextRun, sender);

        var mail = MailsTo(sender, user).ShouldHaveSingleItem();
        mail.Kind.ShouldBe(MatchNotificationKind.Direct);
        mail.Items.ShouldHaveSingleItem().JobAdId.ShouldBe(ad.Value);
        var match = (await MatchesAsync(user, ad)).ShouldHaveSingleItem();
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
        (match.SentAt.ShouldNotBeNull() - ingestedAt).ShouldBeLessThanOrEqualTo(BackgroundMatchingJob.TopMatchMailBound);
    }

    // ADR 0161 D10 pin 3, the race D2 closes. JobAd.Import stamps CreatedAt when it builds the ad, and
    // UpsertExternalJobAdCommandHandler commits it afterwards; here the ad is stamped 30 s before run k
    // and committed after run k's read. Run k still advances the user's watermark, over another ad.
    [Fact]
    public async Task RunAsync_AdStampedBeforeARunButCommittedAfterItsRead_IsMailedByTheNextRunWithinTheBound()
    {
        var runK = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 3, 17, 10, 0, 0, TimeSpan.Zero));
        var runKPlusOne = CronMinuteField.NextAfter(Cron, runK);
        var stampedAt = runK.AddSeconds(-30);
        var user = await RegisterAsync(Mine, runK.AddHours(-1));
        await SaveAsync(ImportUnrelatedAd(runK.AddMinutes(-3)));
        var sender = Substitute.For<IEmailSender>();

        var stampedNotCommitted = ImportTopAd(Mine, stampedAt);
        await RunAtAsync(runK, sender);
        var watermark = await WatermarkAsync(user);
        var ad = await SaveAsync(stampedNotCommitted);

        (await MatchesAsync(user, ad)).ShouldBeEmpty();
        MailsTo(sender, user).ShouldBeEmpty();
        watermark.ShouldBe(runK - BackgroundMatchingJob.IngestSettleWindow);
        stampedAt.ShouldBeGreaterThan(watermark!.Value, "the trailing watermark leaves the ad readable");
        stampedAt.ShouldBeLessThanOrEqualTo(runK, "a watermark advanced to run k's clock would have skipped it for good");

        await RunAtAsync(runKPlusOne, sender);

        MailsTo(sender, user).ShouldHaveSingleItem().Items.ShouldHaveSingleItem().JobAdId.ShouldBe(ad.Value);
        var match = (await MatchesAsync(user, ad)).ShouldHaveSingleItem();
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
        (match.SentAt.ShouldNotBeNull() - stampedAt).ShouldBeLessThanOrEqualTo(BackgroundMatchingJob.TopMatchMailBound);
    }

    // security-auditor V2 through the real scan: a never-scanned user's first run reaches ColdStartDays
    // back, so it writes a fresh Top row for an ad ingested three days ago. The row is not mailed as
    // news. The control's fresh ad shows that the same run's send phase mails.
    [Fact]
    public async Task RunAsync_FreshTopMatchForAnAdIngestedThreeDaysAgo_IsPersistedButNotMailed()
    {
        var now = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 3, 31, 10, 0, 0, TimeSpan.Zero));
        var target = await RegisterAsync(Mine, now.AddMinutes(-1));
        var control = await RegisterAsync(Another, now.AddMinutes(-1));
        var oldAd = await SaveAsync(ImportTopAd(Mine, now.AddDays(-3)));
        var freshAd = await SaveAsync(ImportTopAd(Another, now.AddMinutes(-3)));
        var sender = Substitute.For<IEmailSender>();

        await RunAtAsync(now, sender);

        var match = (await MatchesAsync(target, oldAd)).ShouldHaveSingleItem();
        match.Grade.ShouldBe(NotifiableMatchGrade.Top);
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        MailsTo(sender, target).ShouldBeEmpty();
        MailsTo(sender, control).ShouldHaveSingleItem().Items.ShouldHaveSingleItem().JobAdId.ShouldBe(freshAd.Value);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.JobAds.SingleAsync(ad => ad.Id == oldAd, Ct)).Archive(new FixedClock(now)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    // ADR 0170 (#2154): a preference the user left empty is open to every ad, so it confirms. A user who
    // states an occupation, the skill the ad requires and at most one of ort / anställningsform now gets
    // a Toppmatch, and the direct mail, for an ad that carries ANY value on the dimension left empty.
    // Before ADR 0170 the empty dimension confirmed nothing and the same ad graded Strong (the digest).
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task RunAsync_UserWhoLeftAPreferenceEmpty_IsMailedATopMatch_WhateverTheAdCarriesThere(
        bool statesRegion, bool statesEmployment)
    {
        var run = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 4, 7, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(Mine, run.AddHours(-1), statesRegion, statesEmployment);
        var ad = await SaveAsync(ImportTopAd(
            Mine,
            run.AddMinutes(-3),
            region: statesRegion ? Mine.Region : Another.Region,
            employment: statesEmployment ? Mine.Employment : Another.Employment));
        var sender = Substitute.For<IEmailSender>();

        await RunAtAsync(run, sender);

        var match = (await MatchesAsync(user, ad)).ShouldHaveSingleItem();
        match.Grade.ShouldBe(NotifiableMatchGrade.Top);
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
        var mail = MailsTo(sender, user).ShouldHaveSingleItem();
        mail.Kind.ShouldBe(MatchNotificationKind.Direct);
        mail.Items.ShouldHaveSingleItem().JobAdId.ShouldBe(ad.Value);
    }

    // The contradiction floor is untouched: a user who STATES ort and employment, and an ad that carries
    // another ort, still gets no row and no mail. The control ad matches on every dimension and IS mailed,
    // alone: the scan reached the user, so the absence of a mail for the other ad is the floor's doing.
    [Fact]
    public async Task RunAsync_UserWhoStatesAPreferenceTheAdContradicts_IsNotMailed()
    {
        var run = CronMinuteField.NextAfter(Cron, new DateTimeOffset(2031, 4, 14, 10, 0, 0, TimeSpan.Zero));
        var user = await RegisterAsync(Mine, run.AddHours(-1));
        var contradicted = await SaveAsync(ImportTopAd(Mine, run.AddMinutes(-4), region: Another.Region));
        var control = await SaveAsync(ImportTopAd(Mine, run.AddMinutes(-3)));
        var sender = Substitute.For<IEmailSender>();

        await RunAtAsync(run, sender);

        (await MatchesAsync(user, contradicted)).ShouldBeEmpty();
        MailsTo(sender, user).ShouldHaveSingleItem().Items.ShouldHaveSingleItem().JobAdId.ShouldBe(control.Value);
    }

    private async Task RunAtAsync(DateTimeOffset at, IEmailSender sender, IServiceScopeFactory? scopes = null)
    {
        scopes ??= fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var clock = new FixedClock(at);
        await new BackgroundMatchingJob(
                scopes,
                new TopMatchMailDispatcher(scopes, sender, clock, NullLogger<TopMatchMailDispatcher>.Instance),
                clock,
                NullLogger<BackgroundMatchingJob>.Instance)
            .RunAsync(Ct);
    }

    private static List<MatchNotificationEmail> MailsTo(IEmailSender sender, Recipient recipient) =>
    [
        .. sender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailSender.SendMatchNotificationEmailAsync)
                           && (string?)call.GetArguments()[0] == recipient.Email)
            .Select(call => (MatchNotificationEmail)call.GetArguments()[1]!),
    ];

    private async Task<Recipient> RegisterAsync(
        Profile profile, DateTimeOffset at, bool statesRegion = true, bool statesEmployment = true)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var email = $"top-mail-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        (await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded.ShouldBeTrue();

        var clock = new FixedClock(at);
        var seeker = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        var preferences = MatchPreferences.Create(
            [profile.Group],
            statesRegion ? [profile.Region] : [],
            statesEmployment ? [profile.Employment] : [],
            preferredSkills: [profile.Skill]);
        preferences.IsSuccess.ShouldBeTrue();
        seeker.UpdateMatchPreferences(preferences.Value, clock);
        seeker.UpdateNotificationConsent(enabled: true, clock);

        var db = sp.GetRequiredService<AppDbContext>();
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(Ct);
        return new Recipient(user.Id, email);
    }

    // Top for a user of the same profile: occupation, region and employment match, and the ad requires a
    // skill the user confirms. The terms are the pair JobAdKeywordExtractor writes when the employer
    // states a must-have skill that the text also names: a Requirement term and a Skill term on one concept.
    private static JobAd ImportTopAd(
        Profile profile, DateTimeOffset ingestedAt, string? region = null, string? employment = null)
    {
        var terms = ExtractedTerms.From(
        [
            new ExtractedTerm(profile.Skill, "Testkompetens", ExtractedTermKind.Skill,
                ExtractedTermSource.Description, "Testkompetens", profile.Skill, 1),
            new ExtractedTerm(profile.Skill, "Testkompetens", ExtractedTermKind.Requirement,
                ExtractedTermSource.MustHave, "Testkompetens", profile.Skill, 1),
        ]);
        return Import(profile.Group, region ?? profile.Region, employment ?? profile.Employment, ingestedAt,
            TestKeywordExtraction.Returning(terms));
    }

    private JobAd ImportUnrelatedAd(DateTimeOffset ingestedAt) =>
        Import($"none-{_run}", region: null, employment: null, ingestedAt, TestKeywordExtraction.None);

    private static JobAd Import(
        string group, string? region, string? employment, DateTimeOffset ingestedAt, JobAdTermExtraction extract)
    {
        var externalId = $"top-mail-{Guid.NewGuid():N}";
        var regionJson = region is null ? "null" : $"{{\"region_concept_id\":\"{region}\"}}";
        var employmentJson = employment is null ? "null" : $"{{\"concept_id\":\"{employment}\"}}";
        var payload = $"{{\"id\":\"{externalId}\",\"occupation_group\":{{\"concept_id\":\"{group}\"}},"
            + $"\"workplace_address\":{regionJson},\"employment_type\":{employmentJson}}}";
        var imported = JobAd.Import("Testtjänst", Company.Create("Test AB").Value, "beskrivning",
            $"https://example.test/jobs/{externalId}",
            ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload, facets: TestFacets.FromPayload(payload), declaredContacts: [],
            publishedAt: ingestedAt.AddDays(-1), expiresAt: ingestedAt.AddDays(60),
            clock: new FixedClock(ingestedAt), extractTerms: extract);
        imported.IsSuccess.ShouldBeTrue();
        imported.Value.CreatedAt.ShouldBe(ingestedAt);
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

    private async Task<List<UserJobAdMatch>> MatchesAsync(Recipient user, JobAdId ad)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserJobAdMatches.AsNoTracking()
            .Where(match => match.UserId == user.UserId && match.JobAdId == ad)
            .ToListAsync(Ct);
    }

    private async Task<DateTimeOffset?> WatermarkAsync(Recipient user)
    {
        using var scope = fixture.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.AsNoTracking()
            .SingleAsync(seeker => seeker.UserId == user.UserId, Ct)).LastMatchScanAt;
    }

    // Records which ads each full batch scored, by the profile's occupation group, which is unique to
    // this test's users.
    private sealed class RecordingScorer(IMatchScorer inner) : IMatchScorer
    {
        private readonly List<(IReadOnlyList<string> Groups, IReadOnlyList<JobAdId> Ads)> _fullBatches = [];

        public List<IReadOnlyList<JobAdId>> BatchesFor(Profile profile) =>
            [.. _fullBatches.Where(batch => batch.Groups.Contains(profile.Group)).Select(batch => batch.Ads)];

        public ValueTask<MatchScore> ScoreAsync(
            JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken) =>
            inner.ScoreAsync(jobAdId, profile, cancellationToken);

        public ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile, CancellationToken cancellationToken) =>
            inner.ScoreBatchAsync(jobAdIds, profile, cancellationToken);

        public ValueTask<FullScoredMatch> ScoreFullAsync(
            JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken) =>
            inner.ScoreFullAsync(jobAdId, profile, cancellationToken);

        public ValueTask<IReadOnlyDictionary<JobAdId, FullScoredMatch>> ScoreFullBatchAsync(
            IReadOnlyList<JobAdId> jobAdIds, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
        {
            _fullBatches.Add((profile.Fast.SsykGroupConceptIds, jobAdIds));
            return inner.ScoreFullBatchAsync(jobAdIds, profile, cancellationToken);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
