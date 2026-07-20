using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #544 (ADR 0090 D5) — end-to-end on the wired API: following an enskild-firma (personnummer-shaped)
/// org.nr stores an HMAC token at rest, yet <c>GET /api/v1/me/company-watches</c> still resolves the
/// public <c>companyName</c> + <c>activeAdCount</c> for that employer via the token→plaintext
/// resolution — while MASKING the org.nr itself (<c>organizationNumber: null</c> +
/// <c>isProtectedIdentity: true</c>).
/// </summary>
/// <remarks>
/// The DTO output is unchanged from the plaintext era (ADR 0087 D3/D8(c)) — only the at-rest storage
/// differs. This proves the WIRE contract: a sole proprietor's org.nr (which equals their personnummer,
/// CLAUDE.md §5) never leaves the handler, but the useful public signals still do. The count
/// correctness itself is pinned exhaustively by CompanyWatchMatchCountTests; the token→plaintext
/// resolution mechanics by ListCompanyWatchesQueryHandler's Testcontainers coverage.
/// <para>
/// The [Collection("Api")] Postgres is SHARED and never reset, so this test seeds a PRIVATE
/// Guid-suffixed pnr-shaped org.nr no other test touches — a deterministic count of 1 despite the
/// shared DB (memory api_integration_shared_db_contamination).
/// </para>
/// </remarks>
[Collection("Api")]
public class CompanyWatchesEnskildFirmaMaskingApiTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task<string> FollowAsync(string orgNr, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(Endpoint, new { organizationNumber = orgNr }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> ListAsync(CancellationToken ct)
    {
        var response = await _client.GetAsync(Endpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    // A PRIVATE, Guid-unique personnummer-shaped org.nr (third digit '0' → IsPersonnummerShaped true).
    private static string NewPnrShapedOrgNr() =>
        "900" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString("D7", CultureInfo.InvariantCulture);

    // Seeds a public Active ad for orgNr carrying the nested employer.organization_number (the org.nr
    // shadow column) + employer.name. Only Postgres computes the generated column → Testcontainers-only.
    private async Task SeedActiveAdAsync(string orgNr, string companyName, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"cw544-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task GET_list_masks_enskild_firma_orgNr_but_still_resolves_name_and_active_ad_count()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewPnrShapedOrgNr();
        await SeedActiveAdAsync(orgNr, "Enskild Firma Karlsson", ct);
        await AuthenticateAsync(ct);
        var id = await FollowAsync(orgNr, ct); // tokenised at rest (pnr-shaped) via the real endpoint

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);

        // MASKED: a sole prop's org.nr equals their personnummer — it must never leave the handler.
        item.GetProperty("organizationNumber").ValueKind.ShouldBe(JsonValueKind.Null,
            "en personnummer-format org.nr (enskild firma) får ALDRIG surfas — den maskeras till null.");
        item.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeTrue(
            "isProtectedIdentity flaggar att org.nr:et maskerats (FORK C1 / D8(c)).");

        // ...yet the PUBLIC signals still resolve via the token→plaintext resolution (HMAC is
        // deterministic: the handler re-derives the token from the pnr-shaped active-ad set).
        item.GetProperty("companyName").GetString().ShouldBe("Enskild Firma Karlsson",
            "företagsnamnet resolvas at READ från publika job_ads via token→plaintext-upplösningen.");
        item.GetProperty("activeAdCount").GetInt32().ShouldBe(1,
            "den publika aktiva-annons-räkningen surfas ÄVEN när org.nr:et är maskerat (ingen PII).");
    }
}
