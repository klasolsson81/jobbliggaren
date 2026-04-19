using System.Net;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

public class ListJobAdsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GET_job_ads_returns_200_with_empty_list_when_no_ads_exist()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/job-ads", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(ct);
        content.ShouldBe("[]");
    }
}
