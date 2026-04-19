using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.ListJobAds;

public class ListJobAdsQueryHandlerTests
{
    private static JobAd CreateJobAd(string title, DateTimeOffset publishedAt) =>
        JobAd.Create(
            title,
            Company.Create("Klarna").Value,
            "Vi söker en backend-utvecklare.",
            "https://jobs.klarna.com/job/1",
            JobSource.Manual,
            publishedAt,
            null,
            FakeDateTimeProvider.Default).Value;

    [Fact]
    public async Task Handle_WithJobAds_ReturnsDtosOrderedByPublishedAtDescending()
    {
        await using var db = TestAppDbContextFactory.Create();
        var baseTime = FakeDateTimeProvider.Default.UtcNow;
        var older = CreateJobAd("Junior Developer", baseTime.AddHours(-1));
        var newer = CreateJobAd("Senior Developer", baseTime);
        db.JobAds.Add(older);
        db.JobAds.Add(newer);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListJobAdsQueryHandler(db);
        var result = await handler.Handle(new ListJobAdsQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        result[0].Title.ShouldBe("Senior Developer");
        result[1].Title.ShouldBe("Junior Developer");
    }

    [Fact]
    public async Task Handle_WithNoJobAds_ReturnsEmptyList()
    {
        await using var db = TestAppDbContextFactory.Create();
        var handler = new ListJobAdsQueryHandler(db);

        var result = await handler.Handle(new ListJobAdsQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }
}
