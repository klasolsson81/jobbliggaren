using System.Security.Cryptography;
using System.Text.Json;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.SetMatchSummaryMail;
using Jobbliggaren.Application.JobSeekers.Commands.SetNotificationMail;
using Jobbliggaren.Application.JobSeekers.Commands.SetTopMatchMail;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
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
/// #533: real account scheduling must fence transport even after a mail claim committed.
/// Identity and its address survive scheduling, so an absent recipient cannot satisfy these assertions.
/// </summary>
[Collection("Worker")]
public sealed class UserMailDeletionFenceTests(WorkerTestFixture fixture)
{
    public enum MailArm { TopDirect, MatchDigest, FollowDigest }
    public enum Pause { AllNotificationMail, OwnMailType }

    private static readonly DateTimeOffset Now = new(2025, 4, 7, 6, 0, 0, TimeSpan.Zero);
    // The tracked taxonomy snapshot's software/system-development occupation group.
    private const string OccupationGroup = "DJh5_yyF_hEM";
    private readonly string _run = Guid.NewGuid().ToString("N")[..20];
    private string Region => $"reg-{_run}";
    private string Employment => $"emp-{_run}";
    private string Skill => $"skill-{_run}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Seeker(Guid UserId, JobSeekerId ProfileId, string Email);
    private sealed record Scenario(MailArm Arm, Seeker Target, Seeker Control, JobAdId AdId);

    [Theory]
    [InlineData(MailArm.TopDirect)]
    [InlineData(MailArm.MatchDigest)]
    [InlineData(MailArm.FollowDigest)]
    public async Task RunAsync_ShouldLeaveTheClaimQueuedAndSendOnlyToLiveControl_WhenDeletionCommitsAfterTheClaim(MailArm arm)
    {
        var scenario = await SeedAsync(arm);
        var deletion = new InterleaveAfterThePersistedClaim<AccountDeletionScheduled>(scenario.Target.UserId, async ct =>
        {
            var queued = await StatusAsync(scenario, scenario.Target, ct) == "Queued";
            var receipt = await ScheduleAsync(scenario.Target, ct);
            return (queued, receipt);
        });
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(true);

        var transportTransactions = await RunAsync(scenario, sender, deletion);
        deletion.Fired.ShouldBe(1);
        deletion.SawPersistedQueued.ShouldBeTrue();
        await AssertDeletionAsync(scenario, deletion.Outcome.ShouldNotBeNull());
        SendsFor(sender, scenario.Target).ShouldBe(0);
        SendsFor(sender, scenario.Control).ShouldBe(1);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe("Queued");
        (await StatusAsync(scenario, scenario.Control, Ct)).ShouldBe("Sent");
        transportTransactions.ShouldNotBeEmpty();
        transportTransactions.ShouldAllBe(active => !active);

        var retryTransactions = await RunAsync(scenario, sender, interceptor: null);

        SendsFor(sender, scenario.Target).ShouldBe(0);
        SendsFor(sender, scenario.Control).ShouldBe(1);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe("Queued");
        retryTransactions.ShouldAllBe(active => !active);
    }

    [Theory]
    [InlineData(MailArm.TopDirect)]
    [InlineData(MailArm.MatchDigest)]
    [InlineData(MailArm.FollowDigest)]
    public async Task RunAsync_ShouldSkipProcessingAndSendOnlyToLiveControl_WhenDeletionCommitsBeforeTheDueSet(MailArm arm)
    {
        var scenario = await SeedAsync(arm);
        var receipt = await ScheduleAsync(scenario.Target, Ct);
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(true);

        var transportTransactions = await RunAsync(scenario, sender, interceptor: null);

        await AssertDeletionAsync(scenario, receipt);
        SendsFor(sender, scenario.Target).ShouldBe(0);
        SendsFor(sender, scenario.Control).ShouldBe(1);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe(arm == MailArm.TopDirect ? "Absent" : "Pending");
        (await StatusAsync(scenario, scenario.Control, Ct)).ShouldBe("Sent");
        transportTransactions.ShouldNotBeEmpty();
        transportTransactions.ShouldAllBe(active => !active);
        if (arm == MailArm.TopDirect)
        {
            using var verify = fixture.Services.CreateScope();
            (await verify.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.IgnoreQueryFilters()
                .AsNoTracking().SingleAsync(value => value.Id == scenario.Target.ProfileId, Ct))
                .LastMatchScanAt.ShouldBeNull();
        }
    }

