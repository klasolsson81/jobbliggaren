using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;
using static Jobbliggaren.Api.IntegrationTests.Auth.AccountActivity.AccountActivityKit;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #2161 part 2 (ADR 0172, ADR 0151) — what the directory shows of an account's activity row: both minutes in the
/// list and the detail for every status that can hold a row, nothing where no writer has run, and the "Senast aktiv"
/// sorts. Every row comes from its production writer: a code login through the session grant, or a request through
/// the middleware. A session the test helpers create stands for one the grant
/// did not issue, such as a session older than the release.
/// </summary>
[Collection("Api")]
public sealed class AdminAccountsActivityTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid UserId, string SessionId)> RegisteredAsync(string email)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return (await UserIdAsync(factory, email, Ct), sessionId);
    }

    private static void ShouldShow(JsonElement shown, ActivityRow row)
    {
        shown.GetProperty("lastLoginAt").GetDateTimeOffset().ShouldBe(row.LastLoginAt.ShouldNotBeNull());
        shown.GetProperty("lastActiveAt").GetDateTimeOffset().ShouldBe(row.LastActiveAt);
    }

    [Fact]
    public async Task A_login_and_a_later_request_show_as_their_own_minutes_in_the_list_and_the_detail()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var email = Address(token, "member");
        var (userId, sessionId) = await RegisteredAsync(email);
        using (var client = factory.CreateClient())
            await CodeLoginAsync(factory, client, email, Ct);
        var (host, clock) = HostWithClock(factory);
        using (host)
        {
            clock.UtcNow = clock.UtcNow.AddMinutes(2);
            using var client = host.CreateClient();
            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
        }
        var row = (await RowOfAsync(factory, userId, Ct)).ShouldNotBeNull();
        row.LastLoginAt.ShouldNotBeNull().ShouldBeLessThan(row.LastActiveAt);

        ShouldShow(Items(await SearchOkAsync(admin, new { address = token }, Ct)).Single(), row);
        ShouldShow(await DetailOkAsync(admin, userId, Ct), row);
    }

    [Fact]
    public async Task Each_minute_is_null_where_its_writer_has_not_run()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        // Opened by the registrar and never seen since: neither writer has run.
        var idle = await OpenActiveAsync(factory, Address(token, "idle"), Ct);
        var missing = await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);
        // A request under a session the grant did not issue records the minute and no login.
        var (seen, sessionId) = await RegisteredAsync(Address(token, "seen"));
        using (var client = factory.CreateClient())
            (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
        (await RowOfAsync(factory, idle, Ct)).ShouldBeNull();
        var seenRow = (await RowOfAsync(factory, seen, Ct)).ShouldNotBeNull();
        seenRow.LastLoginAt.ShouldBeNull();

        var rows = Items(await SearchOkAsync(admin, new { address = token }, Ct)).ToDictionary(Id);
        foreach (var id in new[] { idle, missing, seen })
        {
            foreach (var shown in new[] { rows[id], await DetailOkAsync(admin, id, Ct) })
            {
                shown.GetProperty("lastLoginAt").ValueKind.ShouldBe(JsonValueKind.Null);
                if (id == seen)
                    shown.GetProperty("lastActiveAt").GetDateTimeOffset().ShouldBe(seenRow.LastActiveAt);
                else
                    shown.GetProperty("lastActiveAt").ValueKind.ShouldBe(JsonValueKind.Null);
            }
        }
    }

    [Fact]
    public async Task A_suspended_and_a_pending_deletion_account_keep_the_minutes_recorded_before()
    {
        var token = NewToken();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, NewToken(), Ct);
        var suspendedEmail = Address(token, "suspended");
        var pendingEmail = Address(token, "pending");
        var (suspended, _) = await RegisteredAsync(suspendedEmail);
        var (pending, pendingSession) = await RegisteredAsync(pendingEmail);
        using var client = factory.CreateClient();
        await CodeLoginAsync(factory, client, suspendedEmail, Ct);
        await CodeLoginAsync(factory, client, pendingEmail, Ct);

        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        var suspension = await admin.Client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{suspended}/suspend", new { reauthGrant = grant }, Ct);
        suspension.StatusCode.ShouldBe(HttpStatusCode.OK, await suspension.Content.ReadAsStringAsync(Ct));
        var ownGrant = await ReauthTestHelpers.MintGrantAsync(factory, client, pendingSession, pendingEmail, Ct);
        using (var deletion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/delete"))
        {
            deletion.Content = JsonContent.Create(new { reauthGrant = ownGrant });
            deletion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pendingSession);
            var deleted = await client.SendAsync(deletion, Ct);
            deleted.IsSuccessStatusCode.ShouldBeTrue(await deleted.Content.ReadAsStringAsync(Ct));
        }

        var rows = Items(await SearchOkAsync(admin.Client, new { address = token }, Ct)).ToDictionary(Id);
        foreach (var (id, status) in new[] { (suspended, "Suspended"), (pending, "PendingDeletion") })
        {
            var row = (await RowOfAsync(factory, id, Ct)).ShouldNotBeNull();
            foreach (var shown in new[] { rows[id], await DetailOkAsync(admin.Client, id, Ct) })
            {
                shown.GetProperty("status").GetString().ShouldBe(status);
                ShouldShow(shown, row);
            }
        }
    }

    [Fact]
    public async Task The_last_active_sorts_order_by_the_minute_with_nothing_recorded_last()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var accounts = new Dictionary<string, (Guid UserId, string SessionId)>();
        foreach (var label in new[] { "early", "middle", "late", "tie-a", "tie-b" })
            accounts[label] = await RegisteredAsync(Address(token, label));
        var idle = await OpenActiveAsync(factory, Address(token, "idle"), Ct);
        var (host, clock) = HostWithClock(factory);
        using (host)
        {
            using var client = host.CreateClient();
            var start = clock.UtcNow;
            // The clock is the actor: each account's request falls in a minute of its own, and two share one.
            foreach (var (label, minutes) in new[] { ("early", 1), ("middle", 2), ("late", 3), ("tie-a", 4), ("tie-b", 4) })
            {
                clock.UtcNow = start.AddMinutes(minutes);
                (await MeAsync(client, accounts[label].SessionId, Ct)).ShouldBe(HttpStatusCode.OK);
            }
        }

        // Postgres orders a uuid by its bytes, which is the canonical string's order.
        var tied = new[] { accounts["tie-a"].UserId, accounts["tie-b"].UserId }
            .OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        Items(await SearchOkAsync(admin, new { address = token, sort = "LastActiveNewest" }, Ct)).Select(Id).ShouldBe(
            [.. tied, accounts["late"].UserId, accounts["middle"].UserId, accounts["early"].UserId, idle]);
        Items(await SearchOkAsync(admin, new { address = token, sort = "LastActiveOldest" }, Ct)).Select(Id).ShouldBe(
            [accounts["early"].UserId, accounts["middle"].UserId, accounts["late"].UserId, .. tied, idle]);
    }

    [Fact]
    public async Task The_counts_and_the_overview_read_the_same_before_and_after_accounts_are_seen()
    {
        var token = NewToken();
        var (admin, _, _) = await AdminAsync(factory, NewToken(), Ct);
        var sessions = new List<string>();
        foreach (var label in new[] { "a", "b" })
            sessions.Add((await RegisteredAsync(Address(token, label))).SessionId);
        await OpenActiveAsync(factory, Address(token, "idle"), Ct);
        await CreateWithoutProfileAsync(factory, Address(token, "missing"), Ct);

        var before = await SearchOkAsync(admin, new { address = token }, Ct);
        var overviewBefore = await OverviewCountsAsync(admin);
        using (var client = factory.CreateClient())
        {
            foreach (var sessionId in sessions)
                (await MeAsync(client, sessionId, Ct)).ShouldBe(HttpStatusCode.OK);
        }
        var after = await SearchOkAsync(admin, new { address = token }, Ct);

        after.GetProperty("counts").GetRawText().ShouldBe(before.GetProperty("counts").GetRawText());
        after.GetProperty("accounts").GetProperty("totalCount").GetInt32().ShouldBe(4);
        (await OverviewCountsAsync(admin)).ShouldBe(overviewBefore);
    }

    private static async Task<string> OverviewCountsAsync(HttpClient admin)
    {
        var response = await admin.GetAsync("/api/v1/admin/overview/accounts", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("counts").GetRawText();
    }
}
