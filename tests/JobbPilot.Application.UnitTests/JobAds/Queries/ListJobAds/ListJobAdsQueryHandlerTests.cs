using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.ListJobAds;

public class ListJobAdsQueryHandlerTests
{
    private static JobAd CreateJobAd(
        string title,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt = null) =>
        JobAd.Create(
            title,
            Company.Create("Klarna").Value,
            "Vi söker en backend-utvecklare.",
            "https://jobs.klarna.com/job/1",
            JobSource.Manual,
            publishedAt,
            expiresAt,
            FakeDateTimeProvider.Default).Value;

    [Fact]
    public async Task Handle_WithJobAds_ReturnsPagedResultOrderedByPublishedAtDescending()
    {
        await using var db = TestAppDbContextFactory.Create();
        var baseTime = FakeDateTimeProvider.Default.UtcNow;
        var older = CreateJobAd("Junior Developer", baseTime.AddHours(-1));
        var newer = CreateJobAd("Senior Developer", baseTime);
        db.JobAds.Add(older);
        db.JobAds.Add(newer);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListJobAdsQueryHandler(db);
        var result = await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        result.TotalCount.ShouldBe(2);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
        result.Items.Count.ShouldBe(2);
        result.Items[0].Title.ShouldBe("Senior Developer");
        result.Items[1].Title.ShouldBe("Junior Developer");
    }

    [Fact]
    public async Task Handle_WithNoJobAds_ReturnsEmptyPagedResult()
    {
        await using var db = TestAppDbContextFactory.Create();
        var handler = new ListJobAdsQueryHandler(db);

        var result = await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        result.TotalCount.ShouldBe(0);
        result.Items.ShouldBeEmpty();
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_WithPagination_ReturnsCorrectSlice()
    {
        await using var db = TestAppDbContextFactory.Create();
        var baseTime = FakeDateTimeProvider.Default.UtcNow;
        for (var i = 0; i < 25; i++)
        {
            db.JobAds.Add(CreateJobAd($"Ad {i:00}", baseTime.AddMinutes(-i)));
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListJobAdsQueryHandler(db);

        var page1 = await handler.Handle(
            new ListJobAdsQuery(Page: 1, PageSize: 10), TestContext.Current.CancellationToken);
        var page2 = await handler.Handle(
            new ListJobAdsQuery(Page: 2, PageSize: 10), TestContext.Current.CancellationToken);
        var page3 = await handler.Handle(
            new ListJobAdsQuery(Page: 3, PageSize: 10), TestContext.Current.CancellationToken);

        page1.TotalCount.ShouldBe(25);
        page1.Items.Count.ShouldBe(10);
        page1.TotalPages.ShouldBe(3);
        page2.Items.Count.ShouldBe(10);
        page3.Items.Count.ShouldBe(5);

        // Inga duplikat över sidor (deterministisk sortering via Id som tiebreaker).
        var allIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.Id).ToList();
        allIds.Distinct().Count().ShouldBe(25);
    }

    [Fact]
    public async Task Handle_SortByPublishedAtAsc_ReturnsOldestFirst()
    {
        await using var db = TestAppDbContextFactory.Create();
        var baseTime = FakeDateTimeProvider.Default.UtcNow;
        db.JobAds.Add(CreateJobAd("Newest", baseTime));
        db.JobAds.Add(CreateJobAd("Oldest", baseTime.AddHours(-2)));
        db.JobAds.Add(CreateJobAd("Middle", baseTime.AddHours(-1)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListJobAdsQueryHandler(db);
        var result = await handler.Handle(
            new ListJobAdsQuery(SortBy: JobAdSortBy.PublishedAtAsc),
            TestContext.Current.CancellationToken);

        result.Items.Select(i => i.Title).ShouldBe(["Oldest", "Middle", "Newest"]);
    }

    [Fact]
    public async Task Handle_SortByExpiresAtAsc_NullsSortedLast()
    {
        await using var db = TestAppDbContextFactory.Create();
        var baseTime = FakeDateTimeProvider.Default.UtcNow;
        db.JobAds.Add(CreateJobAd("ExpiresLater", baseTime.AddDays(-1), baseTime.AddDays(7)));
        db.JobAds.Add(CreateJobAd("NoExpiry", baseTime.AddDays(-1), null));
        db.JobAds.Add(CreateJobAd("ExpiresSoon", baseTime.AddDays(-1), baseTime.AddDays(1)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListJobAdsQueryHandler(db);
        var result = await handler.Handle(
            new ListJobAdsQuery(SortBy: JobAdSortBy.ExpiresAtAsc),
            TestContext.Current.CancellationToken);

        result.Items.Select(i => i.Title).ShouldBe(["ExpiresSoon", "ExpiresLater", "NoExpiry"]);
    }

    [Fact]
    public async Task Handle_SortByExpiresAtDesc_NullsSortedLast()
    {
        await using var db = TestAppDbContextFactory.Create();
        var baseTime = FakeDateTimeProvider.Default.UtcNow;
        db.JobAds.Add(CreateJobAd("ExpiresLater", baseTime.AddDays(-1), baseTime.AddDays(7)));
        db.JobAds.Add(CreateJobAd("NoExpiry", baseTime.AddDays(-1), null));
        db.JobAds.Add(CreateJobAd("ExpiresSoon", baseTime.AddDays(-1), baseTime.AddDays(1)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListJobAdsQueryHandler(db);
        var result = await handler.Handle(
            new ListJobAdsQuery(SortBy: JobAdSortBy.ExpiresAtDesc),
            TestContext.Current.CancellationToken);

        result.Items.Select(i => i.Title).ShouldBe(["ExpiresLater", "ExpiresSoon", "NoExpiry"]);
    }
}
