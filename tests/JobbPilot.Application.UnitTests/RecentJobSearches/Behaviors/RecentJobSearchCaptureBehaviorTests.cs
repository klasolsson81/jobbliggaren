using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.RecentJobSearches.Abstractions;
using JobbPilot.Application.RecentJobSearches.Behaviors;
using JobbPilot.Application.RecentJobSearches.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.SavedSearches;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.RecentJobSearches.Behaviors;

// C2 (ADR 0067, CTO-dom (d)/(e) + architect F6) — RecentJobSearchCaptureBehavior:
//
//   1. ICapturesRecentSearch-shape är nu Q + OccupationGroup + Municipality +
//      Region + SortBy (Ssyk borta).
//   2. Default-browse-guarden räknar ALLA fyra dimensioner — en yrkesgrupp-only
//      eller kommun-only-sökning ska captureras (stänger C1:s LIVE capture-gap:
//      guarden räknade bara Q/Ssyk/Region → OccupationGroup/Municipality-
//      sökningar capturerades aldrig).
//   3. SearchCriteria.Create anropas med nya signaturen (named args — tre
//      likatypade listor i rad).
//
// RÖD tills interface + behavior uppdaterats. Behaviorn instansieras direkt
// (Mediator.SourceGenerator — pipeline-behaviors är vanliga klasser).
public class RecentJobSearchCaptureBehaviorTests
{
    // Fake-message som matchar nya ICapturesRecentSearch-shapen.
    // E2j (ADR 0060 amend 2026-06-12): Commit-markören gatar capturen —
    // default = true här så de befintliga capture-väntande testerna
    // (commit-intent) består; commit-guarden testas explicit nedan.
    public sealed record FakeSearchQuery(
        string? Q,
        IReadOnlyList<string>? OccupationGroup,
        IReadOnlyList<string>? Municipality,
        IReadOnlyList<string>? Region,
        IReadOnlyList<string>? EmploymentType = null,
        IReadOnlyList<string>? WorktimeExtent = null,
        JobAdSortBy SortBy = JobAdSortBy.PublishedAtDesc,
        bool Commit = true)
        : IQuery<FakeCaptureResponse>, ICapturesRecentSearch;

    // Message UTAN markören — behaviorn ska vara no-op.
    public sealed record FakePlainQuery : IQuery<FakeCaptureResponse>;

    public sealed record FakeCaptureResponse(int TotalCount) : IRecentSearchCaptureResponse;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IRecentJobSearchCapturer _capturer = Substitute.For<IRecentJobSearchCapturer>();
    private readonly Guid _userId = Guid.NewGuid();

    public RecentJobSearchCaptureBehaviorTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private RecentJobSearchCaptureBehavior<TMessage, TResponse> CreateBehavior<TMessage, TResponse>()
        where TMessage : IMessage =>
        new(_currentUser, _capturer,
            Substitute.For<ILogger<RecentJobSearchCaptureBehavior<TMessage, TResponse>>>());

    private static MessageHandlerDelegate<TMessage, TResponse> Next<TMessage, TResponse>(
        TResponse response)
        where TMessage : IMessage =>
        (_, _) => ValueTask.FromResult(response);

    private async ValueTask<FakeCaptureResponse> HandleAsync(
        FakeSearchQuery query, int totalCount = 7)
    {
        var behavior = CreateBehavior<FakeSearchQuery, FakeCaptureResponse>();
        return await behavior.Handle(
            query,
            Next<FakeSearchQuery, FakeCaptureResponse>(new FakeCaptureResponse(totalCount)),
            CancellationToken.None);
    }

