using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class FollowCompanyCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDbExceptionInspector _inspector = Substitute.For<IDbExceptionInspector>();
    private readonly IProtectedIdentityTokenizer _tokenizer = Substitute.For<IProtectedIdentityTokenizer>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();
    private const string OrgNr = "5592804784";

    public FollowCompanyCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Deterministic, distinct-from-plaintext test token (a 64-char length ⇒ IsPersonnummerShaped
        // true, mirroring a real HMAC). Only invoked for pnr-shaped input; OrgNr above is an AB number.
        _tokenizer.Tokenize(Arg.Any<string>())
            .Returns(ci => "hmac" + ci.Arg<string>().PadLeft(60, '0'));
    }

    private FollowCompanyCommandHandler Handler(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _clock, _inspector, _tokenizer);

    [Fact]
    public async Task Handle_WithValidOrgNumber_CreatesActiveWatchAndReturnsId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new FollowCompanyCommand(OrgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        var watch = await db.CompanyWatches.SingleAsync(ct);
        watch.UserId.ShouldBe(_userId);
        watch.OrganizationNumber!.Value.ShouldBe(OrgNr);
        watch.DeletedAt.ShouldBeNull();
        result.Value.ShouldBe(watch.Id.Value);
    }

    [Fact]
    public async Task Handle_WhenAlreadyActivelyFollowed_IsIdempotentSingleRowSameId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var handler = Handler(db);

        var first = await handler.Handle(new FollowCompanyCommand(OrgNr), ct);
        var second = await handler.Handle(new FollowCompanyCommand(OrgNr), ct);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value); // same id, no second row
        (await db.CompanyWatches.IgnoreQueryFilters().CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RefollowAfterUnfollow_ResurrectsSameRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        // Seed an already soft-deleted (unfollowed) watch for this user + org.nr.
        var existing = CompanyWatch.Follow(_userId, OrganizationNumber.Create(OrgNr).Value, _clock).Value;
        existing.SoftDelete(FakeDateTimeProvider.Default);
        db.CompanyWatches.Add(existing);
        await db.SaveChangesAsync(ct);

        var refollowClock = new FakeDateTimeProvider(_clock.UtcNow.AddDays(10));
        var result = await new FollowCompanyCommandHandler(db, _currentUser, refollowClock, _inspector, _tokenizer)
            .Handle(new FollowCompanyCommand(OrgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(existing.Id.Value); // SAME row resurrected, not a new one
        (await db.CompanyWatches.IgnoreQueryFilters().CountAsync(ct)).ShouldBe(1);
        var watch = await db.CompanyWatches.IgnoreQueryFilters().SingleAsync(ct);
        watch.DeletedAt.ShouldBeNull();
        watch.CreatedAt.ShouldBe(refollowClock.UtcNow);
    }

    [Fact]
    public async Task Handle_WithInvalidOrgNumber_ReturnsValidationError()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new FollowCompanyCommand("nope"), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OrganizationNumber.Invalid");
        (await db.CompanyWatches.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new FollowCompanyCommandHandler(db, anon, _clock, _inspector, _tokenizer)
            .Handle(new FollowCompanyCommand(OrgNr), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.Unauthorized");
    }
}
