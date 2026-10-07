using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// The accounts each status needs, made by the actor production uses for it, plus an admin caller. Every
/// address carries the test's own token, so a search by the token is exact in the shared database.
/// </summary>
internal static class AdminAccountsKit
{
    public const string SearchPath = "/api/v1/admin/accounts/search";

    public static string DetailPath(Guid id) => $"/api/v1/admin/accounts/{id}";

    /// <summary>A token no other test's address contains.</summary>
    public static string NewToken() => $"acct{Guid.NewGuid():N}"[..16];

    public static string Address(string token, string label) => $"{label}-{token}@example.se";

    /// <summary>An admin with a session on <paramref name="factory"/>'s host, and its user id.</summary>
    public static async Task<(HttpClient Client, Guid UserId, string SessionId)> AdminAsync(
        WebApplicationFactory<Program> factory, string token, CancellationToken ct)
    {
        var email = Address(token, "admin");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: ct);
        var userId = await UserIdAsync(factory, email, ct);
        await PromoteAsync(factory, userId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (client, userId, sessionId);
    }

    /// <summary>A signed-in account without the admin role.</summary>
    public static async Task<HttpClient> UserAsync(
        WebApplicationFactory<Program> factory, string token, CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, Address(token, "user"), ct: ct);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    /// <summary>An active account, opened by the one writer of a new account.</summary>
    public static async Task<Guid> OpenActiveAsync(WebApplicationFactory<Program> factory, string email, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var opened = await scope.ServiceProvider.GetRequiredService<AccountRegistrar>().OpenAsync(email, ct);
        opened.IsSuccess.ShouldBeTrue();
        return await UserIdAsync(factory, email, ct);
    }

    /// <summary>
    /// Historical data left by AccountRegistrar before #1976: Identity committed before the profile save failed.
    /// AccountRegistrationAtomicityTests pins that today's registrar does not leave this shape.
    /// </summary>
    public static async Task<Guid> CreateWithoutProfileAsync(
        WebApplicationFactory<Program> factory, string email, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = Guid.NewGuid();
        await using var access = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([userId], false, ct);
        var created = await scope.ServiceProvider.GetRequiredService<IPasswordlessAccountCreator>()
            .CreatePasswordlessUserAsync(userId, email, ct);
        created.IsSuccess.ShouldBeTrue();
        await access.CommitAsync(ct);
        return created.Value;
    }

    /// <summary>An account pending deletion, through the real self-service deletion with its grant.</summary>
    public static async Task<Guid> CreatePendingDeletionAsync(ApiFactory factory, string email, CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: ct);
        var client = factory.CreateClient();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, client, sessionId, email, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/delete")
        {
            Content = JsonContent.Create(new { reauthGrant = grant }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        var deleted = await client.SendAsync(request, ct);
        deleted.IsSuccessStatusCode.ShouldBeTrue(await deleted.Content.ReadAsStringAsync(ct));
        return await UserIdAsync(factory, email, ct);
    }

    public static async Task<Guid> UserIdAsync(WebApplicationFactory<Program> factory, string email, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("No such user.");
        return user.Id;
    }

    public static async Task PromoteAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await using var access = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([userId], true, CancellationToken.None);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (!await roles.RoleExistsAsync(Roles.Admin))
            (await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("No such user.");
        (await users.AddToRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
        await access.CommitAsync(CancellationToken.None);
    }

    public static async Task DemoteAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("No such user.");
        (await users.RemoveFromRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
    }

    public static Task<HttpResponseMessage> SearchAsync(HttpClient client, object body, CancellationToken ct) =>
        client.PostAsJsonAsync(SearchPath, body, ct);

    /// <summary>The search response's accounts and counts, parsed.</summary>
    public static async Task<JsonElement> SearchOkAsync(HttpClient client, object body, CancellationToken ct)
    {
        var response = await SearchAsync(client, body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static IReadOnlyList<JsonElement> Items(JsonElement search) =>
        search.GetProperty("accounts").GetProperty("items").EnumerateArray().ToList();

    public static IReadOnlyList<string> Emails(JsonElement search) =>
        Items(search).Select(item => item.GetProperty("email").GetString()!).ToList();

    public static async Task<int> RecentSearchCountAsync(WebApplicationFactory<Program> factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Jobbliggaren.Application.Common.Abstractions.IAppDbContext>();
        return await db.RecentJobSearches.IgnoreQueryFilters().CountAsync(ct);
    }
}