    // ---------------------------------------------------------------
    // Capture sker — per dimension (C1-gapet stängs)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_OccupationGroupOnly_CapturesSearch()
    {
        // C1:s LIVE-gap: yrkesgrupp-only capturerades aldrig. C2 stänger det.
        // .Returns(...) gör anropet till konfiguration (exkluderas från
        // Received-räkning — NSubstitute-footgun annars).
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: ["grp1"], Municipality: null, Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured!.OccupationGroup.ShouldBe(["grp1"]);
        captured.Municipality.ShouldBeEmpty();
        captured.Region.ShouldBeEmpty();
        captured.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_MunicipalityOnly_CapturesSearch()
    {
        // C1:s LIVE-gap del 2: kommun-only.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: ["sthlm_kn"], Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured!.Municipality.ShouldBe(["sthlm_kn"]);
    }

    [Fact]
    public async Task Handle_RegionOnly_CapturesSearch()
    {
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: ["stockholm"]));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QOnly_CapturesSearch()
    {
        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: null, Municipality: null, Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmploymentTypeOnly_CapturesSearch()
    {
        // B2 (ADR 0067 Beslut 6/7): default-browse-guarden räknar nu alla FEM
        // dims → en commit:ad sökning med ENBART EmploymentType släpps igenom.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            EmploymentType: ["et_fast"], WorktimeExtent: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured!.EmploymentType.ShouldBe(["et_fast"]);
        captured.WorktimeExtent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WorktimeExtentOnly_CapturesSearch()
    {
        // B2: spegelbild — enbart WorktimeExtent (Q + övriga tomma, Commit=true).
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            EmploymentType: null, WorktimeExtent: ["wt_heltid"]));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured!.WorktimeExtent.ShouldBe(["wt_heltid"]);
    }

