using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Auth.AccountActivity;

/// <summary>
/// #2161 (ADR 0172) — the activity record through the real pipeline: the middleware behind the session handler on
/// every request, and the session grant on a real code login. Sessions are created by the production helpers and
/// the code is read back from the mail the dispatch consumer sent. A host with a movable clock stands for time
/// passing; its own process-wide claims start empty.
/// </summary>
[Collection("Api")]
public sealed class AccountActivityEndToEndTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset MinuteOf(DateTimeOffset instant) => AccountActivityClaims.MinuteOf(instant);

    private sealed record Row(DateTimeOffset? LastLoginAt, DateTimeOffset LastActiveAt);

    private string ConnectionString()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
    }

    private async Task<Row?> RowOfAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT a.last_login_at, a.last_active_at
            FROM account_activity a JOIN job_seekers js ON js.id = a.job_seeker_id
            WHERE js.user_id = @user_id
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        if (!await reader.ReadAsync(Ct))
            return null;
        return new Row(
            await reader.IsDBNullAsync(0, Ct) ? null : reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task<long> RowCountAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM account_activity", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>A host on the fixture's databases whose clock the test moves, starting at the fixture's own now.</summary>
    private (WebApplicationFactory<Program> Host, MutableFakeDateTimeProvider Clock) HostWithClock()
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

    private static async Task<HttpStatusCode> MeAsync(HttpClient client, string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        if (sessionId is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await client.SendAsync(request, Ct)).StatusCode;
    }

    private async Task<(Guid UserId, string SessionId)> SignedInAsync(string label)
    {
        var email = Address(NewToken(), label);
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return (await UserIdAsync(factory, email, Ct), sessionId);
    }

    // ── the request path ──

    [Fact]
    public async Task An_authenticated_request_records_its_minute_and_a_later_minute_moves_it()
    {
        var (userId, sessionId) = await SignedInAsync("request");
        var (host, clock) = HostWithClock();
        using (host)
        {
            using var client = host.CreateClient();

            (await MeAsync(client, sessionId)).ShouldBe(HttpStatusCode.OK);
            (await RowOfAsync(userId)).ShouldBe(new Row(null, MinuteOf(clock.UtcNow)));

            clock.UtcNow = clock.UtcNow.AddMinutes(2);
            (await MeAsync(client, sessionId)).ShouldBe(HttpStatusCode.OK);
            (await RowOfAsync(userId)).ShouldBe(new Row(null, MinuteOf(clock.UtcNow)));
        }
    }

    [Fact]
    public async Task Requests_without_a_session_record_nothing()
    {
        await SignedInAsync("bystander");
        var before = await RowCountAsync();
        using var client = factory.CreateClient();

        (await MeAsync(client, null)).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(client, "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")).ShouldBe(HttpStatusCode.Unauthorized); // gitleaks:allow

        (await RowCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_suspended_account_records_nothing_after_its_suspension()
    {
        var (userId, sessionId) = await SignedInAsync("suspended");
        var admin = await AccountEmailChangeKit.AdminAsync(factory, NewToken(), Ct);
        var (host, clock) = HostWithClock();
        using (host)
        {
            using var client = host.CreateClient();
            (await MeAsync(client, sessionId)).ShouldBe(HttpStatusCode.OK);
            var recorded = await RowOfAsync(userId);

            var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
            var suspended = await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/accounts/{userId}/suspend", new { reauthGrant = grant }, Ct);
            suspended.StatusCode.ShouldBe(HttpStatusCode.OK, await suspended.Content.ReadAsStringAsync(Ct));

            clock.UtcNow = clock.UtcNow.AddMinutes(2);
            (await MeAsync(client, sessionId)).ShouldBe(HttpStatusCode.Unauthorized);
            (await RowOfAsync(userId)).ShouldBe(recorded);
        }
    }

    [Fact]
    public async Task A_write_held_past_its_budget_still_answers_the_request()
    {
        var (userId, sessionId) = await SignedInAsync("held");
        await using var holder = new NpgsqlConnection(ConnectionString());
        await holder.OpenAsync(Ct);
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await using (var lockTable = new NpgsqlCommand("LOCK TABLE account_activity IN ACCESS EXCLUSIVE MODE", holder, transaction))
            await lockTable.ExecuteNonQueryAsync(Ct);
        using var client = factory.CreateClient();

        var status = await MeAsync(client, sessionId);

        await transaction.RollbackAsync(Ct);
        status.ShouldBe(HttpStatusCode.OK);
        (await RowOfAsync(userId)).ShouldBeNull();
    }

    // ── the login, through the grant ──

    private async Task<(string ChallengeId, string Code)> MintCodeAsync(HttpClient client, string email)
    {
        var before = factory.Emails.LoginChallenges.Count(m => m.ToEmail == email);
        var response = await client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (factory.Emails.LoginChallenges.Count(m => m.ToEmail == email) == before)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        var mail = factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList()[^1];
        return (body.RootElement.GetProperty("challengeId").GetString()!,
            ((LoginChallengeEmail.CodeAndLink)mail.Content).Code.Reveal());
    }

    [Fact]
    public async Task A_code_login_records_its_minute_as_both_the_login_and_the_activity()
    {
        var email = Address(NewToken(), "code-login");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        var userId = await UserIdAsync(factory, email, Ct);
        using var client = factory.CreateClient();
        var (challengeId, code) = await MintCodeAsync(client, email);
        var before = MinuteOf(DateTimeOffset.UtcNow);

        var verified = await client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code }, Ct);

        var after = MinuteOf(DateTimeOffset.UtcNow);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(Ct));
        var row = (await RowOfAsync(userId)).ShouldNotBeNull();
        row.LastLoginAt.ShouldNotBeNull().ShouldBeInRange(before, after);
        row.LastActiveAt.ShouldBe(row.LastLoginAt.Value);
    }

    [Fact]
    public async Task A_wrong_code_records_no_login()
    {
        var email = Address(NewToken(), "wrong-code");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        var userId = await UserIdAsync(factory, email, Ct);
        using var client = factory.CreateClient();
        var (challengeId, code) = await MintCodeAsync(client, email);
        var wrong = code == "000000" ? "111111" : "000000";

        var verified = await client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code = wrong }, Ct);

        verified.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RowOfAsync(userId)).ShouldBeNull();
    }
}
