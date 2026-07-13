using Jobbliggaren.Application.JobAds.Queries.GetJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetJobAd;

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

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(jobAd.Id.Value);
        result.Value.Title.ShouldBe("Backend Developer");
        result.Value.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Handle_WhenJobAdNotFound_ReturnsNotFound()
    {
        await using var db = TestAppDbContextFactory.Create();
        var handler = new GetJobAdQueryHandler(db);

        var result = await handler.Handle(new GetJobAdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound, "an id we never held is a 404.");
    }

    /// <summary>
    /// #842 — an erased ad is GONE (410), never MISSING (404). The distinction is not pedantry:
    /// 404 tells the person holding a link to an ad she applied to that we never had it, which is
    /// false — and manufacturing a false statement to a data subject is the exact defect class this
    /// issue is about. She keeps her own frozen record (ADR 0086's AdSnapshot) either way.
    /// </summary>
    [Fact]
    public async Task Handle_WhenJobAdErased_ReturnsGone_NotNotFound()
    {
        await using var db = TestAppDbContextFactory.Create();
        var jobAd = CreateJobAd();
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        jobAd.Erase(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetJobAdQueryHandler(db);
        var result = await handler.Handle(new GetJobAdQuery(jobAd.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Gone,
            "an erased ad existed and is deliberately gone — 410, which the central mapper "
            + "already translates. A 404 here would say we never held it.");

        // The message is deliberately NEUTRAL. The ad id is public and Arbetsförmedlingen publishes
        // the same ad in its open Historiska annonser dataset — so saying "raderad enligt artikel
        // 17" would let anyone correlate the two and infer that the recruiter named in that ad
        // exercised a right. The erasure would then broadcast the very fact it exists to protect.
        result.Error.Code.ShouldBe("JobAd.Gone");
        result.Error.Message.ShouldNotContain("artikel");
        result.Error.Message.ShouldNotContain("raderat");
    }
}
