using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

public class ListCompanyWatchesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public ListCompanyWatchesQueryHandlerTests() => _currentUser.UserId.Returns(_userId);

    private ListCompanyWatchesQueryHandler Handler(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser);

    private void Add(Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, string orgNr)
        => db.CompanyWatches.Add(
            CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, _clock).Value);

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new ListCompanyWatchesQueryHandler(db, anon)
            .Handle(new ListCompanyWatchesQuery(), ct);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ForLegalEntityOrgNumber_SurfacesFullNumberUnflagged()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "5592804784"); // third digit 9 → legal entity
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        var dto = result.ShouldHaveSingleItem();
        dto.IsProtectedIdentity.ShouldBeFalse();
        dto.OrganizationNumber.ShouldBe("5592804784");
        dto.FollowedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Handle_ForSoleProprietorPersonnummerShapedOrgNumber_MasksAndFlags()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "9001011234"); // YYMMDD 900101 → personnummer-shaped (third digit 0)
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        var dto = result.ShouldHaveSingleItem();
        dto.IsProtectedIdentity.ShouldBeTrue();
        dto.OrganizationNumber.ShouldBeNull(); // raw personnummer-shaped value NEVER surfaced (D8(c))
    }

    [Fact]
    public async Task Handle_ExcludesSoftDeletedAndOtherUsersWatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var mineActive = CompanyWatch.Follow(_userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        var mineUnfollowed = CompanyWatch.Follow(_userId, OrganizationNumber.Create("2120000142").Value, _clock).Value;
        mineUnfollowed.SoftDelete(_clock);
        var otherUsers = CompanyWatch.Follow(Guid.NewGuid(), OrganizationNumber.Create("9696000003").Value, _clock).Value;
        db.CompanyWatches.AddRange(mineActive, mineUnfollowed, otherUsers);
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        result.ShouldHaveSingleItem().Id.ShouldBe(mineActive.Id.Value);
    }
}
