using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatchCriteria;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>#560 PR-3 — the "mina bevakningar" (criteria) list: owner-scoped, newest first,
/// raw codes + label (display-labels are FE-derived, Fork G6).</summary>
public class ListCompanyWatchCriteriaQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static ListCompanyWatchCriteriaQueryHandler HandlerFor(AppDbContext db, Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new ListCompanyWatchCriteriaQueryHandler(db, currentUser);
    }

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

        var result = await HandlerFor(db, Owner).Handle(new ListCompanyWatchCriteriaQuery(), ct);

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(mineNew.Id.Value);
        result[0].Label.ShouldBeNull();
        result[0].SniCodes.ShouldBe(["62201"]);
        result[0].MunicipalityCodes.ShouldBe(["1480"]);
        result[1].Id.ShouldBe(mineOld.Id.Value);
        result[1].Label.ShouldBe("Äldst");
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
    }
}
