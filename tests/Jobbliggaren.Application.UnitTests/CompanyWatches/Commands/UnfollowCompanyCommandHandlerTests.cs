using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Commands.UnfollowCompany;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class UnfollowCompanyCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public UnfollowCompanyCommandHandlerTests() => _currentUser.UserId.Returns(_userId);

    private UnfollowCompanyCommandHandler Handler(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _clock, _failedAccess);

    private async Task<CompanyWatch> SeedActiveAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, CancellationToken ct)
    {
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch;
    }

    [Fact]
    public async Task Handle_OnOwnedActiveWatch_SoftDeletes()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedActiveAsync(db, _userId, ct);

        var result = await Handler(db).Handle(new UnfollowCompanyCommand(watch.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = await db.CompanyWatches.IgnoreQueryFilters().SingleAsync(ct);
        stored.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_OnAlreadyUnfollowedOwnedWatch_IsIdempotentSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedActiveAsync(db, _userId, ct);
        await Handler(db).Handle(new UnfollowCompanyCommand(watch.Id.Value), ct);

        var second = await Handler(db).Handle(new UnfollowCompanyCommand(watch.Id.Value), ct);

        second.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_OnAnotherUsersWatch_ReturnsNotFoundAndLogsCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var otherUsersWatch = await SeedActiveAsync(db, Guid.NewGuid(), ct);

        var result = await Handler(db).Handle(new UnfollowCompanyCommand(otherUsersWatch.Id.Value), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.NotFound");
        _failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatch", otherUsersWatch.Id.Value, _userId, "UnfollowCompany");
        // The other user's watch is untouched (no cross-tenant soft-delete).
        (await db.CompanyWatches.IgnoreQueryFilters().SingleAsync(ct)).DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_OnUnknownId_ReturnsNotFoundWithoutCrossUserLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new UnfollowCompanyCommand(Guid.NewGuid()), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.NotFound");
        _failedAccess.DidNotReceiveWithAnyArgs().LogCrossUserAttempt(default!, default, default, default!);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new UnfollowCompanyCommandHandler(db, anon, _clock, _failedAccess)
            .Handle(new UnfollowCompanyCommand(Guid.NewGuid()), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.Unauthorized");
    }
}