    [Fact]
    public async Task Handle_MapsDimensionsToCorrectCriteriaFields()
    {
        // Positionell tyst-fel-grind (architect F1: named args obligatoriskt):
        // distinkta värden per dimension bevisar att inget fält förväxlats.
        // B2: utökad till alla fem listor.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: "lärare",
            OccupationGroup: ["dim-grp"],
            Municipality: ["dim-kn"],
            Region: ["dim-reg"],
            EmploymentType: ["dim-et"],
            WorktimeExtent: ["dim-wt"],
            SortBy: JobAdSortBy.PublishedAtAsc));

        captured.ShouldNotBeNull();
        captured!.OccupationGroup.ShouldBe(["dim-grp"]);
        captured.Municipality.ShouldBe(["dim-kn"]);
        captured.Region.ShouldBe(["dim-reg"]);
        captured.EmploymentType.ShouldBe(["dim-et"]);
        captured.WorktimeExtent.ShouldBe(["dim-wt"]);
        captured.Q.ShouldBe("lärare");
        captured.SortBy.ShouldBe(JobAdSortBy.PublishedAtAsc);
    }

    [Fact]
    public async Task Handle_PassesResponseTotalCountToCapturer()
    {
        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: null, Municipality: null, Region: null),
            totalCount: 42);

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 42, Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------
    // Default-browse-guard — no-op när ALLA fyra dimensioner tomma
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_AllDimensionsEmpty_DoesNotCapture()
    {
        // Default-browse ("alla annonser, inget filter") får ALDRIG captureras
        // (data-minimering Art. 5(1)(c), security-auditor F6 P4a High-2).
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null));

        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhitespaceQAndEmptyLists_DoesNotCapture()
    {
        await HandleAsync(new FakeSearchQuery(
            Q: "   ", OccupationGroup: [], Municipality: [], Region: []));

        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------
    // Commit-guard (E2j, ADR 0060 amend 2026-06-12) — capture endast vid
    // commit-intent. Live-typing (router.replace per ord) sätter commit=false
    // och får ALDRIG fångas (over-capture + data-minimerings-regression,
    // Art. 5(1)(c)). Sök/Enter/förslags-val/toolbar sätter commit=true.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_CommitFalse_DoesNotCapture()
    {
        // Live-förhandsvisning (commit=false) med fullt giltigt filter får
        // INTE captureras — annars återinförs mellanstegsspammen.
        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: ["grp1"], Municipality: null, Region: null,
            Commit: false));

        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CommitTrue_CapturesSearch()
    {
        // Explicit commit-intent (Sök/Enter/förslags-val) → capture.
        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: null, Municipality: null, Region: null,
            Commit: true));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CommitTrueButAllDimensionsEmpty_DoesNotCapture()
    {
        // Commit-guard OCH default-browse-guard är additiva: en commit på
        // tom sökning ("Sök" utan filter) capture:as fortfarande aldrig
        // (Mekanik-not 2 består — browse-guarden ersätts inte av commit-guarden).
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Commit: true));

        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------
    // Övriga no-op-vägar + best-effort
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_AnonymousUser_DoesNotCapture()
    {
        _currentUser.UserId.Returns((Guid?)null);

        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: ["grp1"], Municipality: null, Region: null));

        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MessageWithoutMarker_DoesNotCapture()
    {
        var behavior = CreateBehavior<FakePlainQuery, FakeCaptureResponse>();
        var response = new FakeCaptureResponse(5);

        var result = await behavior.Handle(
            new FakePlainQuery(),
            Next<FakePlainQuery, FakeCaptureResponse>(response),
            CancellationToken.None);

        result.ShouldBeSameAs(response);
        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidCriteria_DoesNotCaptureButReturnsResponse()
    {
        // Q = 1 tecken → SearchCriteria.Create failar (InvalidQ) → ingen
        // capture, men queryn är orörd (best-effort).
        var result = await HandleAsync(new FakeSearchQuery(
            Q: "a", OccupationGroup: null, Municipality: null, Region: null));

        result.TotalCount.ShouldBe(7);
        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCapturerThrows_ResponseStillReturned()
    {
        // Capture-fel får ALDRIG bryta sök-queryn (500 på söksidan oacceptabelt).
        _capturer.CaptureAsync(
                Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("capture-fel"));

        var result = await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: ["grp1"], Municipality: null, Region: null));

        result.TotalCount.ShouldBe(7);
    }

    [Fact]
    public async Task Handle_ReturnsResponseUnchanged_WhenCaptureSucceeds()
    {
        var behavior = CreateBehavior<FakeSearchQuery, FakeCaptureResponse>();
        var response = new FakeCaptureResponse(3);

        var result = await behavior.Handle(
            new FakeSearchQuery(
                Q: "backend", OccupationGroup: null, Municipality: null, Region: null),
            Next<FakeSearchQuery, FakeCaptureResponse>(response),
            CancellationToken.None);

        result.ShouldBeSameAs(response);
    }

    // ---------------------------------------------------------------
    // Interface-shape-grind — Ssyk borta ur ICapturesRecentSearch
    // ---------------------------------------------------------------

    [Fact]
    public void ICapturesRecentSearch_HasNoSsykProperty_AfterC2()
    {
        typeof(ICapturesRecentSearch).GetProperty("Ssyk").ShouldBeNull();
        typeof(ICapturesRecentSearch).GetProperty("OccupationGroup").ShouldNotBeNull();
        typeof(ICapturesRecentSearch).GetProperty("Municipality").ShouldNotBeNull();
        typeof(ICapturesRecentSearch).GetProperty("Region").ShouldNotBeNull();
        typeof(ICapturesRecentSearch).GetProperty("Q").ShouldNotBeNull();
        typeof(ICapturesRecentSearch).GetProperty("SortBy").ShouldNotBeNull();
        // E2j (ADR 0060 amend 2026-06-12): commit-intent-markören.
        typeof(ICapturesRecentSearch).GetProperty("Commit").ShouldNotBeNull();
        // B2 (ADR 0067 Beslut 6/7): de två nya filter-dimensionerna.
        typeof(ICapturesRecentSearch).GetProperty("EmploymentType").ShouldNotBeNull();
        typeof(ICapturesRecentSearch).GetProperty("WorktimeExtent").ShouldNotBeNull();
    }
}
