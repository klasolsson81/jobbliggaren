using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0175 (#1663 PR 2) — <see cref="CompanyWatchCriterionScanJob"/> against real Postgres: the window and its
/// three bounds (D3, D6), the outcomes, the retention purge for every due user whatever the outcome or hold
/// (DPIA §D9 M-D9), isolation, counts-only logs (M-D10) and the named residuals R-a to R-c at the data level.
///
/// <para>
/// <b>Every state is written by a production writer.</b> Criteria by <see cref="CompanyWatchCriterion.Create"/> and
/// <see cref="CompanyWatchCriterion.UpdateCriteria"/>, their member sets by the real materialiser, follow watermarks
/// by the real <see cref="CompanyWatchScanJob"/>, matches by the real <see cref="BackgroundMatchingJob"/>, ads by
/// <see cref="JobAd.Import"/> with the ingest clock, and an ad that leaves Active by <see cref="JobAd.Archive"/>.
/// The register rows are inserted the way the SCB upsert writes them; the register is not under test here.
/// </para>
///
/// <para>
/// The "Worker" collection shares one database, and the job reads every user with a criterion. Each test makes
/// its own users, a register company of its own and a five-digit SNI code of its own, and asserts only its own
/// rows.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchCriterionScanJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // The criterion scan's run instant on night 1 (03:55 UTC), and the follow scan's slot before it.
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 3, 55, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FollowRun = new(2026, 6, 15, 3, 25, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Now.AddDays(-30);
    private const string Kommun = "0180";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_WritesOneUnreadHitPerNewActiveMemberAd_OwnedByTheCriterionsUser()
    {
        var w = await WorldAsync();
        var first = await SeedAdAsync(w.OrgNr, Now.AddHours(-3));
        var second = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await SeedAdAsync(UniqueOrgNr(), Now.AddHours(-2)); // another company's ad: no hit

        await RunAsync(Now);

        var hits = await HitsAsync(w.UserId);
        hits.Select(h => h.JobAdId).ShouldBe([first, second], ignoreOrder: true);
        hits.ShouldAllBe(h => h.UserId == w.UserId && h.CompanyWatchCriterionId == w.CriterionId);
        hits.ShouldAllBe(h => !h.ReadInNotificationFeed && !h.DismissedFromNotificationFeed);
    }

    [Fact]
    public async Task Run_StampsEachHitFromTheClockPerHit_NotFromTheRunsInstant()
    {
        var w = await WorldAsync();
        await SeedAdAsync(w.OrgNr, Now.AddHours(-3));
        await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        var clock = new SteppingClock(Now, TimeSpan.FromMilliseconds(1));

        await RunAsync(clock);

        var stamps = (await HitsAsync(w.UserId)).Select(h => h.CreatedAt).ToList();
        stamps.Count.ShouldBe(2);
        stamps.Distinct().Count().ShouldBe(2, "a run-wide instant over a burst makes the acknowledgement window's tie live");
        stamps.ShouldAllBe(s => s > Now);
    }

    [Fact]
    public async Task Run_DoesNotReadInsideTheIngestSettleWindow_AndTheNextRunDoes()
    {
        var w = await WorldAsync();
        var settling = await SeedAdAsync(w.OrgNr, Now - BackgroundMatchingJob.IngestSettleWindow / 2);

        await RunAsync(Now);
        (await HitsAsync(w.UserId)).ShouldBeEmpty();

        await RunAsync(Now.AddDays(1));
        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([settling]);
    }

    [Fact]
    public async Task Run_RecordsScanned_AndAdvancesScannedThroughToTheSettledInstant_WithoutTouchingUpdatedAt()
    {
        var w = await WorldAsync();
        var before = await CriterionAsync(w.CriterionId);

        await RunAsync(Now);

        var after = await CriterionAsync(w.CriterionId);
        after.LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.Scanned);
        after.ScannedThrough.ShouldBe(Now - BackgroundMatchingJob.IngestSettleWindow);
        after.UpdatedAt.ShouldBe(before.UpdatedAt, "the materialisation sweep keys on updated_at");
    }

    [Fact]
    public async Task Run_TooBroad_AdvancesAndRecordsTooBroad_AndWritesNoHit()
    {
        var w = await WorldAsync(tooBroad: true);
        await SeedAdAsync(w.OrgNr, Now.AddHours(-2));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).ShouldBeEmpty();
        var criterion = await CriterionAsync(w.CriterionId);
        criterion.LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.TooBroad);
        criterion.ScannedThrough.ShouldBe(Now - BackgroundMatchingJob.IngestSettleWindow);
    }

    [Fact]
    public async Task Run_NotMaterialised_HoldsScannedThrough_RecordsNotMaterialised_AndWritesNoHit()
    {
        var w = await WorldAsync(materialise: false);
        await SeedAdAsync(w.OrgNr, Now.AddHours(-2));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).ShouldBeEmpty();
        var criterion = await CriterionAsync(w.CriterionId);
        criterion.LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.NotMaterialised);
        criterion.ScannedThrough.ShouldBeNull();
    }

    [Fact]
    public async Task Run_FirstScanOfAnExistingCriterion_ReachesBackAtMostTheColdStartFloor()
    {
        var w = await WorldAsync();
        await SeedAdAsync(w.OrgNr, Now.AddDays(-CompanyWatchScanJob.ColdStartDays).AddHours(-1));
        var inside = await SeedAdAsync(w.OrgNr, Now.AddDays(-CompanyWatchScanJob.ColdStartDays).AddHours(1));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([inside]);
    }

    [Fact]
    public async Task Run_ANewCriterion_BackfillsNothing()
    {
        var w = await WorldAsync(createdAt: Now.AddHours(-1));
        await SeedAdAsync(w.OrgNr, Now.AddHours(-3));
        var afterCreation = await SeedAdAsync(w.OrgNr, Now.AddMinutes(-30));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([afterCreation]);
    }

    [Fact]
    public async Task Run_AnEditedCriterion_ReadsOnlyFromTheEdit()
    {
        var w = await WorldAsync();
        await SeedAdAsync(w.OrgNr, Now.AddHours(-3));

        // The edit widens the codes to a second SNI the company also carries, so the same company stays a member
        // and the only thing that moves is the lower bound.
        await UpdateCriteriaAsync(w.CriterionId, [w.Sni, w.SecondSni], at: Now.AddHours(-2));
        await MaterialiseAsync();
        var afterEdit = await SeedAdAsync(w.OrgNr, Now.AddHours(-1));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([afterEdit]);
    }

    [Fact]
    public async Task Run_AgainAtTheSameInstant_WritesNoSecondHit()
    {
        var w = await WorldAsync();
        await SeedAdAsync(w.OrgNr, Now.AddHours(-2));

        await RunAsync(Now);
        await RunAsync(Now);

        (await HitsAsync(w.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Run_WithAnActiveFollow_NeverReadsPastTheFollowWatermark_SoTheFollowHitIsBornFirst()
    {
        // ADR 0175 D3. The owner follows the company the criterion also matches. The follow scan runs at 03:25 and
        // stamps its watermark; an ad ingested after that gets no criterion hit at 03:55, and the next night its
        // follow hit is written before its criterion hit.
        var w = await WorldAsync();
        await FollowAsync(w.UserId, w.OrgNr);
        var beforeFollowRun = await SeedAdAsync(w.OrgNr, FollowRun.AddMinutes(-30));
        await RunFollowScanAsync(FollowRun);
        var afterFollowRun = await SeedAdAsync(w.OrgNr, FollowRun.AddMinutes(10));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([beforeFollowRun]);
        (await CriterionAsync(w.CriterionId)).ScannedThrough.ShouldBe(FollowRun);

        await RunFollowScanAsync(FollowRun.AddDays(1));
        await RunAsync(Now.AddDays(1));

        var criterionHit = (await HitsAsync(w.UserId)).Single(h => h.JobAdId == afterFollowRun);
        var followHit = (await FollowHitsAsync(w.UserId)).Single(h => h.JobAdId == afterFollowRun);
        followHit.CreatedAt.ShouldBeLessThan(criterionHit.CreatedAt);
    }

    [Fact]
    public async Task Run_WithAnActiveFollowAndNoFollowWatermark_HoldsTheOwner()
    {
        var w = await WorldAsync();
        await FollowAsync(w.UserId, UniqueOrgNr());
        await SeedAdAsync(w.OrgNr, Now.AddHours(-2));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).ShouldBeEmpty();
        var criterion = await CriterionAsync(w.CriterionId);
        criterion.ScannedThrough.ShouldBeNull();
        criterion.LastScanOutcome.ShouldBeNull("a held owner records nothing, so the last real answer stands");
    }

    [Fact]
    public async Task Run_WithNoActiveFollow_IsNotBoundByAStaleFollowWatermark()
    {
        // The owner once followed a company, the follow scan stamped a watermark five days back, and the owner then
        // unfollowed it: the follow scan's due set holds only active followers, so that watermark never moves again.
        var w = await WorldAsync();
        var followedOrgNr = UniqueOrgNr();
        await FollowAsync(w.UserId, followedOrgNr);
        await RunFollowScanAsync(Now.AddDays(-5));
        await UnfollowAsync(w.UserId, followedOrgNr, Now.AddDays(-4));
        var recent = await SeedAdAsync(w.OrgNr, Now.AddHours(-1));

        await RunAsync(Now);

        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([recent]);
    }

    [Fact]
    public async Task Run_PurgesAHitWhoseAdLeftActive_ForAnOwnerHeldBehindTheFollowScan()
    {
        // DPIA §D9 M-D9, the named held case: the purge runs for every due user whatever the outcome or hold.
        var w = await WorldAsync();
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await RunAsync(Now);
        (await HitsAsync(w.UserId)).Count.ShouldBe(1);

        await FollowAsync(w.UserId, UniqueOrgNr()); // a follow with no follow watermark yet: the owner is held
        await ArchiveAsync(ad);
        await RunAsync(Now.AddDays(1));

        (await HitsAsync(w.UserId)).ShouldBeEmpty();
        (await CriterionAsync(w.CriterionId)).ScannedThrough.ShouldBe(Now - BackgroundMatchingJob.IngestSettleWindow);
    }

    [Fact]
    public async Task Run_PurgesAHitWhoseAdLeftActive_WhenTheCriterionIsNotMaterialised()
    {
        var w = await WorldAsync();
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await RunAsync(Now);

        // An edit the materialiser has not caught up with: the member set's fingerprint no longer matches.
        await UpdateCriteriaAsync(w.CriterionId, [w.Sni, w.SecondSni], at: Now.AddHours(1));
        await ArchiveAsync(ad);
        await RunAsync(Now.AddDays(1));

        (await HitsAsync(w.UserId)).ShouldBeEmpty();
        (await CriterionAsync(w.CriterionId)).LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.NotMaterialised);
    }

    [Fact]
    public async Task Run_PurgesAHitWhoseAdLeftActive_WhenTheCriterionIsTooBroad()
    {
        var w = await WorldAsync();
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await RunAsync(Now);

        await WidenToTooBroadAsync(w, at: Now.AddHours(1));
        await ArchiveAsync(ad);
        await RunAsync(Now.AddDays(1));

        (await HitsAsync(w.UserId)).ShouldBeEmpty();
        (await CriterionAsync(w.CriterionId)).LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.TooBroad);
    }

    [Fact]
    public async Task Run_KeepsAHitWhoseAdIsStillActive_AndWritesNoFlag()
    {
        var w = await WorldAsync();
        var kept = await SeedAdAsync(w.OrgNr, Now.AddHours(-3));
        var archived = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await RunAsync(Now);

        await ArchiveAsync(archived);
        await RunAsync(Now.AddDays(1));

        var hits = await HitsAsync(w.UserId);
        hits.Select(h => h.JobAdId).ShouldBe([kept]);
        hits.Single().ReadInNotificationFeed.ShouldBeFalse();
    }

    [Fact]
    public async Task Run_SkipsAnAccountUnderDeletion_NeitherPurgingNorWriting()
    {
        // DPIA §D9 M-D8: the due set reads through the JobSeeker query filter, so an account in its deletion window
        // is not due. Its hits stay until the Art. 17 job deletes the account with them.
        var w = await WorldAsync();
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await RunAsync(Now);
        await SeedAdAsync(w.OrgNr, Now.AddHours(10));

        await RequestDeletionAsync(w.UserId, Now.AddHours(11));
        await ArchiveAsync(ad);
        await RunAsync(Now.AddDays(1));

        var hits = await HitsAsync(w.UserId);
        hits.Select(h => h.JobAdId).ShouldBe([ad], "not purged and nothing written");
    }

    [Fact]
    public async Task Run_IsolatesACriterionThatCannotBeRead_AndAdvancesTheOwnersOthers()
    {
        // The actor is the command timeout: the port's statement has a 30-second timeout, and Npgsql surfaces a
        // timed-out command as an NpgsqlException. One criterion's read throws it; the owner's other criterion is
        // read and advanced, and the failing one is held.
        var w = await WorldAsync();
        var other = await SecondCriterionAsync(w);
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));

        await RunAsync(Now, scanFor: inner => new ThrowingScan(inner, w.CriterionId));

        (await CriterionAsync(w.CriterionId)).LastScanOutcome.ShouldBeNull();
        (await CriterionAsync(w.CriterionId)).ScannedThrough.ShouldBeNull();
        (await CriterionAsync(other)).LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.Scanned);
        (await HitsAsync(w.UserId)).ShouldHaveSingleItem().CompanyWatchCriterionId.ShouldBe(other);
        (await HitsAsync(w.UserId)).Single().JobAdId.ShouldBe(ad);
    }

    [Fact]
    public async Task Run_IsolatesAUserWhoseUnitFails_AndScansTheRest()
    {
        // The actor is a concurrent criterion delete (ADR 0175 D10): the criterion goes between the read and the
        // insert, and the user's SaveChanges fails on the foreign key. The other user is scanned.
        var failing = await WorldAsync();
        var healthy = await WorldAsync();
        await SeedAdAsync(failing.OrgNr, Now.AddHours(-2));
        var healthyAd = await SeedAdAsync(healthy.OrgNr, Now.AddHours(-2));

        await RunAsync(Now, scanFor: inner => new DeletingScan(inner, failing.CriterionId, _fixture.ConnectionString));

        (await HitsAsync(failing.UserId)).ShouldBeEmpty();
        (await HitsAsync(healthy.UserId)).Select(h => h.JobAdId).ShouldBe([healthyAd]);
    }

    [Fact]
    public async Task Run_LogsCountsOnly_AndAUserIdOnlyOnAPerUserEvent()
    {
        var w = await WorldAsync();
        var other = await SecondCriterionAsync(w);
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        var logs = new ListLoggerProvider();

        await RunAsync(Now, scanFor: inner => new ThrowingScan(inner, w.CriterionId), logs: logs);

        logs.Messages.ShouldNotBeEmpty();
        var forbidden = new[]
        {
            w.CriterionId.Value.ToString(), other.Value.ToString(), ad.Value.ToString(), w.OrgNr, w.Sni, Kommun,
        };
        foreach (var message in logs.Messages)
            forbidden.ShouldAllBe(value => !message.Contains(value, StringComparison.Ordinal));
        logs.Messages.Where(m => m.Contains(w.UserId.ToString(), StringComparison.Ordinal))
            .ShouldAllBe(m => m.Contains("could not be read", StringComparison.Ordinal)
                || m.Contains("scan failed", StringComparison.Ordinal));
        logs.Messages.ShouldContain(m => m.Contains("done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RA_AFirstFollowAfterACriterionHit_BearsAFollowHitForTheSameAd()
    {
        // ADR 0175 R-a, a named residual pinned at the data level: on a first follow the follow scan cold-starts
        // seven days back, so it can bear a follow hit for an ad already listed as a criterion hit. The bell's half
        // (that ad ringing again) is asserted with the criterion source in PR 3.
        var w = await WorldAsync();
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2));
        await RunAsync(Now);
        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([ad]);

        await FollowAsync(w.UserId, w.OrgNr);
        await RunFollowScanAsync(FollowRun.AddDays(1));

        (await FollowHitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([ad]);
        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([ad]);
    }

    [Fact]
    public async Task RB_AMatchBornAfterACriterionHit_IsWrittenForTheSameAd()
    {
        // ADR 0175 R-b (0173 R1, inherited), at the data level: the owner grants the matching consent after the
        // criterion hit exists, and background matching reaches seven days back, so a match row is born for an ad
        // already listed as a criterion hit. The bell's half is asserted in PR 3.
        var run = Guid.NewGuid().ToString("N")[..8];
        var w = await WorldAsync(matchPreferences: ($"grp-{run}", $"reg-{run}", $"emp-{run}"));
        var ad = await SeedAdAsync(w.OrgNr, Now.AddHours(-2), occupation: ($"grp-{run}", $"reg-{run}", $"emp-{run}"));
        await RunAsync(Now);
        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([ad]);

        await GrantMatchingConsentAsync(w.UserId, Now.AddHours(1));
        await RunBackgroundMatchingAsync(Now.AddHours(2));

        var match = await MatchAsync(w.UserId, ad);
        match.ShouldNotBeNull();
        match.ReadInNotificationFeed.ShouldBeFalse();
        (await HitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([ad]);
    }

    [Fact]
    public async Task RC_AfterAnUnfollow_TheCriterionHitForTheSameAdStays()
    {
        // ADR 0175 R-c (0173 R2's class), at the data level: while the owner follows the company, the bell holds the
        // ad on its follow hit; after an unfollow nothing holds it any longer and the criterion hit speaks for itself.
        // What PR 2 can pin is that the criterion hit is still there, unread. The bell's half is PR 3's.
        var w = await WorldAsync();
        await FollowAsync(w.UserId, w.OrgNr);
        var ad = await SeedAdAsync(w.OrgNr, FollowRun.AddMinutes(-30));
        await RunFollowScanAsync(FollowRun);
        await RunAsync(Now);
        (await FollowHitsAsync(w.UserId)).Select(h => h.JobAdId).ShouldBe([ad]);

        await UnfollowAsync(w.UserId, w.OrgNr, Now.AddHours(1));

        var hit = (await HitsAsync(w.UserId)).ShouldHaveSingleItem();
        hit.JobAdId.ShouldBe(ad);
        hit.ReadInNotificationFeed.ShouldBeFalse();
    }

    [Fact]
    public async Task Run_AnalyzesTheHitTable_OncePerCompletedRun()
    {
        await WorldAsync();

        await RunAsync(Now);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT last_analyze FROM pg_stat_user_tables WHERE relname = 'company_watch_criterion_ad_hits'",
            connection);
        (await command.ExecuteScalarAsync(Ct)).ShouldNotBe(DBNull.Value);
    }

    [Fact]
    public async Task Run_PropagatesCancellation()
    {
        await WorldAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => NewJob(new FixedClock(Now)).RunAsync(cancelled.Token));
    }

    // ─────────────────────────── The world of one test

    private sealed record World(
        Guid UserId, CompanyWatchCriterionId CriterionId, string OrgNr, string Sni, string SecondSni);

    // A user with a live account, one register company carrying two SNI codes of this test's own, and one
    // criterion on the first code, materialised by the real job unless told not to.
    private async Task<World> WorldAsync(
        bool materialise = true,
        bool tooBroad = false,
        DateTimeOffset? createdAt = null,
        (string Occupation, string Region, string Employment)? matchPreferences = null)
    {
        var sni = UniqueSni();
        var secondSni = UniqueSni();
        var orgNr = UniqueOrgNr();
        await SeedRegisterCompanyAsync(orgNr, [sni, secondSni]);
        if (tooBroad)
            await SeedRegisterRangeAsync(CompanyWatchCriterionMember.MaxPerCriterion + 1, sni);

        var userId = await SeedJobSeekerAsync(matchPreferences);
        var criterionId = await SeedCriterionAsync(userId, [sni], createdAt ?? LongAgo);
        if (materialise)
            await MaterialiseAsync();
        return new World(userId, criterionId, orgNr, sni, secondSni);
    }

    private async Task<CompanyWatchCriterionId> SecondCriterionAsync(World w)
    {
        var id = await SeedCriterionAsync(w.UserId, [w.SecondSni], LongAgo);
        await MaterialiseAsync();
        return id;
    }

    private async Task WidenToTooBroadAsync(World w, DateTimeOffset at)
    {
        await SeedRegisterRangeAsync(CompanyWatchCriterionMember.MaxPerCriterion + 1, w.SecondSni);
        await UpdateCriteriaAsync(w.CriterionId, [w.Sni, w.SecondSni], at);
        await MaterialiseAsync();
    }

    // ─────────────────────────── Running the jobs

    private Task RunAsync(DateTimeOffset at) => RunAsync(new FixedClock(at));

    private Task RunAsync(IDateTimeProvider clock) => NewJob(clock).RunAsync(Ct);

    private Task RunAsync(
        DateTimeOffset at,
        Func<ICompanyWatchCriterionAdScan, ICompanyWatchCriterionAdScan> scanFor,
        ListLoggerProvider? logs = null) =>
        NewJob(new FixedClock(at), new ScanOverridingScopeFactory(_fixture.Services, scanFor), logs).RunAsync(Ct);

    private CompanyWatchCriterionScanJob NewJob(
        IDateTimeProvider clock, IServiceScopeFactory? scopes = null, ListLoggerProvider? logs = null)
    {
        ILogger<CompanyWatchCriterionScanJob> logger = logs is null
            ? _fixture.Services.GetRequiredService<ILoggerFactory>().CreateLogger<CompanyWatchCriterionScanJob>()
            : new Logger<CompanyWatchCriterionScanJob>(LoggerFactory.Create(b => b.AddProvider(logs)));
        return new CompanyWatchCriterionScanJob(
            scopes ?? _fixture.Services.GetRequiredService<IServiceScopeFactory>(), clock, logger);
    }

    private async Task MaterialiseAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICompanyWatchCriterionMaterialiser>().MaterialiseAsync(Ct);
    }

    private async Task RunFollowScanAsync(DateTimeOffset at)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var job = new CompanyWatchScanJob(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IProtectedIdentityTokenizer>(),
            sp.GetRequiredService<IBrandGroupProvider>(),
            new FixedClock(at),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CompanyWatchScanJob>());
        await job.RunAsync(Ct);
    }

    private async Task RunBackgroundMatchingAsync(DateTimeOffset at)
    {
        var scopes = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var clock = new FixedClock(at);
        var loggers = _fixture.Services.GetRequiredService<ILoggerFactory>();
        var job = new BackgroundMatchingJob(
            scopes,
            new TopMatchMailDispatcher(
                scopes, Substitute.For<IEmailSender>(), clock, loggers.CreateLogger<TopMatchMailDispatcher>()),
            clock,
            loggers.CreateLogger<BackgroundMatchingJob>());
        await job.RunAsync(Ct);
    }

    // ─────────────────────────── Seeding through the production writers

    private async Task<Guid> SeedJobSeekerAsync((string Occupation, string Region, string Employment)? preferences)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(LongAgo);
        var jobSeeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock).Value;
        if (preferences is { } p)
        {
            jobSeeker.UpdateMatchPreferences(MatchPreferences.Create(
                preferredOccupationGroups: [p.Occupation],
                preferredRegions: [p.Region],
                preferredEmploymentTypes: [p.Employment],
                preferredMunicipalities: null,
                preferredSkills: []).Value, clock);
        }

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(Ct);
        return jobSeeker.UserId;
    }

    private async Task<CompanyWatchCriterionId> SeedCriterionAsync(
        Guid userId, string[] sni, DateTimeOffset createdAt)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = CompanyWatchCriterion.Create(
            userId, CompanyWatchCriteriaSpec.Create(sni, [Kommun]).Value, null, new FixedClock(createdAt)).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(Ct);
        return criterion.Id;
    }

    private async Task UpdateCriteriaAsync(CompanyWatchCriterionId id, string[] sni, DateTimeOffset at)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, Ct);
        criterion.UpdateCriteria(CompanyWatchCriteriaSpec.Create(sni, [Kommun]).Value, new FixedClock(at))
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task FollowAsync(Guid userId, string orgNr)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CompanyWatches.Add(
            CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, new FixedClock(LongAgo)).Value);
        await db.SaveChangesAsync(Ct);
    }

    private async Task UnfollowAsync(Guid userId, string orgNr, DateTimeOffset at)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watches = await db.CompanyWatches.Where(w => w.UserId == userId).ToListAsync(Ct);
        watches.Single(w => w.OrganizationNumber!.Value == orgNr).SoftDelete(new FixedClock(at));
        await db.SaveChangesAsync(Ct);
    }

    private async Task RequestDeletionAsync(Guid userId, DateTimeOffset at)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobSeeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, Ct);
        jobSeeker.SoftDelete(new FixedClock(at));
        await db.SaveChangesAsync(Ct);
    }

    private async Task GrantMatchingConsentAsync(Guid userId, DateTimeOffset at)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobSeeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, Ct);
        jobSeeker.UpdateNotificationConsent(true, new FixedClock(at));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<JobAdId> SeedAdAsync(
        string orgNr, DateTimeOffset createdAt, (string Occupation, string Region, string Employment)? occupation = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = $"cw-criterion-{Guid.NewGuid():N}";
        var occupationJson = occupation is { } o
            ? $",\"occupation_group\":{{\"concept_id\":\"{o.Occupation}\"}},"
              + $"\"workplace_address\":{{\"region_concept_id\":\"{o.Region}\"}},"
              + $"\"employment_type\":{{\"concept_id\":\"{o.Employment}\"}}"
            : string.Empty;
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"employer\":{{\"name\":\"Bolag {orgNr}\",\"organization_number\":\"{orgNr}\"}}{occupationJson}}}";
        var jobAd = JobAd.Import(
            title: "Utvecklare",
            company: Company.Create($"Bolag {orgNr}").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: createdAt.AddHours(-1),
            expiresAt: createdAt.AddDays(60),
            clock: new FixedClock(createdAt), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id;
    }

    private async Task ArchiveAsync(JobAdId id)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.SingleAsync(j => j.Id == id, Ct);
        ad.Archive(new FixedClock(Now)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedRegisterCompanyAsync(string orgNr, string[] sni)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            VALUES ({0}, {1}, {2}, NULL, {3}, false, '1', 'Active', now(), now());
            """,
            [orgNr, "Bolag " + orgNr, Kommun, sni], Ct);
    }

    // Enough legal-entity companies on one SNI code to put a criterion over the breadth gate. The third digit is
    // 8, so no generated org.nr is personnummer-shaped, and the key space is disjoint from UniqueOrgNr's.
    private async Task SeedRegisterRangeAsync(int count, string sni)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prefix = "558" + sni[..2];
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            SELECT {0} || lpad(i::text, 5, '0'), 'Bolag ' || i, {1}, NULL,
                   ARRAY[{2}], false, '1', 'Active', now(), now()
            FROM generate_series(1, {3}) i
            ON CONFLICT (organization_number) DO NOTHING;
            """,
            [prefix, Kommun, sni, count], Ct);
    }

    // ─────────────────────────── Reading back

    private async Task<List<CompanyWatchCriterionAdHit>> HitsAsync(Guid userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CompanyWatchCriterionAdHits.AsNoTracking().Where(h => h.UserId == userId).ToListAsync(Ct);
    }

    private async Task<List<FollowedCompanyAdHit>> FollowHitsAsync(Guid userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits.AsNoTracking().Where(h => h.UserId == userId).ToListAsync(Ct);
    }

    private async Task<CompanyWatchCriterion> CriterionAsync(CompanyWatchCriterionId id)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CompanyWatchCriteria.AsNoTracking().SingleAsync(c => c.Id == id, Ct);
    }

    private async Task<UserJobAdMatch?> MatchAsync(Guid userId, JobAdId jobAdId)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.JobAdId == jobAdId, Ct);
    }

    // A five-digit SNI code and a legal-entity org.nr ("552" + seven digits: third digit 2) unique to the call.
    private static string UniqueSni() =>
        (10000 + Random.Shared.Next(0, 90000)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string UniqueOrgNr() =>
        "552" + Random.Shared.Next(0, 10_000_000).ToString("D7", System.Globalization.CultureInfo.InvariantCulture);

    // ─────────────────────────── Test doubles for the two failure actors

    // Resolves every service from the real graph and wraps only the scan port.
    private sealed class ScanOverridingScopeFactory(
        IServiceProvider root, Func<ICompanyWatchCriterionAdScan, ICompanyWatchCriterionAdScan> wrap)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(root.CreateScope(), wrap);

        private sealed class Scope(
            IServiceScope inner, Func<ICompanyWatchCriterionAdScan, ICompanyWatchCriterionAdScan> wrap)
            : IServiceScope, IServiceProvider, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(ICompanyWatchCriterionAdScan)
                    ? wrap(inner.ServiceProvider.GetRequiredService<ICompanyWatchCriterionAdScan>())
                    : inner.ServiceProvider.GetService(serviceType);

            public void Dispose() => inner.Dispose();

            public ValueTask DisposeAsync()
            {
                if (inner is IAsyncDisposable asyncInner)
                    return asyncInner.DisposeAsync();
                inner.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowingScan(ICompanyWatchCriterionAdScan inner, CompanyWatchCriterionId failing)
        : ICompanyWatchCriterionAdScan
    {
        public ValueTask<MaterialisedNewAdIds> ScanNewActiveAdIdsAsync(
            CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint,
            DateTimeOffset sinceExclusive, DateTimeOffset untilInclusive, CancellationToken cancellationToken) =>
            criterionId == failing
                ? throw new NpgsqlException("Exception while reading from stream", new TimeoutException())
                : inner.ScanNewActiveAdIdsAsync(criterionId, fingerprint, sinceExclusive, untilInclusive, cancellationToken);
    }

    private sealed class DeletingScan(
        ICompanyWatchCriterionAdScan inner, CompanyWatchCriterionId doomed, string connectionString)
        : ICompanyWatchCriterionAdScan
    {
        public async ValueTask<MaterialisedNewAdIds> ScanNewActiveAdIdsAsync(
            CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint,
            DateTimeOffset sinceExclusive, DateTimeOffset untilInclusive, CancellationToken cancellationToken)
        {
            var answer = await inner.ScanNewActiveAdIdsAsync(
                criterionId, fingerprint, sinceExclusive, untilInclusive, cancellationToken);
            if (criterionId == doomed)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    "DELETE FROM company_watch_criteria WHERE id = @id", connection);
                command.Parameters.AddWithValue("id", criterionId.Value);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return answer;
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => [.. _messages];

        public ILogger CreateLogger(string categoryName) => new ListLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class ListLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    private sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : IDateTimeProvider
    {
        private DateTimeOffset _next = start;

        public DateTimeOffset UtcNow
        {
            get
            {
                var value = _next;
                _next += step;
                return value;
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
