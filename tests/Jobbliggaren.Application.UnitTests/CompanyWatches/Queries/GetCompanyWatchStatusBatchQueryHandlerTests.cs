using Jobbliggaren.Application.Common.Abstractions;
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
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();
    private const string FollowedOrgNr = "5592804784";
    private const string OtherOrgNr = "5560360793";

    public GetCompanyWatchStatusBatchQueryHandlerTests() => _currentUser.UserId.Returns(_userId);

    private GetCompanyWatchStatusBatchQueryHandler Handler(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _employerReader, _currentUser);

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

    [Fact]
    public async Task Handle_WhenAnonymous_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new GetCompanyWatchStatusBatchQueryHandler(db, _employerReader, anon)
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
}
