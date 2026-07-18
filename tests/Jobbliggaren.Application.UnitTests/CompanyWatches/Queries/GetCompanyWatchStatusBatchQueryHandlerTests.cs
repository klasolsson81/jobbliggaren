using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #455 — the follow-state batch correlates the user's active follows against each ad's employer org.nr
/// (resolved via the faked <see cref="IJobAdEmployerReader"/>). Proves: followed → companyWatchId set;
/// not-followed but org.nr present → followable, no id; null org.nr (B2) → not followable; absent ad →
/// not followable; anon/empty → empty. The response NEVER carries org.nr (guarded structurally + here).
/// </summary>
public class GetCompanyWatchStatusBatchQueryHandlerTests
{
    private readonly IJobAdEmployerReader _employerReader = Substitute.For<IJobAdEmployerReader>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IProtectedIdentityTokenizer _tokenizer = Substitute.For<IProtectedIdentityTokenizer>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();
    private const string FollowedOrgNr = "5592804784";
    private const string OtherOrgNr = "5560360793";
    private const string PnrShapedOrgNr = "9001011234"; // 3rd digit 0 → personnummer-shaped (enskild)

    // Empty catalogue by default; a group test passes a synthetic one.
    private readonly StubProvider _brandGroups = Stub();

    public GetCompanyWatchStatusBatchQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Deterministic 64-char token (distinct from plaintext), mirroring a real HMAC.
        _tokenizer.Tokenize(Arg.Any<string>()).Returns(ci => "hmac" + ci.Arg<string>().PadLeft(60, '0'));
    }

    private static string FakeToken(string orgNr) => "hmac" + orgNr.PadLeft(60, '0');

    private GetCompanyWatchStatusBatchQueryHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, IBrandGroupProvider? provider = null) =>
        new(db, _employerReader, _currentUser, _tokenizer, provider ?? _brandGroups);

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

    private void ReaderReturns(Dictionary<Guid, string?> map) =>
        _employerReader
            .GetOrganizationNumbersByJobAdIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(map);

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

        var result = await new GetCompanyWatchStatusBatchQueryHandler(
                db, _employerReader, anon, _tokenizer, _brandGroups)
            .Handle(new GetCompanyWatchStatusBatchQuery([Guid.NewGuid()]), ct);

        result.Statuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoIds_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([]), ct);

        result.Statuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenEmployerFollowed_ReturnsCompanyWatchIdAndFollowable()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedFollowAsync(db, FollowedOrgNr, ct);
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = FollowedOrgNr });

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        var status = result.Statuses.Single();
        status.JobAdId.ShouldBe(jobAdId);
        status.Followable.ShouldBeTrue();
        status.CompanyWatchId.ShouldBe(watchId);
    }

    [Fact]
    public async Task Handle_WhenEmployerNotFollowedButHasOrgNumber_FollowableWithNullId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = OtherOrgNr }); // user follows nothing

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        var status = result.Statuses.Single();
        status.Followable.ShouldBeTrue();
        status.CompanyWatchId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenAdHasNoOrgNumber_NotFollowable()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = null }); // B2 not-re-ingested

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        var status = result.Statuses.Single();
        status.Followable.ShouldBeFalse();
        status.CompanyWatchId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenAdAbsent_NotFollowable()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new()); // reader knows no such ad

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        var status = result.Statuses.Single();
        status.Followable.ShouldBeFalse();
        status.CompanyWatchId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenDuplicateIds_ReturnsOneStatusPerDistinctAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = OtherOrgNr });

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusBatchQuery([jobAdId, jobAdId, jobAdId]), ct);

        result.Statuses.Count.ShouldBe(1);
        result.Statuses.Single().JobAdId.ShouldBe(jobAdId);
    }

    [Fact]
    public async Task Handle_MixedPage_MapsEachIndependently()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var followedWatchId = await SeedFollowAsync(db, FollowedOrgNr, ct);
        var followedAd = Guid.NewGuid();
        var otherAd = Guid.NewGuid();
        var noOrgAd = Guid.NewGuid();
        ReaderReturns(new()
        {
            [followedAd] = FollowedOrgNr,
            [otherAd] = OtherOrgNr,
            [noOrgAd] = null,
        });

        var result = await Handler(db).Handle(
            new GetCompanyWatchStatusBatchQuery([followedAd, otherAd, noOrgAd]), ct);

        result.Statuses.Single(s => s.JobAdId == followedAd).CompanyWatchId.ShouldBe(followedWatchId);
        result.Statuses.Single(s => s.JobAdId == otherAd).CompanyWatchId.ShouldBeNull();
        result.Statuses.Single(s => s.JobAdId == otherAd).Followable.ShouldBeTrue();
        result.Statuses.Single(s => s.JobAdId == noOrgAd).Followable.ShouldBeFalse();
    }

    // ─────────────────────────── #544 token-blindness closure + BrandGroup (#311 PR-5, ADR 0087 D4 D5e)

    [Fact]
    public async Task Handle_EnskildFollowStoredAsToken_CorrelatesViaTokenProbe_SelfProvingNegative()
    {
        // #544 gap-closure: an enskild follow is stored HMAC-tokenised at rest, but the page ad's org.nr
        // is PLAINTEXT. The handler must tokenise the ad's org.nr to correlate. MUTATION: drop the token
        // probe in Correlate() and this goes RED (the plaintext ad never matches the token-keyed map).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedRawStoredFollowAsync(db, FakeToken(PnrShapedOrgNr), ct);
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = PnrShapedOrgNr }); // the ad carries the PLAINTEXT pnr org.nr

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        var status = result.Statuses.Single();
        status.Followable.ShouldBeTrue();
        status.CompanyWatchId.ShouldBe(watchId); // correlated via the token probe, not null
    }

    [Fact]
    public async Task Handle_EnskildFollowStoredAsLegacyPlaintext_StillCorrelates()
    {
        // Backfill window: a legacy enskild row still holds the plaintext pnr. The direct raw probe
        // (dual-probe parity the scan/executor) must still correlate it.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watchId = await SeedRawStoredFollowAsync(db, PnrShapedOrgNr, ct); // legacy plaintext at rest
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = PnrShapedOrgNr });

        var result = await Handler(db).Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(watchId);
    }

    [Fact]
    public async Task Handle_AdOrgNrIsAGroupMember_CorrelatesToTheGroupWatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var groupWatchId = await SeedGroupFollowAsync(db, "volvo", ct);
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = FollowedOrgNr }); // the ad's employer is a group member

        var result = await Handler(db, Stub(("volvo", [FollowedOrgNr])))
            .Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        var status = result.Statuses.Single();
        status.Followable.ShouldBeTrue();
        status.CompanyWatchId.ShouldBe(groupWatchId);
    }

    [Fact]
    public async Task Handle_DirectEmployerFollowWins_OverAGroupMembership()
    {
        // Precedence (micro-decision 7): when an ad's org.nr is BOTH a direct employer follow AND a group
        // member, return the DIRECT watch id — the id feeds DELETE /{id} (unfollow), and returning the
        // group id would make an ad-card unfollow silently delete a whole brand-group follow.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var directWatchId = await SeedFollowAsync(db, FollowedOrgNr, ct);
        await SeedGroupFollowAsync(db, "volvo", ct);
        var jobAdId = Guid.NewGuid();
        ReaderReturns(new() { [jobAdId] = FollowedOrgNr });

        var result = await Handler(db, Stub(("volvo", [FollowedOrgNr])))
            .Handle(new GetCompanyWatchStatusBatchQuery([jobAdId]), ct);

        result.Statuses.Single().CompanyWatchId.ShouldBe(directWatchId);
    }
}
