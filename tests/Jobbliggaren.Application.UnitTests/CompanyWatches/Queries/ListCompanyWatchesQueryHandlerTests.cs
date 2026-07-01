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
        // #447 — no job_ads seeded → count is 0 (InMemory cannot populate the generated
        // organization_number column, so the count>0 branch is proven in the Testcontainers suite).
        dto.ActiveAdCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ForSoleProprietor_SurfacesActiveAdCount_EvenWhenOrgNumberMasked()
    {
        // #447 + D8(c) — the active-ad count is PUBLIC data, surfaced even when the org.nr is masked.
        // Here no ads are seeded (InMemory) so the count is 0; the point is that the field is present
        // and independent of the personnummer mask (the count>0 branch is a Testcontainers oracle).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "9001011234"); // personnummer-shaped
        await db.SaveChangesAsync(ct);

        var dto = (await Handler(db).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.IsProtectedIdentity.ShouldBeTrue();
        dto.OrganizationNumber.ShouldBeNull();
        dto.ActiveAdCount.ShouldBe(0);
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