    // security-auditor V1 (ADR 0161 D5): the dispatcher checks consent again on the live row after its
    // claim has committed. The withdrawal is the real command's handler, committed in its own context.
    [Fact]
    public async Task RunAsync_ShouldLeaveTheTopClaimQueuedAndSendOnlyToLiveControl_WhenConsentIsWithdrawnAfterTheClaim()
    {
        var scenario = await SeedAsync(MailArm.TopDirect);
        var withdrawal = new InterleaveAfterThePersistedClaim<bool>(scenario.Target.UserId, async ct =>
        {
            var queued = await StatusAsync(scenario, scenario.Target, ct) == "Queued";
            return (queued, await WithdrawConsentAsync(scenario.Target, ct));
        });
        var sender = Substitute.For<IEmailSender>();

        var transportTransactions = await RunAsync(scenario, sender, withdrawal);

        withdrawal.Fired.ShouldBe(1);
        withdrawal.SawPersistedQueued.ShouldBeTrue();
        withdrawal.Outcome.ShouldBeTrue();
        SendsFor(sender, scenario.Target).ShouldBe(0);
        SendsFor(sender, scenario.Control).ShouldBe(1);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe("Queued");
        (await StatusAsync(scenario, scenario.Control, Ct)).ShouldBe("Sent");
        transportTransactions.ShouldAllBe(active => !active);

        await RunAsync(scenario, sender, interceptor: null);

        SendsFor(sender, scenario.Target).ShouldBe(0);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe("Queued");
    }

    // security-auditor V1 (ADR 0164, #2107): the fence after the claim is each mail's gate, so a pause of
    // every notification mail, or of the mail's own type, committed after the claim stops the send. The
    // follow mail has no type switch of its own. The pause is the real command's handler, in its own context.
    [Theory]
    [InlineData(MailArm.TopDirect, Pause.AllNotificationMail)]
    [InlineData(MailArm.TopDirect, Pause.OwnMailType)]
    [InlineData(MailArm.MatchDigest, Pause.AllNotificationMail)]
    [InlineData(MailArm.MatchDigest, Pause.OwnMailType)]
    [InlineData(MailArm.FollowDigest, Pause.AllNotificationMail)]
    public async Task RunAsync_ShouldLeaveTheClaimQueuedAndSendOnlyToLiveControl_WhenAPauseCommitsAfterTheClaim(
        MailArm arm, Pause pause)
    {
        var scenario = await SeedAsync(arm);
        var pausing = new InterleaveAfterThePersistedClaim<bool>(scenario.Target.UserId, async ct =>
        {
            var queued = await StatusAsync(scenario, scenario.Target, ct) == "Queued";
            return (queued, await PauseAsync(scenario, pause, ct));
        });
        var sender = Substitute.For<IEmailSender>();

        var transportTransactions = await RunAsync(scenario, sender, pausing);

        pausing.Fired.ShouldBe(1);
        pausing.SawPersistedQueued.ShouldBeTrue();
        pausing.Outcome.ShouldBeTrue();
        SendsFor(sender, scenario.Target).ShouldBe(0);
        SendsFor(sender, scenario.Control).ShouldBe(1);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe("Queued");
        (await StatusAsync(scenario, scenario.Control, Ct)).ShouldBe("Sent");
        transportTransactions.ShouldAllBe(active => !active);

        await RunAsync(scenario, sender, interceptor: null);

        SendsFor(sender, scenario.Target).ShouldBe(0);
        (await StatusAsync(scenario, scenario.Target, Ct)).ShouldBe("Queued");
    }

