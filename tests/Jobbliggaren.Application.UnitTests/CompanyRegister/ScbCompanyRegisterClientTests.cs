using System.Net;
using System.Text;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — unit tests for the SCB client's request/response plumbing against a FAKE
/// <see cref="HttpMessageHandler"/> (no real cert, no network). Exercises the code-table seeding →
/// count-then-slice → fetch flow and the tolerant wire→<see cref="ScbCompanyRecord"/> mapping. NB:
/// the canned <c>hamtaforetag</c> envelope encodes the ASSUMED response shape; the exact live shape is
/// confirmed at the population run and the mapping (Infrastructure-internal, behind the ACL) adjusted
/// there if needed.
/// </summary>
public class ScbCompanyRegisterClientTests
{
    // Assumed hamtaforetag envelope: one legal entity (Volvo), advertising-blocked (Reklam 21),
    // active (Företagsstatus 1), one SNI code, seat Stockholm.
    private const string HamtaJson = """
        [
          {
            "Företagsstatus": "1",
            "Variabler": [
              { "Namn": "OrgNr", "Värde": "5560125790" },
              { "Namn": "Företagsnamn", "Värde": "Volvo AB" }
            ],
            "Kategorier": [
              { "Kategori_id": "SätesKommun", "Kod": "0180", "Klartext": "Stockholm" },
              { "Kategori_id": "Bransch", "Kod": "29100", "Klartext": "Motorfordon" },
              { "Kategori_id": "Reklam", "Kod": "21", "Klartext": "Har frånsagt sig reklam" }
            ]
          }
        ]
        """;

    [Fact]
    public async Task StreamLegalEntitiesAsync_SeedsFromCodeTables_FiltersSoleTraders_AndMapsRow()
    {
        var client = BuildClient(new FakeScbHandler());
        var outcome = new ScbSyncOutcome();

        var batches = new List<IReadOnlyList<ScbCompanyRecord>>();
        await foreach (var batch in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            batches.Add(batch);
        }

        var record = batches.SelectMany(b => b).ShouldHaveSingleItem();
        record.OrganizationNumber.ShouldBe("5560125790");
        record.Name.ShouldBe("Volvo AB");
        record.SeatMunicipalityCode.ShouldBe("0180");
        record.SeatMunicipalityName.ShouldBe("Stockholm");
        record.SniCodes.ShouldBe(["29100"]);
        record.HasAdvertisingBlock.ShouldBeTrue();     // Reklam 21 → opted out
        record.RawStatusCode.ShouldBe("1");
        outcome.PartitionsFetched.ShouldBe(1);
        outcome.TotalRowsFetched.ShouldBe(1);
        outcome.TruncatedOrErrored.ShouldBeFalse();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_MarksTruncated_WhenCodeTablesEmpty()
    {
        var client = BuildClient(new EmptyCodeTableHandler());
        var outcome = new ScbSyncOutcome();

        var any = false;
        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            any = true;
        }

        any.ShouldBeFalse();
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamLegalEntitiesAsync_MarksTruncated_WhenCountEnvelopeUnrecognized()
    {
        // Fail-safe (dotnet-architect Minor): a raknaforetag body we cannot parse must NOT look like a
        // legitimate 0 (which would silently skip the partition and let the sweep deregister it). Code
        // tables seed fine, but the count is an unrecognized shape → the run is marked truncated → the
        // sweep is disabled downstream.
        var client = BuildClient(new UnparseableCountHandler());
        var outcome = new ScbSyncOutcome();

        await foreach (var _ in client.StreamLegalEntitiesAsync(
            outcome, TestContext.Current.CancellationToken))
        {
            // no batches expected (count parsed as 0)
        }

        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    private static ScbCompanyRegisterClient BuildClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://fake.scb.local/nv0101/v1/sokpavar/"),
        };
        var options = Options.Create(new ScbRegisterOptions { BatchSize = 2000 });
        return new ScbCompanyRegisterClient(httpClient, options, NullLogger<ScbCompanyRegisterClient>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeScbHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // Dispatch by endpoint (path checked before the Juridisk-form body probe, which also
            // appears in rakna/hamta filter bodies).
            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""[{"Kod":"10","Text":"Fysiska personer"},{"Kod":"49","Text":"Övriga aktiebolag"}]""")
                    : Json("""[{"Kod":"0180","Text":"Stockholm"}]""");
            }
            if (path.EndsWith("raknaforetag", StringComparison.Ordinal))
                return Json("2");
            if (path.EndsWith("hamtaforetag", StringComparison.Ordinal))
                return Json(HamtaJson);
            return Json("[]");
        }
    }

    private sealed class EmptyCodeTableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Json("[]"));
    }

    // Code tables seed normally, but raknaforetag returns an object with no recognizable count field.
    private sealed class UnparseableCountHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path.EndsWith("kodtabell", StringComparison.Ordinal))
            {
                return body.Contains("Juridisk form", StringComparison.Ordinal)
                    ? Json("""[{"Kod":"49"}]""")
                    : Json("""[{"Kod":"0180"}]""");
            }
            // Unrecognized count envelope (no bare number, no Antal/Count).
            return Json("""{"unexpected":true}""");
        }
    }
}
