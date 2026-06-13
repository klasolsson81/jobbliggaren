using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

/// <summary>
/// TD-66 — cross-user-isolation för JobSeeker-profile-endpoints
/// (<c>/api/v1/me/profile</c>). Speglar pattern från
/// <c>ApplicationsCrossUserIsolationTests</c> + <c>ResumesCrossUserIsolationTests</c>.
///
/// JobSeeker-endpoints är claim-baserade — det finns ingen userId-param
/// att overrida. Isolation-invarianten är därför "PATCH från user B muterar
/// B:s profil, INTE A:s". Test bevakar att detta gäller framöver också
/// (regression-skydd om handler någonsin refactoreras med id-param-input).
///
/// Täckning per 2026-05-12: GET /api/v1/me + GET /api/v1/me/profile +
/// PATCH /api/v1/me/profile. Utöka om nya /me-endpoints adderas.
/// </summary>
[Collection("Api")]
public class MeProfileCrossUserIsolationTests(ApiFactory factory)
{
    private async Task<HttpClient> RegisterUserAsync(string userPrefix, string displayName, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{userPrefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email, displayName: displayName, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    [Fact]
    public async Task User_B_PATCH_profile_does_not_affect_user_A_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("me-iso-a", "Anna A", ct);
        var clientB = await RegisterUserAsync("me-iso-b", "Bertil B", ct);

        // User B muterar sin egen profil
        var patchResponse = await clientB.PatchAsJsonAsync(
            "/api/v1/me/profile",
            new
            {
                displayName = "Bertil Förändrad",
                language = "en",
                emailNotifications = false,
                weeklySummary = false
            },
            ct);
        patchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // User A:s profil ska vara orörd
        var aResponse = await clientA.GetAsync("/api/v1/me/profile", ct);
        aResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aJson = await aResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        aJson.GetProperty("displayName").GetString().ShouldBe("Anna A");

        // User B:s profil ska reflektera ändringen
        var bResponse = await clientB.GetAsync("/api/v1/me/profile", ct);
        bResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bJson = await bResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        bJson.GetProperty("displayName").GetString().ShouldBe("Bertil Förändrad");
    }

    [Fact]
    public async Task User_B_PATCH_with_extra_id_fields_does_not_affect_user_A()
    {
        // Defense-in-depth mot framtida regression: om handler råkar börja
        // läsa userId/jobSeekerId från payload (istället för enbart claim)
        // ska A:s profil ändå vara orörd. Idag ignoreras dessa fält av
        // modellbindningen — testet låser det beteendet.
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("me-iso-a", "Anna A", ct);
        var clientB = await RegisterUserAsync("me-iso-b", "Bertil B", ct);

        // Hämta A:s userId så vi kan försöka injicera den i B:s payload
        var aMeResponse = await clientA.GetAsync("/api/v1/me", ct);
        var aMeJson = await aMeResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userIdA = aMeJson.GetProperty("userId").GetString();

        // B PATCH:ar med extra-fält som POTENTIELLT skulle kunna tolkas
        // som override (om handler någonsin börjar läsa dem)
        var patchResponse = await clientB.PatchAsJsonAsync(
            "/api/v1/me/profile",
            new
            {
                displayName = "Bertil ändrad via injection",
                language = "en",
                emailNotifications = false,
                weeklySummary = false,
                userId = userIdA,
                jobSeekerId = userIdA
            },
            ct);
        patchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A:s profil ska vara orörd
        var aProfileResponse = await clientA.GetAsync("/api/v1/me/profile", ct);
        aProfileResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aProfileJson = await aProfileResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        aProfileJson.GetProperty("displayName").GetString().ShouldBe("Anna A");
    }

    [Fact]
    public async Task User_B_GET_me_returns_user_B_data_not_user_A_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("me-iso-a", "Anna A", ct);
        var clientB = await RegisterUserAsync("me-iso-b", "Bertil B", ct);

        var aMeResponse = await clientA.GetAsync("/api/v1/me", ct);
        var aJson = await aMeResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userIdA = aJson.GetProperty("userId").GetString();

        var bMeResponse = await clientB.GetAsync("/api/v1/me", ct);
        var bJson = await bMeResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userIdB = bJson.GetProperty("userId").GetString();

        // Sanity: olika userIds returneras till olika clients
        userIdA.ShouldNotBeNull();
        userIdB.ShouldNotBeNull();
        userIdA.ShouldNotBe(userIdB);
    }
}
