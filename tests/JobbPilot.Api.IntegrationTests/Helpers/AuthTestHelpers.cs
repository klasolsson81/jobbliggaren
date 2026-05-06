using System.Net.Http.Json;
using System.Text.Json;

namespace JobbPilot.Api.IntegrationTests.Helpers;

public static class AuthTestHelpers
{
    /// <summary>
    /// Registrerar en ny user och returnerar raw session-id för Authorization: Bearer-header.
    /// Varje anrop skapar en unik e-post (Guid-suffix) för att undvika konflikter.
    /// </summary>
    public static async Task<string> RegisterAndGetSessionIdAsync(
        HttpClient client,
        string? email = null,
        string password = "T3stlosen123456",
        string displayName = "Test User",
        CancellationToken ct = default)
    {
        email ??= $"test-{Guid.NewGuid()}@example.se";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password, displayName },
            ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("sessionId saknas i register-response.");
    }

    /// <summary>
    /// Loggar in en befintlig user och returnerar raw session-id för Authorization: Bearer-header.
    /// </summary>
    public static async Task<string> LoginAndGetSessionIdAsync(
        HttpClient client,
        string email,
        string password = "T3stlosen123456",
        CancellationToken ct = default)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password },
            ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("sessionId saknas i login-response.");
    }
}
