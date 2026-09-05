using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Behaviors;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Behaviors;

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
        // #311 PR-2b C1 (ADR 0087 D6) — Employer i ICapturesRecentSearch-shapen.
        IReadOnlyList<string>? Employer = null,
        // #551 PR-D — Remote (distans, bool) i ICapturesRecentSearch-shapen. Default false så de
        // befintliga (remote-agnostiska) capture-testerna består; remote-guarden testas explicit nedan.
        bool Remote = false,
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
        // #831 — the REAL parser, not a substitute: it is pure CPU and deterministic, and the
        // behaviour under test is precisely "capture what the parser decided the search was".
        // A fake here would let the test agree with itself instead of with production.
        new(_currentUser, _capturer, new SearchQueryParser(),
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

    // ---- A2: the ?employer= axis stops persisting a personnummer (Klas-beslut 2026-08-19) ----

    // Third digit < '2' is the house discriminator: a legal entity's org.nr always has >= 2 there,
    // a personnummer has 0 or 1. These are the two forms a hand-typed URL can carry, since the
    // format gate accepts any ten digits and has no discriminator of its own.
    [Theory]
    [InlineData("1010101010")]   // pnr-shaped, third digit 1
    [InlineData("0001010101")]   // pnr-shaped, third digit 0
    public async Task Handle_PersonnummerShapedEmployer_RunsTheSearchButCapturesNothing(string employer)
    {
        // Not a hypothetical premise: `?employer=` is a FORMAT gate, so a personnummer-shaped value
        // in it comes from a hand-typed URL or an old bookmark - exactly what this argument is.
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Employer: [employer]));

        // The SEARCH ran (HandleAsync returns the handler's response); only the persistence is
        // skipped. Refusing the search would break a legitimate filter on a sole trader's ads.
        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersonnummerShapedEmployerBesideALegalEntity_CapturesNothingAtAll()
    {
        // The gate is `.Any`, not `.All`: one personnummer-shaped value in a multi-employer list
        // refuses the whole capture. This is the pin ListRecentSearchesCountReplayParityTests'
        // mixed case names as the reason its row can only be pre-A2 (#1471).
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Employer: ["5566010101", "1010101010"]));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersonnummerShapedEmployerBesideOtherFilters_CapturesNothingAtAll()
    {
        // Skip, never filter. A row with the employer stripped out would no longer reproduce the
        // search it claims to be, which is the whole point of Senaste sokningar - and it would put
        // a misleading row in front of the user instead of no row.
        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: ["1234"], Municipality: null, Region: null,
            Employer: ["1010101010"]));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("55660101")]        // too short
    [InlineData("55660101011")]     // too long
    [InlineData("556601010a")]      // not all digits
    public async Task Handle_UnparseableEmployer_CapturesNothing(string employer)
    {
        // DECLARED UNREACHABLE (CLAUDE.md section 5, Tests:), so this asserts only that the
        // persistence path degrades safely - never what production does with such a value.
        //
        // The gate that actually stops them is ListJobAdsQueryValidator's
        // RuleForEach(q => q.Employer).Matches(OrganizationNumberPattern), which runs in
        // ValidationBehavior BEFORE this behaviour and 400s all four. `parseEmployerParam` is a
        // second, narrower gate on the FE and is NOT what makes them unreachable - naming it alone
        // would put the declaration on the wrong gate (code-reviewer, PR #1411).
        //
        // Kept as defence in depth: fail-safe in the wide direction, matching
        // OrganizationNumber.IsPersonnummerShaped's own posture, so the guard does not depend on a
        // gate one layer up staying exactly as narrow as it is now.
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Employer: [employer]));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OneShapedEmployerAmongMany_CapturesNothing()
    {
        // DECLARED UNREACHABLE, same as the four unparseable forms above and for the same reason
        // it must be said out loud: no live path delivers arity > 1. `parseEmployerParam` takes
        // `raw[0]`, and `lib/api/job-ads.ts` appends `employer` once from a `string | undefined`;
        // under Option B the API is never edge-exposed, so nothing else can supply a second
        // element. So this asserts only that the persistence path degrades safely, never what
        // production does (security-auditor, PR #1411).
        //
        // Kept because the wire type IS a list: `ListJobAdsQuery.Employer` binds `string[]`, so
        // `Any` and not `All` is the correct predicate the day a second element becomes possible.
        // One personnummer in the list is one personnummer persisted.
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Employer: ["5566010101", "1010101010"]));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- The ?q= axis stops persisting a personnummer (Klas-beslut 2026-08-19) ----
    //
    // Same class as the employer block above, different detector. `q` has no FORMAT gate -
    // ListJobAdsQueryValidator bounds its length and requires it non-empty when sorting by
    // relevance, and nothing else - so it carries hyphenated, 12-digit and space-gapped forms
    // the ten-digit employer axis structurally cannot. Detection is therefore the validating
    // flag chain (Normalize -> Scan -> Personnummer.TryParse date+Luhn), not a shape predicate.
    //
    // Every vector below is production-producible: the /jobb free-text box feeds ?q= end to
    // end, so none of these needs the declared-unreachable framing the employer block uses.
    // Vectors are ASCII by design - the fullwidth/Unicode-dash folding is pinned in the
    // Domain privacy suites, and these tests call the REAL scanner, so repeating that
    // coverage here would duplicate it rather than add any.

    [Theory]
    [InlineData("811218-9876")]      // 10-digit, hyphenated
    [InlineData("8112189876")]       // 10-digit, contiguous
    [InlineData("19811218-9876")]    // 12-digit, hyphenated
    [InlineData("198112189876")]     // 12-digit, contiguous
    [InlineData("811278-9873")]      // samordningsnummer (day 78 = 18 + 60)
    [InlineData("811218 9876")]      // single-space gapped - bridged by the normalizer
    public async Task Handle_PersonnummerShapedQ_RunsTheSearchButCapturesNothing(string q)
    {
        await HandleAsync(new FakeSearchQuery(
            Q: q, OccupationGroup: null, Municipality: null, Region: null));

        // The SEARCH ran - only the persistence is skipped. Refusing the search would be a
        // behaviour change on a surface that has nothing to do with the leak. Asserting on the
        // returned TotalCount would not pin that: Handle awaits next(...) before every guard, so
        // the fake's response comes back on every path and the assertion could never fail.
        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QWithPersonnummerInsideFreeText_CapturesNothing()
    {
        // The carrier form the employer axis cannot have: a personnummer as one token among
        // ordinary search words. The scanner's candidate regex is delimiter-aware, so it finds
        // the token without the whole string having to look like a number.
        await HandleAsync(new FakeSearchQuery(
            Q: "backend 811218-9876 stockholm", OccupationGroup: null, Municipality: null,
            Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- #1415 / ADR 0134: the residual class this axis used to inherit ----
    //
    // Until #1415 this guard ran the same bridging policy as CV import, so a personnummer
    // typed with a wider gap persisted in plaintext AND re-rendered verbatim as the
    // /sokningar row label (DeriveLabel). The fix is not a wider policy everywhere - it is
    // the RIGHT policy here: ?q= is a single-line hand-typed value, so it runs
    // PersonnummerGapProfile.SingleLineUserInput, while extracted document text keeps the
    // narrow bridge because a line break there is a field boundary whose accidental joining
    // collides far more often (PersonnummerBridgeCollisionRateTests measures both rates).
    //
    // These vectors reach ?q= end to end: percent-encoding carries every one of them through
    // the wire, ListJobAdsQueryValidator constrains q only by MaximumLength and a NotEmpty that
    // applies when sorting by relevance (neither reached here - every vector is non-empty and
    // none sorts by relevance), and SearchCriteria's NormalizeString only trims - so each of
    // these persisted verbatim before this change.
    [Theory]
    [InlineData("811218   9876")]        // three spaces
    [InlineData("811218    9876")]       // four spaces
    [InlineData("811218     9876")]      // five spaces
    [InlineData("811218\t\t\t9876")]     // three tabs
    [InlineData("811218\n9876")]         // U+000A - the form measured reachable on #1414
    [InlineData("811218\r\n9876")]       // CRLF
    [InlineData("811218\r9876")]         // U+000D
    [InlineData("811218\u00019876")]     // U+0001 Cc control
    [InlineData("811218 \u0001 9876")]   // space, Cc, space
    [InlineData("811218\u20289876")]     // U+2028 LINE SEPARATOR (\p{Zl} - in neither Zs nor Cc)
    [InlineData("811218\u000B9876")]     // U+000B LINE TABULATION
    [InlineData("811218\u000C9876")]     // U+000C FORM FEED
    public async Task Handle_WidelyGappedPersonnummerQ_CapturesNothing(string q)
    {
        await HandleAsync(new FakeSearchQuery(
            Q: q, OccupationGroup: null, Municipality: null, Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WidelyGappedPersonnummerQBesideOtherFilters_CapturesNothingAtAll()
    {
        // The skip-versus-null-out distinction from the block below, restated for the widened
        // class: on a q-only query Create's Empty invariant hides a null-out, so only a query
        // carrying a dimension beside the q can tell the two apart. Without this row the whole
        // widened theory above would stay green against a guard that nulled q instead of
        // skipping - and that guard would Bump() a different, real search.
        await HandleAsync(new FakeSearchQuery(
            Q: "811218\n9876", OccupationGroup: ["grp1"], Municipality: null, Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("referens 12345678   0000")]  // bridges, then fails the month gate
    [InlineData("referens 12345678\n0000")]   // same, across a line break
    [InlineData("811218 - 9876")]             // separator mid-gap: the grammar admits none
    public async Task Handle_WidelyGappedDigitsThatAreNoPersonnummer_StillCaptures(string q)
    {
        // The widened profile must not become a digit filter. The date+Luhn authority is
        // UNCHANGED, so ordinary searches carrying wide digit gaps still capture. The third
        // row is the deliberate residual ADR 0134 keeps and names: it is unbridged because a
        // separator is admitted only digit-adjacent, at any bound - not because of the width.
        await HandleAsync(new FakeSearchQuery(
            Q: q, OccupationGroup: null, Municipality: null, Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- #1419: the five taxonomy axes stop persisting a personnummer ----
    //
    // Klas bound #1411 parity on 2026-08-20. These axes are validated on SHAPE ONLY, so a
    // hand-edited /jobb?occupationGroup=8112189876&commit=true reached the sink past every
    // other guard, and the read side renders an unresolved id back verbatim in the row label.
    //
    // Every dimension gets its own row rather than one representative: the guard is five
    // separate list reads, and a single missed list is exactly the defect that would survive a
    // one-vector test.

    [Theory]
    [InlineData("occupationGroup")]
    [InlineData("municipality")]
    [InlineData("region")]
    [InlineData("employmentType")]
    [InlineData("worktimeExtent")]
    public async Task Handle_PersonnummerInAnyTaxonomyAxis_RunsTheSearchButCapturesNothing(string axis)
    {
        const string pnr = "8112189876";
        await HandleAsync(new FakeSearchQuery(
            Q: null,
            OccupationGroup: axis == "occupationGroup" ? [pnr] : null,
            Municipality: axis == "municipality" ? [pnr] : null,
            Region: axis == "region" ? [pnr] : null,
            EmploymentType: axis == "employmentType" ? [pnr] : null,
            WorktimeExtent: axis == "worktimeExtent" ? [pnr] : null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HyphenatedPersonnummerInTaxonomyAxis_CapturesNothing()
    {
        // The conceptId grammar admits '-', so the hyphenated form reaches this axis too. It is
        // the shape a human actually types, and the contiguous-only scanner would miss it
        // without the shared chain's separator handling.
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: ["811218-9876"], Municipality: null, Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersonnummerAmongValidConceptIdsInOneAxis_CapturesNothing()
    {
        // Any element, not just the first: the guard reads the whole list.
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: ["grp1", "grp2", "8112189876"], Municipality: null, Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersonnummerInTaxonomyAxisBesideOtherFilters_CapturesNothingAtAll()
    {
        // Skip, never filter the value out. On a single-axis query Create's Empty invariant
        // would hide a filter-out; with a second dimension beside it the criteria stays valid
        // and a filter-out WOULD capture - as a row whose FilterHash equals a genuine search on
        // the remaining dimension, which CaptureAsync would then find and Bump().
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: ["8112189876"], Municipality: ["sthlm"], Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("8112189875")]  // personnummer shape, Luhn check digit wrong
    [InlineData("5592804784")]  // a real org.nr - Luhn-VALID, rejected on the date gate
    [InlineData("2512")]        // four digits, no candidate shape
    public async Task Handle_DigitBearingConceptIdThatIsNoPersonnummer_StillCaptures(string conceptId)
    {
        // The counterfactual: a VALIDATING detector, not a digit filter. The reason is
        // single-sourcing (#844) rather than false positives — measured 2026-08-20 by
        // security-auditor on the corpus shipped at that commit: no bare digit string at all, at
        // most seven digits in any id, so a shape-only rule would have cost nothing there. What this pins is the DECLARED
        // consequence of using the house chain: a Luhn-invalid ten-digit value is skipped on the
        // employer axis and captured here, because that axis runs a deliberately over-inclusive
        // shape predicate and this one runs the date+Luhn authority.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(_userId, Arg.Do<SearchCriteria>(c => captured = c), 7, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: [conceptId], Municipality: null, Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured.OccupationGroup.ShouldContain(conceptId);
    }

    [Fact]
    public async Task Handle_PersonnummerShapedQBesideOtherFilters_CapturesNothingAtAll()
    {
        // Skip, never null q out, and this is the ONLY test that can tell the two apart: on a
        // q-only query Create's own Empty invariant would drop the capture anyway, so a
        // null-out would look identical there. With a dimension beside it the criteria stays
        // valid and a null-out WOULD capture - as a row whose FilterHash equals that of a
        // genuine q-less search on the same dimension, which CaptureAsync would then find and
        // Bump(), corrupting LastSeenCount and LastViewedAt on a different, real search.
        await HandleAsync(new FakeSearchQuery(
            Q: "811218-9876", OccupationGroup: ["grp1"], Municipality: null, Region: null));

        await _capturer.DidNotReceive().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("811218-9875")]        // personnummer shape, Luhn check digit wrong
    [InlineData("5592804784")]         // a real org.nr - Luhn-VALID, rejected on the date gate
    [InlineData("javautvecklare 2026")] // digits, no candidate shape at all
    public async Task Handle_DigitBearingQThatIsNoPersonnummer_CapturesRawQ(string q)
    {
        // The counterfactual that keeps the guard honest: it is a VALIDATING detector, not a
        // digit filter. Ordinary searches that merely contain digits - a company number, a
        // year, a near-miss - still capture, and the stored q is byte-identical to the input.
        //
        // The org.nr vector is deliberately the Luhn-VALID fixture from
        // ListJobAdsQueryValidator's own doc (5592804784, Luhn sum 50). A Luhn-invalid org.nr
        // would pass this test for the wrong reason - the check digit, not the discriminator
        // under test. What actually rejects it is the date gate reached first: significant[2..4]
        // is "92", and a Swedish org.nr always carries >= 20 in the month position.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: q, OccupationGroup: null, Municipality: null, Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured!.Q.ShouldBe(q);
    }

    [Fact]
    public async Task Handle_EmployerOnly_CapturesSearch_WithOrgNr()
    {
        // #311 PR-2b C1 (ADR 0087 D6): default-browse-guarden räknar nu employer → en committad
        // sökning med ENBART ?employer= släpps igenom OCH org.nr:t trådas in i den fångade criterian
        // (PR-2:s live silent-drop stängd).
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Employer: ["5566010101"]));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured!.Employer.ShouldBe(["5566010101"]);
    }

    [Fact]
    public async Task Handle_RemoteOnly_CapturesSearch()
    {
        // #551 PR-D: the default-browse guard now counts Remote → a committed search with ONLY
        // ?remote=true (all lists empty, Q null) is a genuine filter intention and IS captured, with
        // Remote threaded into the captured criteria. If the guard forgot the !capt.Remote clause a
        // remote-only search would be silently dropped as default-browse (the lockstep-with-Create bind).
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Remote: true));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured!.Remote.ShouldBeTrue();
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

    [Fact]
    public async Task Handle_RemoteFalseAndAllDimensionsEmpty_DoesNotCapture()
    {
        // #551 PR-D counterfactual to Handle_RemoteOnly_CapturesSearch: remote=false with everything
        // else empty is default-browse (bool-semantik: false = inget filter) → never captured. If the
        // guard mis-counted remote=false as a filter this would over-capture (data-minimering
        // Art. 5(1)(c)); SearchCriteria.Create would also reject it (the lockstep tom-invariant).
        await HandleAsync(new FakeSearchQuery(
            Q: null, OccupationGroup: null, Municipality: null, Region: null,
            Remote: false));

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
    public async Task Handle_SubMinimumQAndNoDimensions_DoesNotCaptureButReturnsResponse()
    {
        // Var Handle_InvalidCriteria_DoesNotCaptureButReturnsResponse. Utfallet är detsamma
        // (ingen capture) men #831 bytte VARFÖR, och den gamla kommentaren var därmed falsk:
        // förr släppte default-browse-guarden igenom "a" och SearchCriteria.Create failade på
        // InvalidQ. Nu nollar parsern q före guarden, så det här är ett rent default-browse
        // (ingen söktext, inga dimensioner) och guarden returnerar — Create anropas aldrig.
        // Ett default-browse ska inte capture:as, så beteendet är rätt av rätt skäl.
        var result = await HandleAsync(new FakeSearchQuery(
            Q: "a", OccupationGroup: null, Municipality: null, Region: null));

        result.TotalCount.ShouldBe(7);
        await _capturer.DidNotReceiveWithAnyArgs().CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<SearchCriteria>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SubMinimumQWithDimension_CapturesTheSearchThatActuallyRan()
    {
        // #831 REGRESSIONSTEST — den tysta förlusten som kontrakts-relaxeringen införde.
        //
        // Före #831 400:ade `?q=a&occupationGroup=grp1&commit=1` i ValidationBehavior, så
        // capture var aldrig aktuell. Efter borttaget minimum returnerar samma URL 200 med
        // yrkesgrupp-filtret applicerat — men med RÅ q vidare till Create föll hela capturen
        // på Create:s min-invariant, och användaren tappade sökningen ur "Senaste sökningar"
        // över ett tecken systemet självt ignorerar. Dimensionen försvann med den.
        //
        // Kravet är alltså inte bara "capture sker" utan "capture speglar det som KÖRDES":
        // q = null (parsern nollade det), dimensionen bevarad. Asserterar båda — ett test som
        // bara räknade anrop hade passerat även om q lagrats som "a".
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: "a", OccupationGroup: ["grp1"], Municipality: null, Region: null));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured.Q.ShouldBeNull();
        captured.OccupationGroup.ShouldBe(["grp1"]);
    }

    [Fact]
    public async Task Handle_SubMinimumQWithRelevanceSort_CapturesTheSortThatActuallyRan()
    {
        // #831 rond 2 — SearchCriteria.Create har TRE q-beroende invarianter, inte två.
        // Utöver Empty-guarden och min-längden finns RelevanceRequiresQ: Relevance med
        // null-q avvisas. `?q=a&sortBy=Relevance&occupationGroup=grp1&commit=true` gav
        // därför exakt samma tysta capture-förlust som fixen ovan skulle stänga — 200 med
        // dimensionen applicerad, sedan borta ur "Senaste sökningar" ett tecken senare.
        //
        // Samma princip igen: capture:a det som KÖRDES. Med nollad residual faller
        // ApplyRelevanceSort tillbaka på PublishedAt desc, så det är vad användaren fick.
        // Asserterar sorten OCH att dimensionen överlever — en anropsräkning hade passerat
        // även om Relevance lagrats och nästa reconcile ljugit om vad sökningen var.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: "a", OccupationGroup: ["grp1"], Municipality: null, Region: null,
            SortBy: JobAdSortBy.Relevance));

        await _capturer.Received(1).CaptureAsync(
            _userId, Arg.Any<SearchCriteria>(), 7, Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured.Q.ShouldBeNull();
        captured.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        captured.OccupationGroup.ShouldBe(["grp1"]);
    }

    [Fact]
    public async Task Handle_RelevanceSortWithKeptQ_CapturesRelevanceUnchanged()
    {
        // Motprov till testet ovan: nedgraderingen får bara träffa den nollade residualen.
        // Utan det här testet hade `effectiveSortBy` kunnat nedgradera ALLA relevans-
        // sökningar och den enda assertionen som fanns hade fortfarande varit grön.
        SearchCriteria? captured = null;
        _capturer.CaptureAsync(
                _userId, Arg.Do<SearchCriteria>(c => captured = c), 7,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await HandleAsync(new FakeSearchQuery(
            Q: "backend", OccupationGroup: null, Municipality: null, Region: null,
            SortBy: JobAdSortBy.Relevance));

        captured.ShouldNotBeNull();
        captured.Q.ShouldBe("backend");
        captured.SortBy.ShouldBe(JobAdSortBy.Relevance);
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

    // #1419: EXHAUSTIVE, because the block above is inclusion-only and inclusion cannot see
    // GROWTH. This interface has grown twice — Employer (#311 PR-2b) and Remote (#551 PR-D) —
    // and both times a dimension reached the sink while a guard did not follow. That is the
    // defect class #1419 itself closes, so leaving the shape gate unable to detect the next one
    // would be closing an instance and leaving the mechanism.
    //
    // Every string-bearing member must be read by a personnummer guard before capture: Q and
    // Employer by their own, the five taxonomy axes by BearsPersonnummer. Remote, SortBy and
    // Commit are bool/enum and carry no text.
    [Fact]
    public void ICapturesRecentSearch_HasExactlyTheseMembers_SoANewDimensionCannotArriveUnguarded()
    {
        // GetProperties() on an interface does NOT walk base interfaces (measured with a probe:
        // an IDerived : IBase reports only IDerived's own). ICapturesRecentSearch has no base
        // today, so without this the hole would be closed by absence rather than by construction
        // — and "inclusion cannot see growth" is the exact failure this gate exists to close, so
        // leaving it one level up would be the same defect wearing a hat.
        var actual = typeof(ICapturesRecentSearch).GetProperties()
            .Concat(typeof(ICapturesRecentSearch).GetInterfaces().SelectMany(i => i.GetProperties()))
            .Select(p => p.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            ["Commit", "Employer", "EmploymentType", "Municipality", "OccupationGroup", "Q",
             "Region", "Remote", "SortBy", "WorktimeExtent"],
            "a member arrived on or left ICapturesRecentSearch. If it is a NEW string-bearing " +
            "dimension it reaches recent_job_searches in plaintext, and it must be added to the " +
            "BearsPersonnummer chain in RecentJobSearchCaptureBehavior AND to the default-browse " +
            "guard — neither of which fails on its own when a dimension is merely missing. " +
            "Update this list in the same commit as the guard, never before it.");
    }
}