    private async Task<Scenario> SeedAsync(MailArm arm)
    {
        var organization = "559" + RandomNumberGenerator.GetInt32(1_000_000, 10_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ad = await SeedAdAsync(organization);
        var target = await RegisterAsync(arm);
        var control = await RegisterAsync(arm);
        if (arm == MailArm.TopDirect)
        {
            await AssertActualTopAsync(target, ad);
            await AssertActualTopAsync(control, ad);
        }
        else
        {
            foreach (var seeker in new[] { target, control })
            {
                using var scope = fixture.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (arm == MailArm.MatchDigest)
                {
                    var match = UserJobAdMatch.Create(seeker.UserId, ad, NotifiableMatchGrade.Strong, [], new FixedClock(Now));
                    match.IsSuccess.ShouldBeTrue();
                    match.Value.NotificationStatus.ShouldBe(NotificationStatus.Pending);
                    db.UserJobAdMatches.Add(match.Value);
                }
                else
                {
                    var number = OrganizationNumber.Create(organization);
                    number.IsSuccess.ShouldBeTrue();
                    number.Value.IsPersonnummerShaped().ShouldBeFalse();
                    var watch = CompanyWatch.Follow(seeker.UserId, number.Value, new FixedClock(Now));
                    watch.IsSuccess.ShouldBeTrue();
                    var hit = FollowedCompanyAdHit.Create(seeker.UserId, ad, watch.Value.Id, new FixedClock(Now));
                    hit.IsSuccess.ShouldBeTrue();
                    hit.Value.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
                    db.CompanyWatches.Add(watch.Value);
                    db.FollowedCompanyAdHits.Add(hit.Value);
                }

                await db.SaveChangesAsync(Ct);
            }
        }

        return new Scenario(arm, target, control, ad);
    }

    private async Task<Seeker> RegisterAsync(MailArm arm)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now.AddMinutes(-1));
        var address = $"mail-fence-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = address, Email = address, EmailConfirmed = true };
        await using var transaction = await sp.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([user.Id], lifecycle: false, Ct);
        (await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded.ShouldBeTrue();
        var registered = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock);
        registered.IsSuccess.ShouldBeTrue();
        var profile = registered.Value;
        var preferences = MatchPreferences.Create([OccupationGroup], [Region], [Employment], preferredSkills: [Skill]);
        preferences.IsSuccess.ShouldBeTrue();
        profile.UpdateMatchPreferences(preferences.Value, clock);
        profile.SetDigestCadence(DigestCadence.Weekly, clock).IsSuccess.ShouldBeTrue();
        if (arm == MailArm.FollowDigest)
        {
            profile.UpdateFollowedCompanyNotificationConsent(enabled: true, clock);
            profile.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
            profile.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
        }
        else
        {
            profile.UpdateNotificationConsent(enabled: true, clock);
            profile.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
            profile.Preferences.NotificationConsentWithdrawnAt.ShouldBeNull();
        }

