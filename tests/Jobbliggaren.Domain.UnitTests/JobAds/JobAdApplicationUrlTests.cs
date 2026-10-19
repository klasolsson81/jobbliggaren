using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

public class JobAdApplicationUrlTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private const string AdUrl = "https://arbetsformedlingen.se/platsbanken/annonser/26500001";
    private const string Payload = "{\"id\":\"26500001\"}";

    private static readonly ApplicationUrl EmployerPage =
        ApplicationUrl.FromSource("https://career.example.se/jobs/4567890-integration-developer")!;

    private static JobAd Import(ApplicationUrl? applicationUrl, string url = AdUrl) =>
        JobAd.Import(
            "Integration developer",
            Company.Create("Exempel AB").Value,
            "Vi söker en integrationsutvecklare.",
            url,
            ExternalReference.Create(JobSource.Platsbanken, "26500001").Value,
            Payload,
            TestFacets.FromPayload(Payload),
            [],
            Clock.UtcNow,
            null,
            Clock,
            extractTerms: TestKeywordExtraction.None,
            applicationUrl: applicationUrl).Value;

    private static Result Update(JobAd jobAd, ApplicationUrl? applicationUrl) =>
        jobAd.UpdateFromSource(
            jobAd.Title,
            jobAd.Description,
            AdUrl,
            Payload,
            TestFacets.FromPayload(Payload),
            [],
            jobAd.PublishedAt,
            null,
            extractTerms: TestKeywordExtraction.None,
            applicationUrl: applicationUrl);

    [Fact]
    public void Import_WithApplicationUrl_StoresIt() =>
        Import(EmployerPage).ApplicationUrl.ShouldBe(EmployerPage);

    [Fact]
    public void Import_WithoutApplicationUrl_StoresNull() =>
        Import(null).ApplicationUrl.ShouldBeNull();

    [Fact]
    public void UpdateFromSource_WithANewApplicationUrl_ReplacesIt()
    {
        var jobAd = Import(EmployerPage);
        var moved = ApplicationUrl.FromSource("https://career.example.se/jobs/4567890-v2")!;

        Update(jobAd, moved).IsSuccess.ShouldBeTrue();

        jobAd.ApplicationUrl.ShouldBe(moved);
    }

    [Fact]
    public void UpdateFromSource_SourceNoLongerCarriesIt_ClearsIt()
    {
        var jobAd = Import(EmployerPage);

        Update(jobAd, null).IsSuccess.ShouldBeTrue();

        jobAd.ApplicationUrl.ShouldBeNull();
    }

    [Fact]
    public void UpdateFromSource_OnAnArchivedAd_KeepsItNull()
    {
        var jobAd = Import(EmployerPage);
        jobAd.Archive(Clock).IsSuccess.ShouldBeTrue();

        Update(jobAd, EmployerPage).IsSuccess.ShouldBeTrue();

        jobAd.ApplicationUrl.ShouldBeNull();
    }

    [Fact]
    public void Archive_ClearsIt()
    {
        var jobAd = Import(EmployerPage);

        jobAd.Archive(Clock).IsSuccess.ShouldBeTrue();

        jobAd.ApplicationUrl.ShouldBeNull();
    }

    [Fact]
    public void Erase_ClearsIt()
    {
        var jobAd = Import(EmployerPage);

        jobAd.Erase(Clock).IsSuccess.ShouldBeTrue();

        jobAd.ApplicationUrl.ShouldBeNull();
    }

    [Fact]
    public void Create_ManualAd_HasNone()
    {
        var jobAd = JobAd.Create(
            "Integration developer",
            Company.Create("Exempel AB").Value,
            "Vi söker en integrationsutvecklare.",
            "https://career.example.se/jobs/4567890",
            JobSource.Manual,
            Clock.UtcNow,
            null,
            Clock).Value;

        jobAd.ApplicationUrl.ShouldBeNull();
    }
}
