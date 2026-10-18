using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;
using static Jobbliggaren.Api.IntegrationTests.Auth.AccountActivity.AccountActivityKit;

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

    private async Task<long> RowCountAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString(factory));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM account_activity", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<(Guid UserId, string SessionId)> SignedInAsync(string label)
    {
        var email = Address(NewToken(), label);
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return (await UserIdAsync(factory, email, Ct), sessionId);
    }

    // ── the request path ──

    [Fact]
    public void The_api_host_holds_one_claim_map_for_every_request()
    {
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        first.ServiceProvider.GetRequiredService<AccountActivityClaims>()
            .ShouldBeSameAs(second.ServiceProvider.GetRequiredService<AccountActivityClaims>());
    }

    [Fact]
    public async Task An_authenticated_request_records_its_minute_and_a_later_minute_moves_it()
    {
        var (userId, sessionId) = await SignedInAsync("request");
        var (host, clock) = HostWithClock(factory);
        using (host)
        {
            using var client = host.CreateClient();

            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
            (await RowOfAsync(factory, userId, Ct)).ShouldBe(new ActivityRow(null, MinuteOf(clock.UtcNow)));

            clock.UtcNow = clock.UtcNow.AddMinutes(2);
            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
            (await RowOfAsync(factory, userId, Ct)).ShouldBe(new ActivityRow(null, MinuteOf(clock.UtcNow)));
        }
    }

    [Fact]
    public async Task A_request_the_admin_policy_refuses_still_records_its_minute()
    {
        var (userId, sessionId) = await SignedInAsync("forbidden");
        var (host, clock) = HostWithClock(factory);
        using (host)
        {
            using var client = host.CreateClient();
            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
            clock.UtcNow = clock.UtcNow.AddMinutes(2);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/accounts/{Guid.NewGuid()}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
            (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            (await RowOfAsync(factory, userId, Ct)).ShouldBe(new ActivityRow(null, MinuteOf(clock.UtcNow)));
        }
    }

    [Fact]
    public async Task Requests_without_a_session_record_nothing()
    {
        await SignedInAsync("bystander");
        var before = await RowCountAsync();
        using var client = factory.CreateClient();

        (await MeAsync(client, null, Ct)).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(client, "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", Ct)).ShouldBe(HttpStatusCode.Unauthorized); // gitleaks:allow

        (await RowCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_suspended_account_records_nothing_after_its_suspension()
    {
        var (userId, sessionId) = await SignedInAsync("suspended");
        var admin = await AccountEmailChangeKit.AdminAsync(factory, NewToken(), Ct);
        var (host, clock) = HostWithClock(factory);
        using (host)
        {
            using var client = host.CreateClient();
            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
            var recorded = (await RowOfAsync(factory, userId, Ct)).ShouldNotBeNull();

            var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
            var suspended = await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/accounts/{userId}/suspend", new { reauthGrant = grant }, Ct);
            suspended.StatusCode.ShouldBe(HttpStatusCode.OK, await suspended.Content.ReadAsStringAsync(Ct));

            clock.UtcNow = clock.UtcNow.AddMinutes(2);
            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.Unauthorized);
            (await RowOfAsync(factory, userId, Ct)).ShouldBe(recorded);
        }
    }

    [Fact]
    public async Task A_write_held_past_its_budget_still_answers_the_request()
    {
        var (userId, sessionId) = await SignedInAsync("held");
        await using var holder = new NpgsqlConnection(ConnectionString(factory));
        await holder.OpenAsync(Ct);
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await using (var lockTable = new NpgsqlCommand("LOCK TABLE account_activity IN ACCESS EXCLUSIVE MODE", holder, transaction))
            await lockTable.ExecuteNonQueryAsync(Ct);
        using var client = factory.CreateClient();
        var started = TimeProvider.System.GetTimestamp();

        var status = await MeAsync(client, sessionId, Ct);

        var waited = TimeProvider.System.GetElapsedTime(started);
        await transaction.RollbackAsync(Ct);
        status.ShouldBe(HttpStatusCode.OK);
        waited.ShouldBeGreaterThanOrEqualTo(SqlAccountActivityRecorder.RequestWriteBudget - TimeSpan.FromMilliseconds(100));
        (await RowOfAsync(factory, userId, Ct)).ShouldBeNull();
    }

    // ── the login, through the grant ──

    [Fact]
    public async Task A_code_login_records_its_minute_as_both_the_login_and_the_activity()
    {
        var email = Address(NewToken(), "code-login");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        var userId = await UserIdAsync(factory, email, Ct);
        using var client = factory.CreateClient();
        var (challengeId, code) = await MintCodeAsync(factory, client, email, Ct);
        var before = MinuteOf(DateTimeOffset.UtcNow);

        var verified = await client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code }, Ct);

        var after = MinuteOf(DateTimeOffset.UtcNow);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(Ct));
        var row = (await RowOfAsync(factory, userId, Ct)).ShouldNotBeNull();
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
        var (challengeId, code) = await MintCodeAsync(factory, client, email, Ct);
        var wrong = code == "000000" ? "111111" : "000000";

        var verified = await client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code = wrong }, Ct);

        verified.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RowOfAsync(factory, userId, Ct)).ShouldBeNull();
    }
}
