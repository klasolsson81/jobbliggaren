using System.Net.Http.Json;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Auth;

[Collection("Api")]
public class AuthProviderDefaultsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_register_new_user_has_Provider_Local_and_null_ProviderUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"provider-{Guid.NewGuid()}@example.com";
        var body = new
        {
            email,
            password = "T3stlosen123456",
            displayName = "Provider Test User",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);
        response.IsSuccessStatusCode.ShouldBeTrue();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Email == email, ct);

        user.ShouldNotBeNull();
        user.Provider.ShouldBe(AuthProvider.Local);
        user.ProviderUserId.ShouldBeNull();
    }
}
