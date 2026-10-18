using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth.AccountActivity;

/// <summary>
/// #2161 (ADR 0172) — the two writers of an activity row, as tests drive them: a real code login, whose session the
/// grant issues, and an authenticated request through the middleware, on a host whose clock the test moves. The row
/// itself is read straight from the table.
/// </summary>
internal static class AccountActivityKit
{
    public sealed record ActivityRow(DateTimeOffset? LastLoginAt, DateTimeOffset LastActiveAt);

    public static string ConnectionString(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
    }

    /// <summary>The account's activity row, read from the table, or null when it has none.</summary>
    public static async Task<ActivityRow?> RowOfAsync(ApiFactory factory, Guid userId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString(factory));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT a.last_login_at, a.last_active_at
            FROM account_activity a JOIN job_seekers js ON js.id = a.job_seeker_id
            WHERE js.user_id = @user_id
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ActivityRow(
            await reader.IsDBNullAsync(0, ct) ? null : reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetFieldValue<DateTimeOffset>(1));
    }

    /// <summary>A host on the fixture's databases whose clock the test moves, starting at the fixture's own now.</summary>
    public static (WebApplicationFactory<Program> Host, MutableFakeDateTimeProvider Clock) HostWithClock(ApiFactory factory)
    {
        var clock = new MutableFakeDateTimeProvider
        {
            UtcNow = factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow,
        };
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
        }));
        return (host, clock);
    }

    public static async Task<HttpStatusCode> MeAsync(HttpClient client, string? sessionId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        if (sessionId is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await client.SendAsync(request, ct)).StatusCode;
    }

    /// <summary>Asks for a login code for the address and reads it back from the mail the dispatch consumer sent.</summary>
    public static async Task<(string ChallengeId, string Code)> MintCodeAsync(
        ApiFactory factory, HttpClient client, string email, CancellationToken ct)
    {
        var before = factory.Emails.LoginChallenges.Count(m => m.ToEmail == email);
        var response = await client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (factory.Emails.LoginChallenges.Count(m => m.ToEmail == email) == before)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, ct);
        }

        var mail = factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList()[^1];
        return (body.RootElement.GetProperty("challengeId").GetString()!,
            ((LoginChallengeEmail.CodeAndLink)mail.Content).Code.Reveal());
    }

    /// <summary>A code login on an existing account: the grant issues the session and records the login.</summary>
    public static async Task CodeLoginAsync(ApiFactory factory, HttpClient client, string email, CancellationToken ct)
    {
        var (challengeId, code) = await MintCodeAsync(factory, client, email, ct);
        var verified = await client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code }, ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(ct));
    }
}
