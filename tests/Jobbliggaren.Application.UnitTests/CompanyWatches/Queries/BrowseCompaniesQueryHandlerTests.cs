using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 kriterie-vågen PR-2 — <see cref="BrowseCompaniesQueryHandler"/>. Unit-testable against
/// InMemory precisely BECAUSE the register is not on <c>IAppDbContext</c> (DPIA C-D4): the handler can
/// only read the user's own criterion, and the register answers through a port that is faked here. The
/// port's real SQL semantics are proven against real Postgres in
/// <c>CompanyWatchBrowseQueryTests</c> / <c>CompanyWatchBrowseQueryPlanTests</c>.
/// </summary>
public class BrowseCompaniesQueryHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));

    private static readonly string[] SniIt = ["62010"];
    private static readonly string[] KommunStockholm = ["0180"];

    [Fact]
    public async Task Handle_OwnCriterion_RunsItThroughThePort_AndMapsTheHits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, SniIt, KommunStockholm, ct);

        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.BrowseAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CompanyBrowseResult>(
                [new CompanyBrowseResult("5560000012", "Acme AB", "0180", "Stockholm", ["62010"])],
                totalCount: 1, page: 1, pageSize: 20));

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCompaniesQuery(criterion.Id.Value, Page: 1, PageSize: 20), ct);

        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(1);
        var hit = result.Items.Single();
        hit.OrganizationNumber.ShouldBe("5560000012");
        hit.IsProtectedIdentity.ShouldBeFalse();
        hit.Name.ShouldBe("Acme AB");

        // The port receives the criterion's OWN predicate — not something the request could influence.
        await port.Received(1).BrowseAsync(
            Arg.Is<CompanyBrowseCriteria>(c =>
                c.Criteria.SniCodes.SequenceEqual(SniIt)
                && c.Criteria.MunicipalityCodes.SequenceEqual(KommunStockholm)
                && c.Page == 1
                && c.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownCriterion_ReturnsNotFound_AndLogsNoCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var result = await HandlerFor(db, Owner, Substitute.For<ICompanyWatchBrowseQuery>(), failedAccess)
            .Handle(new BrowseCompaniesQuery(Guid.NewGuid(), 1, 20), ct);

        // null = not-found; the endpoint (PR-3) maps it to 404.
        result.ShouldBeNull();

        // No criterion with that id exists at all → nobody probed anybody's data.
        failedAccess.DidNotReceiveWithAnyArgs().LogCrossUserAttempt(default!, default, default, default!);
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_ReturnsTheIDENTICAL_NotFound_AndLogsTheCrossUserAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var theirs = await SeedCriterionAsync(db, Stranger, SniIt, KommunStockholm, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var crossUser = await HandlerFor(db, Owner, port, failedAccess)
            .Handle(new BrowseCompaniesQuery(theirs.Id.Value, 1, 20), ct);

        // The attempt IS detected (ADR 0031)...
        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, "BrowseCompanies");

        // ...and the register is never touched on behalf of a stranger.
        await port.DidNotReceiveWithAnyArgs().BrowseAsync(default!, CancellationToken.None);

        // THE assertion: the cross-user response is INDISTINGUISHABLE from the unknown-id response, so
        // it cannot be used as an existence oracle for another user's criterion ids (IDOR — the
        // endpoint maps both to 404, never 403). Both are literally `null`, so unlike a constructed
        // error they cannot drift apart into two distinguishable shapes.
        var unknownId = await HandlerFor(db, Owner, port, Substitute.For<IFailedAccessLogger>())
            .Handle(new BrowseCompaniesQuery(Guid.NewGuid(), 1, 20), ct);

        crossUser.ShouldBeNull();
        unknownId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsNotFound_WithoutTouchingTheRegister()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, SniIt, KommunStockholm, ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var result = await new BrowseCompaniesQueryHandler(
                db, currentUser, Substitute.For<IFailedAccessLogger>(), port)
            .Handle(new BrowseCompaniesQuery(criterion.Id.Value, 1, 20), ct);

        // Fail-closed: no Guid.Empty fallback that an unauthenticated caller could share a scope with.
        result.ShouldBeNull();
        await port.DidNotReceiveWithAnyArgs().BrowseAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_PersonnummerShapedOrgNr_IsMaskedAndFlagged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, SniIt, KommunStockholm, ct);

        // ADR 0091 keeps sole traders out of company_register at ingest, so this row should be
        // unreachable — which is exactly why the masking must not depend on that staying true.
        //
        // "9001011234" is a TEN-digit sole-trader org.nr (= a personnummer). The length matters:
        // company_register.organization_number is varchar(10), so a 12-digit value can NEVER come out of
        // that column. Testing one would go green through IsPersonnummerShaped's fail-safe branch
        // (Length != 10 -> true) and leave the branch this data path CAN actually reach — the
        // Value[2] < '2' heuristic — completely untested (code-reviewer Major, 2026-07-13). This value
        // exercises the real heuristic: Value[2] = '0' < '2'.
        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.BrowseAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CompanyBrowseResult>(
                [new CompanyBrowseResult("9001011234", "Enskild Firma", "0180", "Stockholm", ["62010"])],
                totalCount: 1, page: 1, pageSize: 20));

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCompaniesQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        var hit = result.Items.Single();
        hit.OrganizationNumber.ShouldBeNull();
        hit.IsProtectedIdentity.ShouldBeTrue();
        // The rest of the row is public data and still renders — masking hides the identity, not the hit.
        hit.Name.ShouldBe("Enskild Firma");
    }

    [Fact]
    public async Task Handle_MalformedOrgNr_IsMaskedByTheFailSafeBranch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedCriterionAsync(db, Owner, SniIt, KommunStockholm, ct);

        // The OTHER branch: IsPersonnummerShaped fail-safes to TRUE on anything that is not exactly ten
        // ASCII digits ("the unexpected is sensitive"). A varchar(10) column cannot produce this, but a
        // future non-register caller of the port could — and it must mask, not leak.
        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.BrowseAsync(Arg.Any<CompanyBrowseCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CompanyBrowseResult>(
                [new CompanyBrowseResult("199001011234", "Tolv Siffror", "0180", "Stockholm", ["62010"])],
                totalCount: 1, page: 1, pageSize: 20));

        var result = await HandlerFor(db, Owner, port)
            .Handle(new BrowseCompaniesQuery(criterion.Id.Value, 1, 20), ct);

        result.ShouldNotBeNull();
        var hit = result.Items.Single();
        hit.OrganizationNumber.ShouldBeNull();
        hit.IsProtectedIdentity.ShouldBeTrue();
    }

    private static BrowseCompaniesQueryHandler HandlerFor(
        AppDbContext db,
        Guid userId,
        ICompanyWatchBrowseQuery port,
        IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new BrowseCompaniesQueryHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>(), port);
    }

    private static async Task<CompanyWatchCriterion> SeedCriterionAsync(
        AppDbContext db, Guid userId, string[] sni, string[] kommun, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(sni, kommun).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
