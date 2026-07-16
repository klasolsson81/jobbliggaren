using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 PR-3 (Fork G3) — the saved criterion's magnitude: same IDOR posture as the browse
/// (null for unknown AND cross-user), the port receives the criterion's OWN predicate + the
/// single-sourced ceiling, and saturation flips exactly at the ceiling.
/// </summary>
public class GetCriterionMatchMagnitudeQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));

    private static readonly string[] SniIt = ["62100"];

    private static GetCriterionMatchMagnitudeQueryHandler HandlerFor(
        AppDbContext db, Guid? userId, ICompanyWatchBrowseQuery port,
        IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new GetCriterionMatchMagnitudeQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(), port);
    }

    [Fact]
    public async Task Handle_OwnCriterion_CountsThroughThePort_WithTheSingleSourcedCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountMatchingCompaniesAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(412);

        var result = await HandlerFor(db, Owner, port).Handle(
            new GetCriterionMatchMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Magnitude.ShouldBe(412);
        result.Saturated.ShouldBeFalse();

        await port.Received(1).CountMatchingCompaniesAsync(
            Arg.Is<CompanyWatchCriteriaSpec>(s => s.SniCodes.SequenceEqual(SniIt)),
            CriterionMatchMagnitudeDto.Ceiling,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AtTheCeiling_IsSaturated()
    {
        // Saturated means the copy MUST say "10 000+" — the exact number is no longer known.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountMatchingCompaniesAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CriterionMatchMagnitudeDto.Ceiling);

        var result = await HandlerFor(db, Owner, port).Handle(
            new GetCriterionMatchMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Saturated.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_OneBelowTheCeiling_IsExact()
    {
        // The boundary from the honest side: 9 999 is a REAL count and renders as-is.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountMatchingCompaniesAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CriterionMatchMagnitudeDto.Ceiling - 1);

        var result = await HandlerFor(db, Owner, port).Handle(
            new GetCriterionMatchMagnitudeQuery(criterion.Id.Value), ct);

        result.ShouldNotBeNull();
        result.Saturated.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_IsTheIdenticalNull_LogsTheProbe_AndNeverCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var theirs = await SeedAsync(db, Stranger, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var crossUser = await HandlerFor(db, Owner, port, failedAccess).Handle(
            new GetCriterionMatchMagnitudeQuery(theirs.Id.Value), ct);
        var unknown = await HandlerFor(db, Owner, port).Handle(
            new GetCriterionMatchMagnitudeQuery(Guid.NewGuid()), ct);

        crossUser.ShouldBeNull();
        unknown.ShouldBeNull();
        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, "GetCriterionMatchMagnitude");
        await port.DidNotReceiveWithAnyArgs()
            .CountMatchingCompaniesAsync(default!, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        (await HandlerFor(db, userId: null, port).Handle(
            new GetCriterionMatchMagnitudeQuery(criterion.Id.Value), ct)).ShouldBeNull();
    }

    private static async Task<CompanyWatchCriterion> SeedAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(["62100"], ["0180"]).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
