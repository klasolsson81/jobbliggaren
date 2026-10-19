using System.Runtime.CompilerServices;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// #2191 — the ACL carries JobTech's <c>application_details.url</c> onto
/// <see cref="JobAdImportItem.ApplicationUrl"/>. The hits are built from wire JSON through the real
/// converter, so a wrong JSON path fails here instead of leaving the column silently empty. Hand-written
/// fakes for the same reason as <see cref="PlatsbankenJobSourceUtcNormalizationTests"/>: the clients
/// are <c>internal</c> and cannot be proxied.
/// </summary>
public class PlatsbankenJobSourceApplicationUrlMappingTests
{
    private const string AdPage = "https://arbetsformedlingen.se/platsbanken/annonser/ext-apply-1";
    private const string EmployerPage = "https://career.example.se/jobs/4567890-integration-developer";

    private static readonly DateTimeOffset FakeNow = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

    // applicationDetails is spliced in verbatim, so a case can omit the node or write any wire value.
    private static JobTechHit Hit(string? applicationDetails, bool withWebpageUrl = true) =>
        JsonSerializer.Deserialize<JobTechHit>($$"""
            {
              "id": "ext-apply-1",
              "headline": "Integration developer",
              "description": { "text": "Beskrivning av tjänsten." },
              "employer": { "name": "Exempel AB" },
              {{(withWebpageUrl ? $"\"webpage_url\": \"{AdPage}\"," : string.Empty)}}
              {{(applicationDetails is null ? string.Empty : $"\"application_details\": {applicationDetails},")}}
              "publication_date": "2026-10-19T08:00:00"
            }
            """)!;

    private static async Task<JobAdImportItem> MapAsync(JobTechHit hit)
    {
        var sut = new PlatsbankenJobSource(
            new FakeStreamClient(),
            new FakeSearchClient(hit),
            new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

        var item = await sut.RefetchByExternalIdAsync("ext-apply-1", TestContext.Current.CancellationToken);
        item.ShouldNotBeNull();
        return item;
    }

    [Fact]
    public async Task ApplicationDetailsUrl_IsMappedAsTheApplicationUrl_AndUrlStaysTheAdPage()
    {
        var item = await MapAsync(Hit($$"""{ "url": "{{EmployerPage}}", "via_af": false }"""));

        item.Url.ShouldBe(AdPage);
        item.ApplicationUrl.ShouldNotBeNull();
        item.ApplicationUrl.Value.ShouldBe(EmployerPage);
    }

    [Fact]
    public async Task NoApplicationDetailsNode_MapsNull()
    {
        var item = await MapAsync(Hit(applicationDetails: null));

        item.ApplicationUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "url": null, "email": "rekryterare@example.se" }""")]
    [InlineData("""{ "url": "mailto:rekryterare@example.se?subject=Job" }""")]
    [InlineData("""{ "url": "https://example.se/apply?email=anna@example.se" }""")]
    [InlineData("""{ "url": "https://example.se/apply?email=anna%40example.se" }""")]
    [InlineData("""{ "url": "Ansök via vår hemsida" }""")]
    [InlineData("""{ "url": "www.example.se/jobb" }""")]
    public async Task ApplicationDetailsUrl_TheValueObjectRefuses_MapsNull(string applicationDetails)
    {
        var item = await MapAsync(Hit(applicationDetails));

        item.ApplicationUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WithoutWebpageUrlOrSourceLinks_TheHitMapsToNoItem_ApplicationDetailsUrlNeverStandsInForTheAdPage()
    {
        // Actor: JobTech's wire contract, where webpage_url is optional.
        var sut = new PlatsbankenJobSource(
            new FakeStreamClient(),
            new FakeSearchClient(Hit($$"""{ "url": "{{EmployerPage}}" }""", withWebpageUrl: false)),
            new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

        var item = await sut.RefetchByExternalIdAsync("ext-apply-1", TestContext.Current.CancellationToken);

        item.ShouldBeNull();
    }

    private sealed class FakeSearchClient(JobTechHit? hit = null) : IJobTechSearchClient
    {
        public Task<JobTechHit?> GetAdByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(hit);

        public Task<JobTechSearchListResponse> SearchRemoteAsync(
            int offset, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobTechSearchListResponse
            {
                Total = new JobTechSearchTotal { Value = 0 },
                Hits = [],
            });
    }

    private sealed class FakeStreamClient(params JobTechHit[] hits) : IJobTechStreamClient
    {
        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        public IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        private static async IAsyncEnumerable<JobTechHit> Yield(
            JobTechHit[] items, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }
}
