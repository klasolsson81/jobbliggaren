using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusByOrgNrBatch;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 PR-C — the ORG.NR-keyed follow-state batch for /foretag/sok. Unlike the jobAdId-keyed sibling, the
/// org.nrs arrive directly (no <c>IJobAdEmployerReader</c> hop), so these tests seed follows and pass the
/// org.nrs straight in. Proves: followed → companyWatchId set; not-followed → null id; the three
/// correlation channels (AB plaintext, enskild token/legacy probe, brand-group member) + direct-wins
/// precedence; and — the two org.nr-specific contracts — the response is POSITIONAL (1:1 with the request
/// order) and NOT deduped. The response DTO carries no org.nr member (guarded structurally).
/// </summary>
public class GetCompanyWatchStatusByOrgNrBatchQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IProtectedIdentityTokenizer _tokenizer = Substitute.For<IProtectedIdentityTokenizer>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();
    private const string FollowedOrgNr = "5592804784";
    private const string OtherOrgNr = "5560360793";
    private const string ThirdOrgNr = "5569999999"; // request-only, never seeded → correlates to null
    private const string PnrShapedOrgNr = "9001011234"; // 3rd digit 0 → personnummer-shaped (enskild)

    // Empty catalogue by default; a group test passes a synthetic one.
    private readonly StubProvider _brandGroups = Stub();

    public GetCompanyWatchStatusByOrgNrBatchQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Deterministic 64-char token (distinct from plaintext), mirroring a real HMAC.
        _tokenizer.Tokenize(Arg.Any<string>()).Returns(ci => "hmac" + ci.Arg<string>().PadLeft(60, '0'));
    }

    private static string FakeToken(string orgNr) => "hmac" + orgNr.PadLeft(60, '0');

    private GetCompanyWatchStatusByOrgNrBatchQueryHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, IBrandGroupProvider? provider = null) =>
        new(db, _currentUser, _tokenizer, provider ?? _brandGroups);

    private static StubProvider Stub(params (string Slug, string[] Members)[] groups)
    {
        var dict = groups.ToDictionary(
            g => g.Slug, g => new BrandGroup(g.Slug, g.Slug + " (koncern)", g.Members), StringComparer.Ordinal);
        return new StubProvider(new BrandGroupCatalog("test.v1", dict));
    }

    private sealed class StubProvider(BrandGroupCatalog catalog) : IBrandGroupProvider
    {
        public BrandGroupCatalog Catalog { get; } = catalog;
    }

    private async Task<Guid> SeedFollowAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, string orgNr, CancellationToken ct)
    {
        var watch = CompanyWatch.Follow(_userId, OrganizationNumber.Create(orgNr).Value, _clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id.Value;
    }

    // Seeds an enskild follow with the value stored EXACTLY as the executor would store it: FromTrusted
    // (a token for a pnr-shaped org.nr, or the raw plaintext for the legacy backfill window).
    private async Task<Guid> SeedRawStoredFollowAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, string storedValue, CancellationToken ct)
    {
        var watch = CompanyWatch.Follow(_userId, OrganizationNumber.FromTrusted(storedValue), _clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id.Value;
    }

    private async Task<Guid> SeedGroupFollowAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, string slug, CancellationToken ct)
    {
        var watch = CompanyWatch.FollowBrandGroup(_userId, BrandGroupId.Create(slug).Value, _clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id.Value;
    }

    [Fact]
    public async Task Handle_WhenAnonymous_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new GetCompanyWatchStatusByOrgNrBatchQueryHandler(
                db, anon, _tokenizer, _brandGroups)
            .Handle(new GetCompanyWatchStatusByOrgNrBatchQuery([FollowedOrgNr]), ct);

        result.Statuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoOrgNrs_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new GetCompanyWatchStatusByOrgNrBatchQuery([]), ct);

        result.Statuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenEmployerFollowed_ReturnsCompanyWatchId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedFollowAsync(db, FollowedOrgNr, ct);

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusByOrgNrBatchQuery([FollowedOrgNr]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(watchId);
    }

    [Fact]
    public async Task Handle_WhenNotFollowed_ReturnsNullId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        // user follows nothing

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusByOrgNrBatchQuery([OtherOrgNr]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_PreservesRequestOrder_Positional()
    {
        // The FE zips the response to its request list by index, so the handler must emit one status per
        // requested org.nr IN ORDER. MUTATION: add .Distinct() or reorder in the projection and this goes
        // RED (idB lands at the wrong index).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedFollowAsync(db, FollowedOrgNr, ct);

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusByOrgNrBatchQuery([OtherOrgNr, FollowedOrgNr, ThirdOrgNr]), ct);

        result.Statuses.Count.ShouldBe(3);
        result.Statuses[0].CompanyWatchId.ShouldBeNull();
        result.Statuses[1].CompanyWatchId.ShouldBe(watchId);
        result.Statuses[2].CompanyWatchId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenDuplicateOrgNrs_PreservesThemNotDeduped()
    {
        // Contrast the jobAdId-keyed sibling (which dedupes to one-per-distinct): here dedup would misalign
        // the positional zip. MUTATION: add .Distinct() and this goes RED (Count == 1).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedFollowAsync(db, FollowedOrgNr, ct);

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusByOrgNrBatchQuery([FollowedOrgNr, FollowedOrgNr]), ct);

        result.Statuses.Count.ShouldBe(2);
        result.Statuses[0].CompanyWatchId.ShouldBe(watchId);
        result.Statuses[1].CompanyWatchId.ShouldBe(watchId);
    }

    // ─────────────────────────── #544 token-blindness closure + BrandGroup (#311 PR-5, ADR 0087 D4 D5e)

    [Fact]
    public async Task Handle_EnskildFollowStoredAsToken_CorrelatesViaTokenProbe_SelfProvingNegative()
    {
        // #544: an enskild follow is stored HMAC-tokenised at rest, but the search row's org.nr is
        // PLAINTEXT. The handler must tokenise the request org.nr to correlate. MUTATION: drop the token
        // probe in Correlate() and this goes RED (the plaintext never matches the token-keyed map).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedRawStoredFollowAsync(db, FakeToken(PnrShapedOrgNr), ct);

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusByOrgNrBatchQuery([PnrShapedOrgNr]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(watchId); // correlated via the token probe, not null
    }

    [Fact]
    public async Task Handle_EnskildFollowStoredAsLegacyPlaintext_StillCorrelates()
    {
        // Backfill window: a legacy enskild row still holds the plaintext pnr. The direct raw probe
        // (dual-probe parity with the scan/executor) must still correlate it.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedRawStoredFollowAsync(db, PnrShapedOrgNr, ct); // legacy plaintext at rest

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusByOrgNrBatchQuery([PnrShapedOrgNr]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(watchId);
    }

    [Fact]
    public async Task Handle_OrgNrIsAGroupMember_CorrelatesToTheGroupWatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var groupWatchId = await SeedGroupFollowAsync(db, "volvo", ct);

        var result = await Handler(db, Stub(("volvo", [FollowedOrgNr])))
            .Handle(new GetCompanyWatchStatusByOrgNrBatchQuery([FollowedOrgNr]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(groupWatchId);
    }

    [Fact]
    public async Task Handle_DirectEmployerFollowWins_OverAGroupMembership()
    {
        // Precedence: when an org.nr is BOTH a direct employer follow AND a group member, return the DIRECT
        // watch id — the id feeds DELETE /{id} (unfollow), and returning the group id would make a single
        // -row unfollow silently delete a whole brand-group follow.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var directWatchId = await SeedFollowAsync(db, FollowedOrgNr, ct);
        await SeedGroupFollowAsync(db, "volvo", ct);

        var result = await Handler(db, Stub(("volvo", [FollowedOrgNr])))
            .Handle(new GetCompanyWatchStatusByOrgNrBatchQuery([FollowedOrgNr]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(directWatchId);
    }
}