        profile.DeletedAt.ShouldBeNull();
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);
        (await sp.GetRequiredService<IAccountAccessReader>().ReadAsync(user.Id, Ct)).ShouldNotBeNull()
            .CanAuthenticate.ShouldBeTrue();
        await transaction.CommitAsync(Ct);
        return new Seeker(user.Id, profile.Id, address);
    }

    private async Task<JobAdId> SeedAdAsync(string organization)
    {
        var externalId = $"mail-fence-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            id = externalId,
            occupation_group = new { concept_id = OccupationGroup },
            workplace_address = new { region_concept_id = Region },
            employment_type = new { concept_id = Employment },
            employer = new { name = "Synthetic AB", organization_number = organization },
        });
        var terms = ExtractedTerms.From([
            new ExtractedTerm(Skill, "Test skill", ExtractedTermKind.Skill, ExtractedTermSource.Description, "Test skill", Skill, 1),
            new ExtractedTerm(Skill, "Test skill", ExtractedTermKind.Requirement, ExtractedTermSource.MustHave, "Test skill", Skill, 1),
        ]);
        var imported = JobAd.Import("Synthetic developer role", Company.Create("Synthetic AB").Value,
            "Test skill", $"https://example.test/jobs/{externalId}",
            ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload, facets: TestFacets.FromPayload(payload), declaredContacts: [],
            publishedAt: Now.AddDays(-1), expiresAt: Now.AddDays(60), clock: new FixedClock(Now),
            extractTerms: TestKeywordExtraction.Returning(terms));
        imported.IsSuccess.ShouldBeTrue();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobAds.Add(imported.Value);
        await db.SaveChangesAsync(Ct);
        return imported.Value.Id;
    }

    private async Task AssertActualTopAsync(Seeker seeker, JobAdId ad)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var profile = await sp.GetRequiredService<IMatchProfileBuilder>().BuildFullForUserIdAsync(seeker.UserId, Ct);
        var scored = await sp.GetRequiredService<IMatchScorer>().ScoreFullBatchAsync([ad], profile, Ct);
        scored.TryGetValue(ad, out var actual).ShouldBeTrue();
        MatchGradeCalculator.Grade(actual!.Score, actual.Causes, actual.SsykIsRelated).ShouldBe(MatchGrade.Top);
    }

    private async Task<bool> WithdrawConsentAsync(Seeker target, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = Substitute.For<ICurrentUser>();
        owner.UserId.Returns(target.UserId);
        owner.IsAuthenticated.Returns(true);
        var handled = await new UpdateNotificationConsentCommandHandler(db, owner, new FixedClock(Now))
            .Handle(new UpdateNotificationConsentCommand(Enabled: false), ct);
        handled.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var stored = await db.JobSeekers.AsNoTracking().SingleAsync(value => value.UserId == target.UserId, ct);
        return !stored.Preferences.BackgroundMatchNotificationsEnabled
            && stored.Preferences.NotificationConsentWithdrawnAt == Now;
    }

    // True when the pause is stored at the run's clock and the arm's consent still stands: a pause is never a
    // withdrawal (security-auditor V3).
    private async Task<bool> PauseAsync(Scenario scenario, Pause pause, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = Substitute.For<ICurrentUser>();
        owner.UserId.Returns(scenario.Target.UserId);
        owner.IsAuthenticated.Returns(true);
        var clock = new FixedClock(Now);
        var handled = (pause, scenario.Arm) switch
        {
            (Pause.AllNotificationMail, _) => await new SetNotificationMailCommandHandler(db, owner, clock)
                .Handle(new SetNotificationMailCommand(Enabled: false), ct),
            (Pause.OwnMailType, MailArm.TopDirect) => await new SetTopMatchMailCommandHandler(db, owner, clock)
                .Handle(new SetTopMatchMailCommand(Enabled: false), ct),
            (Pause.OwnMailType, MailArm.MatchDigest) => await new SetMatchSummaryMailCommandHandler(db, owner, clock)
                .Handle(new SetMatchSummaryMailCommand(Enabled: false), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(pause), "The follow mail has no type switch."),
        };
        handled.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var stored = await db.JobSeekers.AsNoTracking().SingleAsync(value => value.UserId == scenario.Target.UserId, ct);
        var stamp = pause == Pause.AllNotificationMail
            ? stored.Preferences.NotificationMailOffAt
            : scenario.Arm == MailArm.TopDirect
                ? stored.Preferences.TopMatchMailOffAt
                : stored.Preferences.MatchSummaryMailOffAt;
        var consent = scenario.Arm == MailArm.FollowDigest
            ? NotificationConsent.FollowedCompany
            : NotificationConsent.BackgroundMatch;
        return stamp == Now && consent.IsSatisfiedBy(stored);
    }

    private async Task<AccountDeletionScheduled> ScheduleAsync(Seeker target, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ICurrentDataOwner>().JobSeekerId.ShouldBeNull();
        var coordinator = sp.GetRequiredService<IAccountAccessCoordinator>();
        await using var transaction = await coordinator.BeginAsync([target.UserId], lifecycle: true, ct);
        var reader = sp.GetRequiredService<IAccountAccessReader>();
        var eraser = new IdentityExternalLoginStore(sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<AppIdentityDbContext>(), sp.GetRequiredService<IDbExceptionInspector>(), coordinator, reader);
        var scheduler = new AccountDeletionScheduler(sp.GetRequiredService<AppDbContext>(), new FixedClock(Now),
            reader, sp.GetRequiredService<IAccountAccessWriter>(), eraser);
        var scheduled = await scheduler.ScheduleAsync(target.UserId, administratorInitiated: true, ct);
        scheduled.IsSuccess.ShouldBeTrue();
        await sp.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return scheduled.Value;
    }

    private async Task AssertDeletionAsync(Scenario scenario, AccountDeletionScheduled receipt)
    {
        receipt.UserId.ShouldBe(scenario.Target.UserId);
        receipt.ProfileId.ShouldBe(scenario.Target.ProfileId.Value);
        using var scope = fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        (await db.JobSeekers.AnyAsync(value => value.Id == scenario.Target.ProfileId, Ct)).ShouldBeFalse();
        var deleted = await db.JobSeekers.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == scenario.Target.ProfileId, Ct);
        deleted.DeletedAt.ShouldBe(receipt.DeletedAt);
        var control = await db.JobSeekers.AsNoTracking().SingleAsync(value => value.Id == scenario.Control.ProfileId, Ct);
        control.DeletedAt.ShouldBeNull();
        if (scenario.Arm == MailArm.FollowDigest)
        {
            deleted.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
            control.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        }
        else
        {
            deleted.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
            control.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        }

        (await sp.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .SingleAsync(value => value.Id == scenario.Target.UserId, Ct)).Email.ShouldBe(scenario.Target.Email);
        (await sp.GetRequiredService<IUserAccountService>().GetEmailAsync(scenario.Target.UserId, Ct))
            .ShouldBe(scenario.Target.Email);
    }

    private async Task<string> StatusAsync(Scenario scenario, Seeker seeker, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (scenario.Arm == MailArm.FollowDigest)
            return (await db.FollowedCompanyAdHits.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(value => value.UserId == seeker.UserId && value.JobAdId == scenario.AdId, ct))
                .NotificationStatus.ToString();
        var match = await db.UserJobAdMatches.AsNoTracking()
            .SingleOrDefaultAsync(value => value.UserId == seeker.UserId && value.JobAdId == scenario.AdId, ct);
        return match?.NotificationStatus.ToString() ?? "Absent";
    }

    private async Task<List<bool>> RunAsync(Scenario scenario, IEmailSender sender, IInterceptor? interceptor)
    {
        var transactionsAtTransport = new List<bool>();
        if (scenario.Arm == MailArm.TopDirect)
        {
            var scopes = new ClaimInterleavingScopeFactory(fixture.Services.GetRequiredService<IServiceScopeFactory>(), interceptor);
            RecordTransport(sender, () => scopes.HasActiveTransaction, transactionsAtTransport);
            var clock = new FixedClock(Now);
            await new BackgroundMatchingJob(scopes,
                    new TopMatchMailDispatcher(scopes, sender, clock, NullLogger<TopMatchMailDispatcher>.Instance),
                    clock, NullLogger<BackgroundMatchingJob>.Instance)
                .RunAsync(Ct);
        }
        else
        {
            using var scope = fixture.Services.CreateScope();
            var sp = scope.ServiceProvider;
            await using var db = ContextWith(sp, interceptor);
            RecordTransport(sender, () => HasActiveTransaction(db, sp), transactionsAtTransport);
            await new DigestDispatchJob(db, sender, sp.GetRequiredService<IUserAccountService>(),
                sp.GetRequiredService<IMatchProfileBuilder>(), sp.GetRequiredService<IPerUserJobAdSearchQuery>(),
                new FixedClock(Now), sp.GetRequiredService<IOptions<DigestDispatchOptions>>(),
                NullLogger<DigestDispatchJob>.Instance).RunAsync(DigestCadence.Weekly, Ct);
        }

        return transactionsAtTransport;
    }

    private static void RecordTransport(IEmailSender sender, Func<bool> transactionActive, List<bool> observations)
    {
        sender.SendMatchNotificationEmailAsync(Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ => { observations.Add(transactionActive()); return Task.CompletedTask; });
        sender.SendFollowedCompanyNotificationEmailAsync(Arg.Any<string>(), Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ => { observations.Add(transactionActive()); return Task.CompletedTask; });
    }

    private static int SendsFor(IEmailSender sender, Seeker seeker) => sender.ReceivedCalls().Count(call =>
        (call.GetMethodInfo().Name is nameof(IEmailSender.SendMatchNotificationEmailAsync) or nameof(IEmailSender.SendFollowedCompanyNotificationEmailAsync))
        && call.GetArguments()[0] is string recipient && recipient == seeker.Email);

    private static AppDbContext ContextWith(IServiceProvider sp, IInterceptor? interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(sp.GetRequiredService<DbContextOptions<AppDbContext>>());
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    private static bool HasActiveTransaction(AppDbContext db, IServiceProvider sp) =>
        db.Database.CurrentTransaction is not null ||
        sp.GetRequiredService<AppIdentityDbContext>().Database.CurrentTransaction is not null ||
        sp.GetRequiredService<IAccountAccessCoordinator>().HasActiveScope ||
        System.Transactions.Transaction.Current is not null;

    private sealed class InterleaveAfterThePersistedClaim<TOutcome>(
        Guid target,
        Func<CancellationToken, Task<(bool Queued, TOutcome Outcome)>> interleave) : SaveChangesInterceptor
    {
        public int Fired { get; private set; }
        public bool SawPersistedQueued { get; private set; }
        public TOutcome? Outcome { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Fired > 0 || eventData.Context is not { } db)
                return result;
            var queued = db.ChangeTracker.Entries<UserJobAdMatch>().Any(value =>
                value.Entity.UserId == target && value.Entity.NotificationStatus == NotificationStatus.Queued)
                || db.ChangeTracker.Entries<FollowedCompanyAdHit>().Any(value =>
                    value.Entity.UserId == target && value.Entity.NotificationStatus == FollowedCompanyAdHitStatus.Queued);
            if (queued)
            {
                Fired++;
                (SawPersistedQueued, Outcome) = await interleave(cancellationToken);
            }

            return result;
        }
    }

    private sealed class ClaimInterleavingScopeFactory(IServiceScopeFactory inner, IInterceptor? interceptor) : IServiceScopeFactory
    {
        private readonly List<MailScope> _active = [];
        public bool HasActiveTransaction => _active.Any(scope => scope.HasActiveTransaction);

        public IServiceScope CreateScope()
        {
            var scope = new MailScope(inner.CreateScope(), interceptor, value => _active.Remove(value));
            _active.Add(scope);
            return scope;
        }

        private sealed class MailScope : IServiceScope, IServiceProvider, IAsyncDisposable
        {
            private readonly IServiceScope _inner;
            private readonly AppDbContext _db;
            private readonly Action<MailScope> _released;
            public IServiceProvider ServiceProvider => this;
            public bool HasActiveTransaction => UserMailDeletionFenceTests.HasActiveTransaction(_db, _inner.ServiceProvider);

            public MailScope(IServiceScope innerScope, IInterceptor? interceptor, Action<MailScope> released)
            {
                _inner = innerScope;
                _released = released;
                _db = ContextWith(_inner.ServiceProvider, interceptor);
            }

            public object? GetService(Type type)
            {
                if (type == typeof(IAppDbContext) || type == typeof(AppDbContext))
                    return _db;
                if (type == typeof(IMatchProfileBuilder))
                    return new MatchProfileBuilder(_db, _inner.ServiceProvider.GetRequiredService<ICurrentUser>(),
                        _inner.ServiceProvider.GetRequiredService<ITaxonomyReadModel>());
                if (type == typeof(IMatchScorer))
                    return new MatchScorer(_db, new LocalTextAnalyzer(new SnowballStemmer()));
                return _inner.ServiceProvider.GetService(type);
            }

            public void Dispose()
            {
                _db.Dispose();
                _inner.Dispose();
                _released(this);
            }

            public async ValueTask DisposeAsync()
            {
                await _db.DisposeAsync();
                if (_inner is IAsyncDisposable asyncScope)
                    await asyncScope.DisposeAsync();
                else
                    _inner.Dispose();
                _released(this);
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
