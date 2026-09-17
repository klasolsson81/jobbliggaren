using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

public static class AuthTestHelpers
{
    /// <summary>
    /// Default test-lösenord för integration-tester. Inte ett riktigt secret.
    /// </summary>
    public const string DefaultTestPassword = "T3stlosen123456";

    /// <summary>
    /// Creates the account shape ADR 0142 D10 defines — a passwordless user whose address is
    /// confirmed — with a JobSeeker and a <see cref="SessionLifetime.Persistent"/> session, and returns
    /// the raw session id for an Authorization: Bearer header.
    /// </summary>
    public static async Task<string> RegisterAndGetSessionIdAsync(
        WebApplicationFactory<Program> factory,
        string? email = null,
        string displayName = "Test User",
        CancellationToken ct = default)
    {
        email ??= $"test-{Guid.NewGuid()}@example.se";

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                $"Bootstrap user creation failed: {string.Join(", ", created.Errors.Select(e => e.Code))}");

        return await RegisterJobSeekerAndCreateSessionAsync(
            services, user.Id, displayName, SessionLifetime.Persistent, ct);
    }

    /// <summary>
    /// Creates the account shape the flag-OFF branch of <c>RegisterCommandHandler</c> produces — a
    /// password through <see cref="IUserAccountService.CreateUserAsync"/>, an unconfirmed address and a
    /// <see cref="SessionLifetime.Session"/> session — for tests whose subject is a password surface or
    /// the Session profile (ADR 0142 D9 amendment 2026-09-17).
    /// </summary>
    public static async Task<string> RegisterWithPasswordAndGetSessionIdAsync(
        WebApplicationFactory<Program> factory,
        string? email = null,
        string password = DefaultTestPassword,
        string displayName = "Test User",
        CancellationToken ct = default)
    {
        email ??= $"test-{Guid.NewGuid()}@example.se";

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var created = await services.GetRequiredService<IUserAccountService>()
            .CreateUserAsync(email, password, ct);
        if (created.IsFailure)
            throw new InvalidOperationException($"Bootstrap user creation failed: {created.Error.Code}");

        return await RegisterJobSeekerAndCreateSessionAsync(
            services, created.Value, displayName, SessionLifetime.Session, ct);
    }

    private static async Task<string> RegisterJobSeekerAndCreateSessionAsync(
        IServiceProvider services,
        Guid userId,
        string displayName,
        SessionLifetime lifetime,
        CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, displayName, services.GetRequiredService<IDateTimeProvider>());
        if (seeker.IsFailure)
            throw new InvalidOperationException($"Bootstrap JobSeeker.Register failed: {seeker.Error.Code}");

        var db = services.GetRequiredService<IAppDbContext>();
        db.JobSeekers.Add(seeker.Value);
        await db.SaveChangesAsync(ct);

        var session = await services.GetRequiredService<ISessionStore>().CreateAsync(userId, lifetime, ct);
        return session.Id.Reveal();
    }

    /// <summary>
    /// Loggar in en befintlig user och returnerar raw session-id för Authorization: Bearer-header.
    /// </summary>
    public static async Task<string> LoginAndGetSessionIdAsync(
        HttpClient client,
        string email,
        string password = DefaultTestPassword,
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
