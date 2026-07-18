using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowBrandGroup;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class FollowBrandGroupCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDbExceptionInspector _inspector = Substitute.For<IDbExceptionInspector>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();
    private const string Slug = "volvo-koncernen";

    // A catalogue containing exactly the "volvo-koncernen" group (one AB member — the handler only reads
    // existence; the members matter to the scan, not here).
    private static readonly IBrandGroupProvider Provider = StubProvider(Slug);

    public FollowBrandGroupCommandHandlerTests() => _currentUser.UserId.Returns(_userId);

    private FollowBrandGroupCommandHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, IBrandGroupProvider? provider = null) =>
        new(db, _currentUser, _clock, _inspector, provider ?? Provider);

    private static Stub StubProvider(params string[] slugs)
    {
        var dict = slugs.ToDictionary(
            s => s, s => new BrandGroup(s, s + " (koncern)", ["5560125790"]), StringComparer.Ordinal);
        return new Stub(new BrandGroupCatalog("test.v1", dict));
    }

    private sealed class Stub(BrandGroupCatalog catalog) : IBrandGroupProvider
    {
        public BrandGroupCatalog Catalog { get; } = catalog;
    }

    [Fact]
    public async Task Handle_WithCuratedSlug_CreatesActiveBrandGroupWatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new FollowBrandGroupCommand(Slug), ct);

        result.IsSuccess.ShouldBeTrue();
        var watch = await db.CompanyWatches.SingleAsync(ct);
        watch.UserId.ShouldBe(_userId);
        watch.TargetType.ShouldBe(CompanyWatchTargetType.BrandGroup);
        watch.BrandGroupId!.Value.ShouldBe(Slug);
        watch.OrganizationNumber.ShouldBeNull();
        watch.DeletedAt.ShouldBeNull();
        result.Value.ShouldBe(watch.Id.Value);
    }

    [Fact]
    public async Task Handle_WithUnknownButWellFormedSlug_ReturnsNotFound_AndStoresNoWatch()
    {
        // The vacuous-follow guard: a well-formed slug absent from the curated catalogue is a 404, never a
        // stored watch that would match nothing forever.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new FollowBrandGroupCommand("not-a-real-group"), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("BrandGroup.NotFound");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        (await db.CompanyWatches.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithMalformedSlug_ReturnsValidationError()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new FollowBrandGroupCommand("Volvo Koncern"), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("BrandGroupId.Invalid");
        (await db.CompanyWatches.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenAlreadyActivelyFollowed_IsIdempotentSingleRowSameId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        var first = await handler.Handle(new FollowBrandGroupCommand(Slug), ct);
        var second = await handler.Handle(new FollowBrandGroupCommand(Slug), ct);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);
        (await db.CompanyWatches.IgnoreQueryFilters().CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RefollowAfterUnfollow_ResurrectsSameRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var existing = CompanyWatch.FollowBrandGroup(_userId, BrandGroupId.Create(Slug).Value, _clock).Value;
        existing.SoftDelete(FakeDateTimeProvider.Default);
        db.CompanyWatches.Add(existing);
        await db.SaveChangesAsync(ct);

        var refollowClock = new FakeDateTimeProvider(_clock.UtcNow.AddDays(10));
        var result = await new FollowBrandGroupCommandHandler(db, _currentUser, refollowClock, _inspector, Provider)
            .Handle(new FollowBrandGroupCommand(Slug), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(existing.Id.Value);
        (await db.CompanyWatches.IgnoreQueryFilters().CountAsync(ct)).ShouldBe(1);
        var watch = await db.CompanyWatches.IgnoreQueryFilters().SingleAsync(ct);
        watch.DeletedAt.ShouldBeNull();
        watch.CreatedAt.ShouldBe(refollowClock.UtcNow);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new FollowBrandGroupCommandHandler(db, anon, _clock, _inspector, Provider)
            .Handle(new FollowBrandGroupCommand(Slug), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.Unauthorized");
    }
}
