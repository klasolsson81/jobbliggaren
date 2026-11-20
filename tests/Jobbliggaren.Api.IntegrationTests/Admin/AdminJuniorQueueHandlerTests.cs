using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.JobAds;
using Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.JobAds.JuniorClassificationHarness;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #2264 (ADR 0187) — the admin junior queue's two handlers on real Postgres: <c>ListJuniorJobAdsQueryHandler</c>'s
/// three tabs and their counts, and <c>DecideJuniorJobAdCommandHandler</c>'s decision and its eligibility (assessed,
/// listed now, in an enabled field). Its own database per test, because the exact tabs and counts are the assertion.
/// Every verdict is the real sweep's and every decision the real handler's (<see cref="JuniorClassificationHarness"/>).
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class AdminJuniorQueueHandlerTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly JuniorClassificationHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task List_ShouldPutEveryListedAssessedAdInItsTabs_AndCountEveryTab_WhateverTheTab()
    {
        var shownByRule = await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddHours(-1));
        var unclear = await _h.ImportAsync("Junior utvecklare", description: MentorText, publishedAt: Now.AddHours(-2));
        await _h.ImportAsync("Systemutvecklare", publishedAt: Now.AddHours(-3));
        var vetoed = await _h.ImportAsync("Trainee inom IT", publishedAt: Now.AddHours(-4));
        var confirmed = await _h.ImportAsync("Praktikant", publishedAt: Now.AddHours(-5));
        var raised = await _h.ImportAsync("Junior utvecklare", description: MentorText, publishedAt: Now.AddHours(-6));
        var overruled = await _h.ImportAsync("Systemutvecklare", publishedAt: Now.AddHours(-7));
        await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt, publishedAt: Now.AddHours(-8));
        (await _h.SweepAsync()).Total.ShouldBe(7);
        (await _h.DecideAsync(vetoed, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(confirmed, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(raised, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(overruled, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        // Listed and in the field, but the sweep has not reached it yet.
        await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddMinutes(-1));

        var expected = new Dictionary<JuniorQueueView, Guid[]>
        {
            [JuniorQueueView.Unclear] = [unclear],
            [JuniorQueueView.Shown] = [shownByRule, confirmed, raised, overruled],
            [JuniorQueueView.Decided] = [vetoed, confirmed, raised, overruled],
        };

        foreach (var (view, ids) in expected)
        {
            var queue = await _h.ListAsync(view);

            queue.Counts.ShouldBe(new JuniorJobAdQueueCountsDto(Unclear: 1, Shown: 4, Decided: 4), view.ToString());
            queue.Items.Items.Select(i => i.Id).ShouldBe(ids, ignoreOrder: false, view.ToString());
            queue.Items.TotalCount.ShouldBe(ids.Length, view.ToString());
            queue.Items.Page.ShouldBe(1);
            queue.Items.PageSize.ShouldBe(ListJuniorJobAdsQuery.PageSize);
        }
    }

    // The admin's "Visas" tab says what is on /junior now: both read through their real handlers, over the same rows.
    [Fact]
    public async Task List_TheShownTab_ShouldHoldExactlyTheAdsTheJuniorListShows_ForTheSameData()
    {
        var byRule = await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddHours(-1));
        var vetoed = await _h.ImportAsync("Trainee inom IT", publishedAt: Now.AddHours(-2));
        var unclear = await _h.ImportAsync("Junior utvecklare", description: MentorText, publishedAt: Now.AddHours(-3));
        var raised = await _h.ImportAsync("Junior utvecklare", description: MentorText, publishedAt: Now.AddHours(-4));
        var overruled = await _h.ImportAsync("Systemutvecklare", publishedAt: Now.AddHours(-5));
        var read = await _h.ImportAsync("Systemutvecklare", externalId: CuratedInField, publishedAt: Now.AddHours(-6));
        await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt, publishedAt: Now.AddHours(-7));
        await _h.ImportAsync("Systemutvecklare", group: NotDataIt, externalId: CuratedOutsideField, publishedAt: Now.AddHours(-8));
        var closed = await _h.ImportAsync("Praktikant", publishedAt: Now.AddHours(-9));
        (await _h.SweepAsync()).ShouldBe(new ClassifyJuniorJobAdsCounts(Junior: 4, NotJunior: 1, Unclear: 2, Refused: 0, Failed: 0));
        (await _h.DecideAsync(vetoed, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(raised, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        (await _h.DecideAsync(overruled, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        await _h.ArchiveThroughTheStreamAsync(closed);
        // Listed and in the field, but the sweep has not reached it yet.
        await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddMinutes(-1));

        var shown = await _h.ListAsync(JuniorQueueView.Shown);
        var junior = await _h.PublicListAsync();

        shown.Items.Items.Select(i => i.Id).ShouldBe([byRule, raised, overruled, read]);
        junior.Rows.Items.Select(r => r.Id).ShouldBe(shown.Items.Items.Select(i => i.Id));
        junior.Rows.TotalCount.ShouldBe(shown.Counts.Shown);
        shown.Items.TotalCount.ShouldBe(shown.Counts.Shown);
        shown.Items.Items.Select(i => i.Id).ShouldNotContain(unclear);
    }

    [Fact]
    public async Task List_ShouldShapeARow_FromWhatTheAdShows_AndWhatTheRulesAndTheAdminSaid()
    {
        var id = await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddDays(-1), expiresAt: Now.AddDays(10));
        await _h.SweepAsync();
        (await _h.DecideAsync(id, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();

        var queue = await _h.ListAsync(JuniorQueueView.Decided);

        queue.Items.Items.ShouldBe([new JuniorJobAdQueueItemDto(
            id, "Junior utvecklare", "Exempelbolaget AB", "Stockholm", Now.AddDays(-1), Now.AddDays(10),
            JuniorVerdict.Junior, "title-junior", JuniorDecision.NotJunior)]);
    }

    [Fact]
    public async Task List_ShouldReadANarrowProjection_NeverTheAdsText()
    {
        await _h.ImportAsync("Junior utvecklare", description: MentorText);
        await _h.SweepAsync();
        _h.Commands.Clear();

        await _h.ListAsync(JuniorQueueView.Unclear);

        var reads = _h.Commands.Commands.Where(c => c.Contains("job_ads", StringComparison.Ordinal)).ToList();
        reads.Count.ShouldBe(4, "the three counts and the page");
        foreach (var text in reads)
        {
            text.ShouldContain("job_ad_junior_assessments");
            text.ShouldNotContain("description", Case.Insensitive);
            text.ShouldNotContain("raw_payload", Case.Insensitive);
            text.ShouldNotContain("organization_number", Case.Insensitive);
            text.ShouldNotContain("contacts", Case.Insensitive);
        }

        typeof(JuniorJobAdQueueItemDto).GetProperties().Select(p => p.Name).ShouldBe(
            ["Id", "Title", "CompanyName", "MunicipalityName", "PublishedAt", "ExpiresAt", "Verdict", "RuleId", "Decision"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task List_ShouldPageTwentyFiveRows_NewestFirst()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < ListJuniorJobAdsQuery.PageSize + 1; i++)
            ids.Add(await _h.ImportAsync("Junior utvecklare", publishedAt: Now.AddMinutes(-1 - i)));
        await _h.SweepAsync();

        var first = await _h.ListAsync(JuniorQueueView.Shown);
        var second = await _h.ListAsync(JuniorQueueView.Shown, page: 2);

        first.Items.Items.Select(i => i.Id).ShouldBe(ids.Take(ListJuniorJobAdsQuery.PageSize));
        second.Items.Items.Select(i => i.Id).ShouldBe([ids[^1]]);
        second.Items.TotalCount.ShouldBe(ListJuniorJobAdsQuery.PageSize + 1);
        second.Items.Page.ShouldBe(2);
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
    public async Task List_AnAdThatStopsBeingListed_ShouldLeaveEveryTabAndEveryCount(string removal)
    {
        var deadline = DeadlineFor(removal);
        var unclear = await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(MentorText), expiresAt: deadline);
        var decided = await _h.ImportAsync("Trainee inom IT", description: NamingTheRecruiter(NeutralText), expiresAt: deadline);
        await _h.SweepAsync();
        (await _h.DecideAsync(decided, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        (await _h.ListAsync(JuniorQueueView.Unclear)).Counts.ShouldBe(new JuniorJobAdQueueCountsDto(1, 1, 1));

        await CloseAsync([unclear, decided], removal);

        foreach (var view in Enum.GetValues<JuniorQueueView>())
        {
            var queue = await _h.ListAsync(view);
            queue.Counts.ShouldBe(new JuniorJobAdQueueCountsDto(0, 0, 0), view.ToString());
            queue.Items.Items.ShouldBeEmpty(view.ToString());
            queue.Items.TotalCount.ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(JuniorDecision.Junior)]
    [InlineData(JuniorDecision.NotJunior)]
    public async Task Decide_ShouldRecordTheDecision_AndLeaveTheVerdict(JuniorDecision decision)
    {
        var id = await _h.ImportAsync("Junior utvecklare", description: MentorText);
        await _h.SweepAsync();
        _h.Clock.UtcNow = Now.AddMinutes(5);

        var result = await _h.DecideAsync(id, decision);

        result.IsSuccess.ShouldBeTrue();
        var assessment = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        assessment.Decision.ShouldBe(decision);
        assessment.DecidedAt.ShouldBe(Now.AddMinutes(5));
        (assessment.Verdict, assessment.RuleId).ShouldBe((JuniorVerdict.Unclear, "text-mentor"));
        assessment.ClassifiedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Decide_Null_ShouldHandTheAdBackToTheRules()
    {
        var id = await _h.ImportAsync("Junior utvecklare", description: MentorText);
        await _h.SweepAsync();
        (await _h.DecideAsync(id, JuniorDecision.Junior)).IsSuccess.ShouldBeTrue();
        (await _h.ListAsync(JuniorQueueView.Shown)).Items.Items.Select(i => i.Id).ShouldBe([id]);
        _h.Clock.UtcNow = Now.AddMinutes(5);

        var result = await _h.DecideAsync(id, null);

        result.IsSuccess.ShouldBeTrue();
        var assessment = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        assessment.Decision.ShouldBeNull();
        assessment.DecidedAt.ShouldBeNull();
        var queue = await _h.ListAsync(JuniorQueueView.Unclear);
        queue.Items.Items.Select(i => i.Id).ShouldBe([id]);
        queue.Counts.ShouldBe(new JuniorJobAdQueueCountsDto(1, 0, 0));
    }

    [Fact]
    public async Task Decide_ShouldBeNotFound_ForAListedAdTheSweepHasNotAssessed()
    {
        var id = await _h.ImportAsync("Junior utvecklare");

        var result = await _h.DecideAsync(id, JuniorDecision.Junior);

        ShouldBeNotFound(result);
        (await _h.AssessmentAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task Decide_ShouldBeNotFound_ForAnIdNoAdHas()
    {
        ShouldBeNotFound(await _h.DecideAsync(Guid.NewGuid(), JuniorDecision.Junior));
    }

    [Theory]
    [InlineData("archived by the stream's removal event")]
    [InlineData("past its deadline")]
    [InlineData("archived by ExpireJobAdsJob")]
    public async Task Decide_ShouldBeNotFound_AndChangeNothing_ForAnAssessedAdThatIsNoLongerListed(string removal)
    {
        var id = await _h.ImportAsync("Junior utvecklare", description: MentorText, expiresAt: DeadlineFor(removal));
        await _h.SweepAsync();
        await CloseAsync([id], removal);

        var result = await _h.DecideAsync(id, JuniorDecision.Junior);

        ShouldBeNotFound(result);
        var assessment = (await _h.AssessmentAsync(id)).ShouldNotBeNull();
        assessment.Decision.ShouldBeNull();
        assessment.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Decide_ShouldBeNotFound_ForAnErasedAd()
    {
        var id = await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(MentorText));
        await _h.SweepAsync();
        await CloseAsync([id], "erased by the erasure command");

        ShouldBeNotFound(await _h.DecideAsync(id, JuniorDecision.Junior));
    }

    [Fact]
    public async Task Decide_ShouldBeNotFound_AndChangeNothing_ForAnAssessedAdThatASourceWriteMovedOutOfTheField()
    {
        var id = await _h.ImportAsync("Junior utvecklare", description: MentorText);
        await _h.SweepAsync();
        await _h.RewriteAsync(id, group: NotDataIt);

        var result = await _h.DecideAsync(id, JuniorDecision.Junior);

        ShouldBeNotFound(result);
        (await _h.AssessmentAsync(id)).ShouldNotBeNull().Decision.ShouldBeNull();
    }

    [Fact]
    public async Task Decide_ShouldBeNotFound_ForAnAdOutsideTheField()
    {
        var id = await _h.ImportAsync("Junior ekonomiassistent", group: NotDataIt);
        await _h.SweepAsync();

        ShouldBeNotFound(await _h.DecideAsync(id, JuniorDecision.Junior));
        (await _h.AssessmentAsync(id)).ShouldBeNull();
    }

    private static void ShouldBeNotFound(Result result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    private static DateTimeOffset? DeadlineFor(string removal) =>
        removal is "past its deadline" or "archived by ExpireJobAdsJob" ? Now.AddMinutes(1) : null;

    private async Task CloseAsync(IReadOnlyList<Guid> ids, string removal)
    {
        switch (removal)
        {
            case "archived by the stream's removal event":
                foreach (var id in ids)
                    await _h.ArchiveThroughTheStreamAsync(id);
                break;
            case "past its deadline":
                _h.Clock.UtcNow = Now.AddMinutes(2);
                break;
            case "archived by ExpireJobAdsJob":
                _h.Clock.UtcNow = Now.AddMinutes(2);
                await _h.RunExpireJobAdsJobAsync();
                foreach (var id in ids)
                    (await _h.JuniorInputsAsync(id)).Status.ShouldBe(JobAdStatus.Archived);
                break;
            default:
                (await _h.EraseAsync(RecruiterName)).Erased.JobAds.ShouldBe(ids.Count);
                break;
        }
    }
}
