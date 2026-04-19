using JobbPilot.Application.JobAds.Queries.GetJobAd;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.GetJobAd;

public class GetJobAdQueryHandlerTests
{
    private static JobAd CreateJobAd(string title = "Backend Developer") =>
        JobAd.Create(
            title,
            Company.Create("Klarna").Value,
            "Vi söker en backend-utvecklare.",
            "https://jobs.klarna.com/job/1",
            JobSource.Manual,
            FakeDateTimeProvider.Default.UtcNow,
            null,
            FakeDateTimeProvider.Default).Value;

    [Fact]
    public async Task Handle_WhenJobAdExists_ReturnsDto()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd("Backend Developer");
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetJobAdQueryHandler(db);
        var result = await handler.Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(jobAd.Id.Value);
        result.Title.ShouldBe("Backend Developer");
        result.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Handle_WhenJobAdNotFound_ReturnsNull()
    {
        await using var db = TestAppDbContextFactory.Create();
        var handler = new GetJobAdQueryHandler(db);

        var result = await handler.Handle(new GetJobAdQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
    }
}
