using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #1559 — <see cref="GetCriterionAdMagnitudeQueryHandler"/>. Unit-testable against InMemory for the
/// same reason its siblings are: the register is not on <c>IAppDbContext</c> (DPIA C-D4), so the
/// handler can only read the user's own criterion and the join answers through a faked port. The
/// port's real SQL is proven against Postgres in <c>CompanyWatchBrowseQueryPlanTests</c> and the
/// endpoint suite.
/// </summary>
public class GetCriterionAdMagnitudeQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero));

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    [Fact]
    public async Task Handle_OwnCriterion_CountsThroughThePort_WithTheProductCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(167);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Magnitude.ShouldBe(167);
        result.Saturated.ShouldBeFalse();

        // The ceiling is the AD question's own constant, passed from the single source — never
        // re-stated at the call site, and never the company magnitude's.
        await port.Received(1).CountActiveAdsAsync(
            Arg.Is<CompanyWatchCriteriaSpec>(s => s != null
                && s.SniCodes.SequenceEqual(SniIt)
                && s.MunicipalityCodes.SequenceEqual(KommunStockholm)),
            CriterionAdMagnitudeDto.Ceiling,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CountAtTheCeiling_IsReportedSaturated()
    {
        // The saturation arm is what makes the copy say "10 000+" instead of a bare number the join
        // cannot stand behind (#859). It is REACHABLE, not decorative: measured 2026-09-04 against the
        // dev register, the broadest bound-legal criterion matched 39 909 active ads.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CriterionAdMagnitudeDto.Ceiling);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Saturated.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_JustBelowTheCeiling_IsNotSaturated()
    {
        // The boundary in the other direction — without this the `>=` could become `>` (or the whole
        // comparison be inverted) with the saturated-arm test still green.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountActiveAdsAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CriterionAdMagnitudeDto.Ceiling - 1);

        var result = await HandlerFor(db, Owner, port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Saturated.ShouldBeFalse();
        result.Magnitude.ShouldBe(CriterionAdMagnitudeDto.Ceiling - 1);
    }

    [Fact]
    public async Task Handle_UnknownCriterion_ReturnsNotFound_AndLogsNoCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var result = await HandlerFor(db, Owner, Substitute.For<ICompanyWatchBrowseQuery>(), failedAccess)
            .Handle(new GetCriterionAdMagnitudeQuery(Guid.NewGuid()), ct);

        result.ShouldBeNull();
        failedAccess.DidNotReceiveWithAnyArgs().LogCrossUserAttempt(default!, default, default, default!);
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_ReturnsTheIDENTICAL_NotFound_AndLogsTheCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var theirs = await SeedCriterionAsync(db, Stranger, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var crossUser = await HandlerFor(db, Owner, port, failedAccess)
            .Handle(new GetCriterionAdMagnitudeQuery(theirs.Id.Value), ct);

        // Detected (ADR 0031), and the operation name is THIS surface's — a shared constant would
        // erase which surface was probed.
        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, nameof(GetCriterionAdMagnitudeQuery));

        // The register is never joined on behalf of a stranger.
        await port.DidNotReceiveWithAnyArgs().CountActiveAdsAsync(default!, default, CancellationToken.None);

        // Indistinguishable from the unknown-id answer: both literally null, so they cannot drift into
        // two shapes an attacker could tell apart (IDOR).
        var unknownId = await HandlerFor(db, Owner, port, Substitute.For<IFailedAccessLogger>())
            .Handle(new GetCriterionAdMagnitudeQuery(Guid.NewGuid()), ct);

        crossUser.ShouldBeNull();
        unknownId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsNotFound_WithoutTouchingTheRegister()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var result = await new GetCriterionAdMagnitudeQueryHandler(
                db, currentUser, Substitute.For<IFailedAccessLogger>(), port)
            .Handle(new GetCriterionAdMagnitudeQuery(criterion.Id.Value), ct);

        // Fail-closed: no Guid.Empty fallback an unauthenticated caller could share a scope with.
        result.ShouldBeNull();
        await port.DidNotReceiveWithAnyArgs().CountActiveAdsAsync(default!, default, CancellationToken.None);
    }

    private static GetCriterionAdMagnitudeQueryHandler HandlerFor(
        AppDbContext db,
        Guid userId,
        ICompanyWatchBrowseQuery port,
        IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetCriterionAdMagnitudeQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(), port);
    }

    private static async Task<CompanyWatchCriterion> SeedCriterionAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(SniIt, KommunStockholm).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
