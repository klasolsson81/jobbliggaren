using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatchCriteria;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 PR-3 — the "mina bevakningar" (criteria) list: owner-scoped, newest first, raw codes + label
/// (display-labels are FE-derived, Fork G6).
///
/// <para>
/// <b>#1681 part 2 (ADR 0139) — each row now carries the detail page's two ad numbers</b>, resolved
/// through the REAL <see cref="CriterionMatchingAdSetResolver"/> with faked ports, so the guard
/// order, the refusal semantics and the memoisation are the ones production runs.
/// </para>
///
/// <para>
/// <b>The call COUNT on <c>FilterToMatchingAsync</c> is a bound, not a preference</b>
/// (senior-cto-advisor 2026-09-06, binding): "the list's fan-in is ONE batched
/// <c>FilterToMatchingAsync</c> call over the union, never 20 calls." A per-criterion fan-out returns
/// IDENTICAL numbers, so no outcome assertion anywhere could see the regression — the count is the
/// only oracle there is, and this route sits on the 300 ms <c>MeListRead</c> budget with the
/// dominant term (<c>GradeRankExpression</c>) unmeasured.
/// </para>
/// </summary>
public class ListCompanyWatchCriteriaQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch =
        Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly ICompanyWatchBrowseQuery _browse = Substitute.For<ICompanyWatchBrowseQuery>();

    // Non-empty Fast.SsykGroupConceptIds → assessable, so the grade filter IS consulted.
    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    private ListCompanyWatchCriteriaQueryHandler HandlerFor(AppDbContext db, Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new ListCompanyWatchCriteriaQueryHandler(
            db, currentUser,
            new CriterionMatchingAdSetResolver(_profileBuilder, _perUserSearch, _browse));
    }

    // Keyed on the criterion id so one list can carry several different answers; the fingerprint half
    // is left open here and pinned exactly by
    // CriterionMatchingAdSetResolverTests.MatchingBatchAsync_AsksThePortAboutEachCriterionsOwn-
    // CurrentPredicate.
    private void CountFor(Guid criterionId, MaterialisedAdCount answer) =>
        _browse.CountActiveAdsAsync(
                new CompanyWatchCriterionId(criterionId), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    private void IdSetFor(Guid criterionId, MaterialisedAdIds answer) =>
        _browse.ListActiveAdIdsAsync(
                new CompanyWatchCriterionId(criterionId), Arg.Any<CriteriaFingerprint>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    private void GradesTo(params JobAdId[] matching) =>
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>(matching));

    [Fact]
    public async Task Handle_ReturnsOnlyTheOwnRows_NewestFirst_WithCodesAndLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var older = new FakeDateTimeProvider(new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero));
        var newer = new FakeDateTimeProvider(new DateTimeOffset(2026, 7, 16, 8, 0, 0, TimeSpan.Zero));

        var spec = CompanyWatchCriteriaSpec.Create(["62100"], ["0180"]).Value;
        var mineOld = CompanyWatchCriterion.Create(Owner, spec, "Äldst", older).Value;
        var mineNew = CompanyWatchCriterion.Create(
            Owner, CompanyWatchCriteriaSpec.Create(["62201"], ["1480"]).Value, null, newer).Value;
        var theirs = CompanyWatchCriterion.Create(Stranger, spec, "Främmande", older).Value;
        db.CompanyWatchCriteria.AddRange(mineOld, mineNew, theirs);
        await db.SaveChangesAsync(ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        foreach (var id in new[] { mineOld.Id.Value, mineNew.Id.Value, theirs.Id.Value })
        {
            CountFor(id, MaterialisedAdCount.Counted(0, saturated: false));
            IdSetFor(id, MaterialisedAdIds.Resolved([]));
        }

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(mineNew.Id.Value);
        result[0].Label.ShouldBeNull();
        result[0].SniCodes.ShouldBe(["62201"]);
        result[0].MunicipalityCodes.ShouldBe(["1480"]);
        result[1].Id.ShouldBe(mineOld.Id.Value);
        result[1].Label.ShouldBe("Äldst");

        // The stranger's criterion is not merely filtered out of the RESPONSE — it is never resolved,
        // so no number is computed on behalf of a user who cannot see it.
        await _browse.DidNotReceive().CountActiveAdsAsync(
            new CompanyWatchCriterionId(theirs.Id.Value), Arg.Any<CriteriaFingerprint>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var clock = new FakeDateTimeProvider(new DateTimeOffset(2026, 7, 16, 8, 0, 0, TimeSpan.Zero));
        db.CompanyWatchCriteria.Add(CompanyWatchCriterion.Create(
            Owner, CompanyWatchCriteriaSpec.Create(["62100"], ["0180"]).Value, null, clock).Value);
        await db.SaveChangesAsync(ct);

        // Fail-closed: never a Guid.Empty scope an anonymous caller could share.
        (await HandlerFor(db, userId: null).Handle(new ListCompanyWatchCriteriaQuery(), ct))
            .ShouldBeEmpty();

        // ...and the fail-closed guard runs BEFORE anything is resolved, so an unauthenticated
        // request costs no port read and no grading call at all.
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoCriteria_AsksNothing()
    {
        // A fresh account is the common case. Building the match profile, or entering the batch at
        // all, for a user with no criteria is work nobody can consume.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        (await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct)).ShouldBeEmpty();

        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
        await _browse.DidNotReceiveWithAnyArgs()
            .CountActiveAdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_LandsEachCriterionsOwnNumbers_OnItsOwnRow()
    {
        // The numbers are per row, and a batch answers for the whole list at once — so mis-attributing
        // one criterion's matches to another is exactly the class of bug the composition introduces.
        // Three criteria with three DIFFERENT answers is what makes a swap visible; equal numbers
        // would hide it.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var third = await SeedCriterionAsync(db, Owner, ["62010"], "Tredje", days: 3, ct);
        var second = await SeedCriterionAsync(db, Owner, ["41200"], "Andra", days: 2, ct);
        var first = await SeedCriterionAsync(db, Owner, ["68201"], "Första", days: 1, ct);

        var a = new JobAdId(Guid.NewGuid());
        var b = new JobAdId(Guid.NewGuid());
        var c = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());

        CountFor(first.Id.Value, MaterialisedAdCount.Counted(2, saturated: false));
        IdSetFor(first.Id.Value, MaterialisedAdIds.Resolved([a, b]));

        CountFor(second.Id.Value, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(second.Id.Value, MaterialisedAdIds.Resolved([c]));

        CountFor(third.Id.Value, MaterialisedAdCount.Counted(0, saturated: false));
        IdSetFor(third.Id.Value, MaterialisedAdIds.Resolved([]));

        // b is graded OUT, so no row's personal count is simply its own ad magnitude.
        GradesTo(a, c);

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        // Newest first, and the row order is what carries the attribution.
        result.Select(r => r.Id).ShouldBe([first.Id.Value, second.Id.Value, third.Id.Value]);

        result[0].Ads.Magnitude.ShouldBe(2);
        result[0].Matching.Count.ShouldBe(1);

        result[1].Ads.Magnitude.ShouldBe(1);
        result[1].Matching.Count.ShouldBe(1);

        // A criterion whose companies have no active ads: zero on both, and it is a MEASUREMENT.
        result[2].Ads.Magnitude.ShouldBe(0);
        result[2].Matching.Count.ShouldBe(0);
        result[2].Ads.NotMaterialised.ShouldBeFalse();
        result[2].Ads.TooBroad.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_GradesTheWholeListInEXACTLYOneCall()
    {
        // THE bound. See the class docblock: identical numbers come back either way, so the call
        // count is the only thing that can go red when a batch decays into a fan-out.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var ads = new List<JobAdId>();
        var criteria = new List<CompanyWatchCriterion>();
        for (var i = 0; i < 5; i++)
        {
            var criterion = await SeedCriterionAsync(
                db, Owner, [$"6201{i}"], $"Bevakning {i}", days: i + 1, ct);
            var ad = new JobAdId(Guid.NewGuid());
            ads.Add(ad);
            criteria.Add(criterion);
            CountFor(criterion.Id.Value, MaterialisedAdCount.Counted(1, saturated: false));
            IdSetFor(criterion.Id.Value, MaterialisedAdIds.Resolved([ad]));
        }

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        GradesTo([.. ads]);

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        result.Count.ShouldBe(5);
        result.ShouldAllBe(r => r.Matching.Count == 1);

        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());

        // ...and the ONE call carried the whole union.
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Count == 5),
            Arg.Any<CancellationToken>());

        // The profile is built ONCE for the list too — it is the same profile for every row, and
        // rebuilding it per criterion would be the same fan-out one layer up.
        await _profileBuilder.Received(1).BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MeasuresEachCriterionsMagnitudeOnce_ThoughItIsReadTwice()
    {
        // The handler asks the resolver for the magnitude AFTER the batch has already measured it,
        // and its own comment says that is "free... so this cannot become a SECOND measurement of the
        // same fact". That claim rests on the resolver's per-request memo, and nothing else measured
        // it: an unshared memo would double every port read on this route while returning identical
        // numbers.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var criterion = await SeedCriterionAsync(db, Owner, ["62010"], "Enda", days: 1, ct);
        var ad = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(criterion.Id.Value, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(criterion.Id.Value, MaterialisedAdIds.Resolved([ad]));
        GradesTo(ad);

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        result.Single().Ads.Magnitude.ShouldBe(1);

        await _browse.Received(1).CountActiveAdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnassessableProfile_IsNotAssessedOnEveryRow_NeverAZero()
    {
        // A user who has stated no occupation gets a nudge on every row. A zero would tell them that
        // nothing matches them, which is exactly what an unassessable profile cannot establish — and
        // on a LIST that lie is repeated once per watch.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var one = await SeedCriterionAsync(db, Owner, ["62010"], "Ett", days: 1, ct);
        var two = await SeedCriterionAsync(db, Owner, ["41200"], "Två", days: 2, ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());
        CountFor(one.Id.Value, MaterialisedAdCount.Counted(7, saturated: false));
        CountFor(two.Id.Value, MaterialisedAdCount.Counted(3, saturated: false));

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(r => r.Matching.Count == null);
        result.ShouldAllBe(r => !r.Matching.TooBroad);
        result.ShouldAllBe(r => !r.Matching.NotMaterialised);

        // The AD magnitude is still answered — it does not depend on the user's profile, and
        // suppressing it would remove a true number because a different one is unavailable.
        result.Select(r => r.Ads.Magnitude).ShouldBe([7, 3]);

        // Nothing was graded and no set was read: assessability is the FIRST guard, list-wide.
        await _perUserSearch.DidNotReceiveWithAnyArgs().FilterToMatchingAsync(
            default!, default!, CancellationToken.None);
        await _browse.DidNotReceiveWithAnyArgs()
            .ListActiveAdIdsAsync(default, default, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ARefusingCriterion_ContributesNoIdsToTheGradingUnion_AndKeepsItsOwnState()
    {
        // Phase 1 resolves every criterion's set before anything is graded, so a refusal costs no
        // grading input. And the three no-number states stay APART on the wire: too broad, "inte
        // räknad än" and a real zero are three different sentences, and a list that collapsed them
        // would render one of them as another on every affected row.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var counted = await SeedCriterionAsync(db, Owner, ["62010"], "Räknad", days: 1, ct);
        var broad = await SeedCriterionAsync(db, Owner, ["41200"], "För bred", days: 2, ct);
        var fresh = await SeedCriterionAsync(db, Owner, ["68201"], "Ny", days: 3, ct);

        var ad = new JobAdId(Guid.NewGuid());

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        CountFor(counted.Id.Value, MaterialisedAdCount.Counted(1, saturated: false));
        IdSetFor(counted.Id.Value, MaterialisedAdIds.Resolved([ad]));
        CountFor(broad.Id.Value, MaterialisedAdCount.TooBroad);
        CountFor(fresh.Id.Value, MaterialisedAdCount.NotMaterialised);
        GradesTo(ad);

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        // The union is the answerable criterion's set and nothing else.
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Count == 1 && ids.Contains(ad)),
            Arg.Any<CancellationToken>());

        var countedRow = result.Single(r => r.Id == counted.Id.Value);
        countedRow.Ads.Magnitude.ShouldBe(1);
        countedRow.Matching.Count.ShouldBe(1);

        var broadRow = result.Single(r => r.Id == broad.Id.Value);
        broadRow.Ads.TooBroad.ShouldBeTrue();
        broadRow.Ads.Magnitude.ShouldBeNull();
        broadRow.Matching.TooBroad.ShouldBeTrue();
        broadRow.Matching.Count.ShouldBeNull();
        broadRow.Matching.NotMaterialised.ShouldBeFalse();

        var freshRow = result.Single(r => r.Id == fresh.Id.Value);
        freshRow.Ads.NotMaterialised.ShouldBeTrue();
        freshRow.Ads.TooBroad.ShouldBeFalse();
        freshRow.Ads.Magnitude.ShouldBeNull();
        freshRow.Matching.NotMaterialised.ShouldBeTrue();
        freshRow.Matching.TooBroad.ShouldBeFalse();
        freshRow.Matching.Count.ShouldBeNull();

        // Only the answerable criterion was probed for a set.
        await _browse.Received(1).ListActiveAdIdsAsync(
            Arg.Any<CompanyWatchCriterionId>(), Arg.Any<CriteriaFingerprint>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Seeds through the aggregate's own factory — the same call
    /// <c>CreateCompanyWatchCriterionCommandHandler</c> makes — so the rows this handler reads are
    /// ones production produces (CLAUDE.md §5 <c>Tests:</c>). <paramref name="days"/> spaces the
    /// <c>CreatedAt</c> stamps so "newest first" has something to order.
    /// </summary>
    private static async Task<CompanyWatchCriterion> SeedCriterionAsync(
        AppDbContext db, Guid userId, string[] sni, string? label, int days, CancellationToken ct)
    {
        var clock = new FakeDateTimeProvider(
            new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero).AddDays(-days));
        var spec = CompanyWatchCriteriaSpec.Create(sni, ["0180"]).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label, clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
