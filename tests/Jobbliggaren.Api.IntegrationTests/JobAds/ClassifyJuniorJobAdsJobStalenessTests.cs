using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Domain.JobAds;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.JobAds.JuniorClassificationHarness;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2264 (ADR 0187) — what the junior sweep selects, on real Postgres: a listed ad in an enabled field whose assessment
/// is missing, about other content (unequal fingerprints) or from other rules (another rule-set version), and nothing
/// else. Its own database per test, because the counts, the written tables and the exact set of assessments are the
/// assertion. Every row comes from the actor <see cref="JuniorClassificationHarness"/> names.
/// </summary>
/// <remarks>
/// Between two sweeps the clock moves on, so a sweep that re-classified an ad would stamp a new
/// <c>ClassifiedAt</c>: an unchanged instant, no counts and no write command are three readings of "not selected".
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class ClassifyJuniorJobAdsJobStalenessTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly JuniorClassificationHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task RunAsync_ShouldClassifyAnAdWrittenBeforeTheMigrationOnce_AndAgainOnlyAfterItsNextSourceWrite()
    {
        var id = await _h.ImportAsync("Junior utvecklare");
        await _h.ApplyTheJuniorMigrationOverTheStoredAdsAsync();

        // The migration's own transform: a stored ad reads NULL in both new columns.
        var migrated = await _h.JuniorInputsAsync(id);
        migrated.Fingerprint.ShouldBeNull();
        migrated.ExperienceRequired.ShouldBeNull();

        _h.Commands.Clear();
        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(1, 0, 0, 0, 0));
        _h.Commands.WrittenTables.ShouldBe(["job_ad_junior_assessments"], ignoreOrder: false, "the sweep never writes job_ads");
        var first = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        first.Verdict.ShouldBe(JuniorVerdict.Junior);
        first.ClassifiedFingerprint.ShouldBeNull();
        first.ClassifiedAt.ShouldBe(Now);

        // A null stamped over a null fingerprint is equal: the ad is not selected on every tick.
        _h.Clock.UtcNow = Now.AddMinutes(2);
        _h.Commands.Clear();
        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
        _h.Commands.WrittenTables.ShouldBeEmpty();
        (await _h.AssessmentAsync(id)).ShouldNotBeNull().ClassifiedAt.ShouldBe(Now);

        // Its next source write: the stream re-imports it unchanged, and the write stamps a fingerprint.
        await _h.RewriteAsync(id);
        var stamped = (await _h.JuniorInputsAsync(id)).Fingerprint.ShouldNotBeNull();

        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(1, 0, 0, 0, 0));
        var second = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        second.ClassifiedFingerprint.ShouldBe(stamped);
        second.ClassifiedAt.ShouldBe(Now.AddMinutes(2));
    }

    [Fact]
    public async Task RunAsync_ShouldNotSelectAnAd_WhenASourceWriteLeavesWhatTheRulesReadUnchanged()
    {
        var id = await _h.ImportAsync("Junior utvecklare", expiresAt: Now.AddDays(10));
        (await _h.SweepAsync()).Total.ShouldBe(1);
        var before = (await _h.JuniorInputsAsync(id)).Fingerprint.ShouldNotBeNull();

        // The stream re-imports it with a new last day only, which no rule reads.
        _h.Clock.UtcNow = Now.AddMinutes(2);
        await _h.RewriteAsync(id, expiresAt: Now.AddDays(20));
        (await _h.JuniorInputsAsync(id)).Fingerprint.ShouldBe(before);

        _h.Commands.Clear();
        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
        _h.Commands.WrittenTables.ShouldBeEmpty();
        var assessment = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        assessment.ClassifiedFingerprint.ShouldBe(before);
        assessment.ClassifiedAt.ShouldBe(Now);
    }

    public static TheoryData<string, JuniorVerdict, string> InputChanges => new()
    {
        { "title", JuniorVerdict.NotJunior, "no-signal" },
        { "text", JuniorVerdict.Unclear, "text-mentor" },
        { "occupation group", JuniorVerdict.Junior, "title-junior" },
        { "experience mark", JuniorVerdict.Junior, "title-junior" },
    };

    [Theory]
    [MemberData(nameof(InputChanges))]
    public async Task RunAsync_ShouldReclassifyAnAd_WhenASourceWriteChangesWhatTheRulesRead_AndKeepItsDecision(
        string change, JuniorVerdict verdict, string ruleId)
    {
        var id = await _h.ImportAsync("Junior utvecklare");
        (await _h.SweepAsync()).Total.ShouldBe(1);
        (await _h.DecideAsync(id, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        var before = (await _h.JuniorInputsAsync(id)).Fingerprint;

        _h.Clock.UtcNow = Now.AddMinutes(2);
        switch (change)
        {
            case "title":
                await _h.RewriteAsync(id, title: "Systemutvecklare");
                break;
            case "text":
                await _h.RewriteAsync(id, description: MentorText);
                break;
            case "occupation group":
                await _h.RewriteAsync(id, group: OtherDataIt);
                break;
            default:
                await _h.RewriteAsync(id, experienceRequired: true);
                break;
        }

        var after = (await _h.JuniorInputsAsync(id)).Fingerprint.ShouldNotBeNull();
        after.ShouldNotBe(before);

        _h.Commands.Clear();
        (await _h.SweepAsync()).Total.ShouldBe(1);
        _h.Commands.WrittenTables.ShouldBe(["job_ad_junior_assessments"]);
        var assessment = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        assessment.ClassifiedFingerprint.ShouldBe(after);
        assessment.ClassifiedAt.ShouldBe(Now.AddMinutes(2));
        (assessment.Verdict, assessment.RuleId).ShouldBe((verdict, ruleId));
        assessment.Decision.ShouldBe(JuniorDecision.NotJunior);
        assessment.DecidedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task RunAsync_ShouldReclassifyEveryListedAdInTheField_WhenTheRulesChange_AndKeepEveryDecision()
    {
        var vetoed = await _h.ImportAsync("Junior utvecklare");
        var raised = await _h.ImportAsync("Junior utvecklare", description: MentorText);
        var noSignal = await _h.ImportAsync("Systemutvecklare");
        var trainee = await _h.ImportAsync("Trainee inom IT", group: OtherDataIt);
        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(2, 1, 1, 0, 0));
        (await _h.DecideAsync(vetoed, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(raised, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();

        // A deploy of a new rule file: the title rule is no longer automatic, so the version (with its content hash)
        // changes while no ad's content does.
        var previous = _h.Rules.Version;
        _h.Rules = LoadRules(titleRuleIsAuto: false);
        _h.Rules.Version.ShouldNotBe(previous);
        _h.Clock.UtcNow = Now.AddMinutes(2);

        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(0, 1, 3, 0, 0));

        var assessments = (await _h.AssessmentsAsync()).ToDictionary(a => a.Id.Value);
        assessments.Keys.ShouldBe([vetoed, raised, noSignal, trainee], ignoreOrder: true);
        assessments.Values.ShouldAllBe(a => a.RuleSetVersion == _h.Rules.Version);
        assessments.Values.ShouldAllBe(a => a.ClassifiedAt == Now.AddMinutes(2));
        assessments[vetoed].Decision.ShouldBe(JuniorDecision.NotJunior);
        assessments[vetoed].DecidedAt.ShouldBe(Now);
        assessments[raised].Decision.ShouldBe(JuniorDecision.Junior);
        assessments[raised].DecidedAt.ShouldBe(Now);
        assessments[noSignal].Decision.ShouldBeNull();
        assessments[trainee].Decision.ShouldBeNull();
        (assessments[trainee].Verdict, assessments[trainee].RuleId).ShouldBe((JuniorVerdict.Unclear, "title-junior"));

        // And once every ad carries the new version, nothing is selected.
        _h.Clock.UtcNow = Now.AddMinutes(4);
        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
    }

    [Fact]
    public async Task RunAsync_ShouldNeverClassifyAnAdOutsideTheEnabledField()
    {
        var inField = await _h.ImportAsync("Junior utvecklare");
        await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt);
        // The synthetic reading names it a junior job; its group is outside the field all the same.
        await _h.ImportAsync("Systemutvecklare", group: NotDataIt, externalId: CuratedOutsideField);
        // An ad whose source carries no occupation group (PlatsbankenJobSource.MapFacets,
        // PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone).
        await _h.ImportAsync("Junior utvecklare", group: null);
        await _h.CreateManuallyAsync("Junior utvecklare");

        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(1, 0, 0, 0, 0));

        (await _h.AssessmentsAsync()).Select(a => a.Id.Value).ShouldBe([inField]);
        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
    }

    [Fact]
    public async Task RunAsync_ShouldNeverReclassifyAnAd_ThatASourceWriteMovedOutOfTheField()
    {
        var id = await _h.ImportAsync("Junior utvecklare");
        (await _h.SweepAsync()).Total.ShouldBe(1);
        var classified = (await _h.AssessmentAsync(id)).ShouldNotBeNull().ClassifiedFingerprint;

        // The source re-files the ad under a group outside Data/IT: its fingerprint changes.
        _h.Clock.UtcNow = Now.AddMinutes(2);
        await _h.RewriteAsync(id, group: NotDataIt);
        (await _h.JuniorInputsAsync(id)).Fingerprint.ShouldNotBe(classified);

        _h.Commands.Clear();
        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
        _h.Commands.WrittenTables.ShouldBeEmpty();
        var assessment = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        assessment.ClassifiedFingerprint.ShouldBe(classified);
        assessment.ClassifiedAt.ShouldBe(Now);
    }

    public static TheoryData<string> Removals =>
    [
        "archived by the stream's removal event",
        "past its deadline",
        "archived by ExpireJobAdsJob",
        "erased by the erasure command",
    ];

    [Theory]
    [MemberData(nameof(Removals))]
    public async Task RunAsync_ShouldNeverClassifyAnAd_ThatStoppedBeingListedBeforeItsFirstSweep(string removal)
    {
        var kept = await _h.ImportAsync("Junior utvecklare");
        var closed = await _h.ImportAsync(
            "Trainee inom IT", description: NamingTheRecruiter(NeutralText), expiresAt: DeadlineFor(removal));

        await CloseAsync(closed, removal);

        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(1, 0, 0, 0, 0));
        (await _h.AssessmentsAsync()).Select(a => a.Id.Value).ShouldBe([kept]);
    }

    [Theory]
    [InlineData("archived by the stream's removal event")]
    [InlineData("past its deadline")]
    [InlineData("archived by ExpireJobAdsJob")]
    public async Task RunAsync_ShouldNeverReclassifyAnAd_ThatStoppedBeingListed_WhenTheRulesChange(string removal)
    {
        var closed = await _h.ImportAsync("Trainee inom IT", expiresAt: DeadlineFor(removal));
        (await _h.SweepAsync()).Total.ShouldBe(1);
        var classifiedUnder = _h.Rules.Version;

        await CloseAsync(closed, removal);
        _h.Rules = LoadRules(titleRuleIsAuto: false);

        _h.Commands.Clear();
        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
        _h.Commands.WrittenTables.ShouldBeEmpty();
        var assessment = (await _h.AssessmentAsync(closed)).ShouldNotBeNull();
        assessment.RuleSetVersion.ShouldBe(classifiedUnder);
        assessment.ClassifiedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task RunAsync_ShouldWriteNothing_WhenNothingIsStale()
    {
        var junior = await _h.ImportAsync("Junior utvecklare");
        var unclear = await _h.ImportAsync("Junior utvecklare", description: MentorText);
        await _h.ImportAsync("Systemutvecklare");
        await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt);
        var closed = await _h.ImportAsync("Praktikant");
        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(2, 1, 1, 0, 0));
        (await _h.DecideAsync(junior, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(unclear, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        await _h.ArchiveThroughTheStreamAsync(closed);

        _h.Clock.UtcNow = Now.AddMinutes(2);
        _h.Commands.Clear();

        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);

        _h.Commands.WrittenTables.ShouldBeEmpty();
        var assessments = await _h.AssessmentsAsync();
        assessments.Count.ShouldBe(4);
        assessments.ShouldAllBe(a => a.ClassifiedAt == Now);
    }

    [Fact]
    public async Task RunAsync_ShouldDrainABacklogLargerThanABatch_InOneTick()
    {
        await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddHours(-1));
        await _h.ImportAsync("Trainee inom IT", publishedAt: Now.AddHours(-2));
        await _h.ImportAsync("Systemutvecklare", publishedAt: Now.AddHours(-3));

        (await _h.SweepAsync(batchSize: 1)).ShouldBe(new ClassifyJuniorJobAdsCounts(2, 1, 0, 0, 0));

        (await _h.AssessmentsAsync()).Count.ShouldBe(3);
        (await _h.SweepAsync(batchSize: 1)).ShouldBe(ClassifyJuniorJobAdsCounts.None);
    }

    private static DateTimeOffset? DeadlineFor(string removal) =>
        removal is "past its deadline" or "archived by ExpireJobAdsJob" ? Now.AddMinutes(1) : null;

    private async Task CloseAsync(Guid id, string removal)
    {
        switch (removal)
        {
            case "archived by the stream's removal event":
                await _h.ArchiveThroughTheStreamAsync(id);
                break;
            case "past its deadline":
                _h.Clock.UtcNow = Now.AddMinutes(2);
                break;
            case "archived by ExpireJobAdsJob":
                _h.Clock.UtcNow = Now.AddMinutes(2);
                await _h.RunExpireJobAdsJobAsync();
                (await _h.JuniorInputsAsync(id)).Status.ShouldBe(JobAdStatus.Archived);
                break;
            default:
                (await _h.EraseAsync(RecruiterName)).Erased.JobAds.ShouldBe(1);
                (await _h.JuniorInputsAsync(id)).Status.ShouldBe(JobAdStatus.Erased);
                break;
        }
    }
}
