using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1975 (ADR 0153) — the administrator's side of an address change, end to end through the real pipeline: who may
/// start one (security-auditor T-1, T-2), which accounts and addresses are refused before anything is mailed or spent
/// (T-3), the two mails in their order with nothing completable when either is refused (T-12), the shared per-address
/// cap (T-13), and the request and cancel rows and nothing else (T-11). Every grant is minted the way production mints
/// one, from the code in the administrator's own inbox.
/// </summary>
[Collection("Api")]
public sealed class AccountEmailChangeRequestTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _token = AdminAccountsKit.NewToken();

    private string Address(string label) => AdminAccountsKit.Address(_token, label);

    private async Task<(AccountEmailChangeKit.Admin Admin, Guid Target, string Current)> AdminAndTargetAsync()
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, _token, Ct);
        var current = Address("agare");
        return (admin, await AdminAccountsKit.OpenActiveAsync(factory, current, Ct), current);
    }

    private List<RecordedEmail> SentTo(params string[] recipients) =>
        [.. factory.Emails.Sent.Where(mail => recipients.Contains(mail.ToEmail))];

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        (await BodyAsync(response)).GetProperty("title").GetString().ShouldBe(code);
    }

    // ── T-1: who may reach the three routes ──

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public async Task An_anonymous_caller_is_401_and_an_account_without_the_role_403(string method)
    {
        var target = await AdminAccountsKit.OpenActiveAsync(factory, Address("agare"), Ct);
        var user = await AdminAccountsKit.UserAsync(factory, _token, Ct);

        foreach (var (client, status) in new[]
                 {
                     (factory.CreateClient(), HttpStatusCode.Unauthorized), (user, HttpStatusCode.Forbidden),
                 })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), AccountEmailChangeKit.Path(target));
            if (method == "POST")
                request.Content = JsonContent.Create(new { newEmail = Address("ny"), reauthGrant = "a-grant" });
            (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(status);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public async Task The_empty_id_is_400_on_all_three_routes(string method)
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, _token, Ct);
        using var request = new HttpRequestMessage(new HttpMethod(method), AccountEmailChangeKit.Path(Guid.Empty));
        if (method == "POST")
            request.Content = JsonContent.Create(new { newEmail = Address("ny"), reauthGrant = "a-grant" });

        (await admin.Client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Emails.Sent.Where(mail => mail.ToEmail == Address("ny")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_administrator_whose_role_was_removed_is_403_on_the_next_request()
    {
        var (admin, target, _) = await AdminAndTargetAsync();
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Unreachable today: nothing in src/ removes the role. Asserted only as the read side's safe degradation.
        await AdminAccountsKit.DemoteAsync(factory, admin.UserId);

        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── T-2: the step-up ──

    [Fact]
    public async Task A_request_without_a_grant_is_400_and_one_with_another_users_grant_401()
    {
        var (admin, target, current) = await AdminAndTargetAsync();
        var other = Address("annan");
        var otherSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, other, ct: Ct);
        var othersGrant = await ReauthTestHelpers.MintGrantAsync(factory, factory.CreateClient(), otherSession, other, Ct);

        (await admin.Client.PostAsJsonAsync(AccountEmailChangeKit.Path(target), new { newEmail = Address("ny") }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldBeProblemAsync(
            await admin.Client.PostAsJsonAsync(
                AccountEmailChangeKit.Path(target), new { newEmail = Address("ny"), reauthGrant = othersGrant }, Ct),
            HttpStatusCode.Unauthorized,
            AuthErrorCodes.InvalidCredentials);
        SentTo(current, Address("ny")).ShouldBeEmpty();
    }

    [Fact]
    public async Task One_grant_serves_one_request()
    {
        var (admin, target, _) = await AdminAndTargetAsync();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        var body = new { newEmail = Address("ny"), reauthGrant = grant };

        (await admin.Client.PostAsJsonAsync(AccountEmailChangeKit.Path(target), body, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await ShouldBeProblemAsync(
            await admin.Client.PostAsJsonAsync(AccountEmailChangeKit.Path(target), body, Ct),
            HttpStatusCode.Unauthorized,
            AuthErrorCodes.InvalidCredentials);
    }

    // ── a started change: the two mails, the answer, the row ──

    [Fact]
    public async Task An_admitted_request_tells_the_current_address_then_mails_the_code_and_answers_both_instants()
    {
        var (admin, target, current) = await AdminAndTargetAsync();
        var newEmail = Address("ny");
        var now = factory.Services.GetRequiredService<Jobbliggaren.Domain.Common.IDateTimeProvider>().UtcNow;

        var response = await AccountEmailChangeKit.RequestAsync(factory, admin, target, newEmail, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        var body = await BodyAsync(response);
        body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(["completableFrom", "expiresAt"]);
        var completableFrom = body.GetProperty("completableFrom").GetDateTimeOffset();
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        completableFrom.ShouldBe(now + TimeSpan.FromHours(72), TimeSpan.FromMinutes(1));
        (expiresAt - completableFrom).ShouldBe(TimeSpan.FromHours(24));

        // The notice is sent first, while the change is not yet completable, and names no address but its recipient's.
        SentTo(current, newEmail).Select(mail => mail.Kind).ShouldBe(
            [RecordedEmailKind.AccountEmailChangeRequestedNotification, RecordedEmailKind.LoginChallenge]);
        factory.Emails.AccountEmailChangeNotices.ShouldContain(
            new RecordedAccountEmailChangeNotice(current, completableFrom, expiresAt));
        var code = factory.Emails.LoginChallenges.Where(mail => mail.ToEmail == newEmail).ShouldHaveSingleItem().Content
            .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>();
        (code.CompletableFrom, code.ExpiresAt).ShouldBe((completableFrom, expiresAt));

        var row = (await AccountEmailChangeKit.AuditRowsAsync(factory, target, Ct)).ShouldHaveSingleItem();
        row.EventType.ShouldBe("Admin.AccountEmailChangeRequested");
        row.UserId.ShouldBe(admin.UserId);
        row.AggregateType.ShouldBe("User");
        var payload = JsonDocument.Parse(row.Payload.ShouldNotBeNull()).RootElement;
        payload.EnumerateObject().Select(property => property.Name).ShouldBe(["requestId"]);
        payload.GetProperty("requestId").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task The_pending_read_shows_the_change_and_a_cancel_removes_it_once()
    {
        var (admin, target, _) = await AdminAndTargetAsync();
        var started = await BodyAsync(await AccountEmailChangeKit.RequestAsync(factory, admin, target, Address("ny"), Ct));

        var pending = await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct);
        pending.StatusCode.ShouldBe(HttpStatusCode.OK);
        pending.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var read = await BodyAsync(pending);
        read.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["completableFrom", "expiresAt", "state"]);
        read.GetProperty("state").GetString().ShouldBe("Pending");
        read.GetProperty("completableFrom").GetDateTimeOffset()
            .ShouldBe(started.GetProperty("completableFrom").GetDateTimeOffset());

        (await admin.Client.DeleteAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        await ShouldBeProblemAsync(
            await admin.Client.DeleteAsync(AccountEmailChangeKit.Path(target), Ct),
            HttpStatusCode.Gone,
            AuthErrorCodes.AccountEmailChangeNothingPending);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        // One row for the request and one for the cancel that removed something; none for the one that did not.
        var rows = await AccountEmailChangeKit.AuditRowsAsync(factory, target, Ct);
        rows.Select(row => row.EventType)
            .ShouldBe(["Admin.AccountEmailChangeRequested", "Admin.AccountEmailChangeCancelled"]);
        rows.ShouldAllBe(row => row.UserId == admin.UserId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_mail_that_is_not_accepted_leaves_nothing_pending_and_no_row(bool theNoticeIsRefused)
    {
        var refused = theNoticeIsRefused
            ? RecordedEmailKind.AccountEmailChangeRequestedNotification
            : RecordedEmailKind.LoginChallenge;
        var (admin, target, _) = await AdminAndTargetAsync();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);

        HttpResponseMessage response;
        using (factory.Emails.Refusing(refused))
        {
            response = await admin.Client.PostAsJsonAsync(
                AccountEmailChangeKit.Path(target), new { newEmail = Address("ny"), reauthGrant = grant }, Ct);
        }

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await AccountEmailChangeKit.AuditRowsAsync(factory, target, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_is_503_and_nothing_is_written()
    {
        var (admin, target, current) = await AdminAndTargetAsync();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);

        HttpResponseMessage response;
        using (factory.Emails.Incapable())
        {
            response = await admin.Client.PostAsJsonAsync(
                AccountEmailChangeKit.Path(target), new { newEmail = Address("ny"), reauthGrant = grant }, Ct);
        }

        await ShouldBeProblemAsync(response, HttpStatusCode.ServiceUnavailable, AuthErrorCodes.EmailDeliveryUnavailable);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SentTo(current, Address("ny")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_volatile_instance_out_of_reach_is_503_for_the_read_and_the_cancel()
    {
        var (admin, target, _) = await AdminAndTargetAsync();

        using (factory.LoginChallengeFaults.Unavailable())
        {
            (await admin.Client.GetAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode
                .ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await admin.Client.DeleteAsync(AccountEmailChangeKit.Path(target), Ct)).StatusCode
                .ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        (await AccountEmailChangeKit.AuditRowsAsync(factory, target, Ct)).ShouldBeEmpty();
    }

    // ── T-3: the accounts and addresses refused before anything is mailed or spent ──

    [Fact]
    public async Task An_administrator_account_is_refused_and_nothing_is_mailed()
    {
        var (admin, _, _) = await AdminAndTargetAsync();
        var otherAdmin = Address("admin2");
        var otherAdminId = await AdminAccountsKit.OpenActiveAsync(factory, otherAdmin, Ct);
        await AccountEmailChangeKit.GrantAdminAsTheRetiredSeederDidAsync(factory, otherAdminId);

        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, otherAdminId, Address("ny"), Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.AccountEmailChangeAdministratorTarget);
        SentTo(otherAdmin, Address("ny")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_administrators_own_account_is_refused()
    {
        var (admin, _, _) = await AdminAndTargetAsync();

        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, admin.UserId, Address("ny"), Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.AccountEmailChangeAdministratorTarget);
        SentTo(Address("ny")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_account_pending_deletion_and_one_without_a_profile_are_refused()
    {
        var (admin, _, _) = await AdminAndTargetAsync();
        var pending = await AdminAccountsKit.CreatePendingDeletionAsync(factory, Address("raderas"), Ct);
        var orphan = await AdminAccountsKit.CreateWithoutProfileAsync(factory, Address("utan"), Ct);

        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, pending, Address("ny1"), Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.AccountEmailChangeInactiveTarget);
        await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, orphan, Address("ny2"), Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.AccountEmailChangeInactiveTarget);
        // The deletion itself mailed a re-authentication code; the refusals mailed nothing.
        SentTo(Address("ny1"), Address("ny2")).ShouldBeEmpty();
        factory.Emails.AccountEmailChangeNotices.ShouldNotContain(
            notice => notice.ToEmail == Address("raderas") || notice.ToEmail == Address("utan"));
    }

    [Fact]
    public async Task The_accounts_own_address_in_another_case_and_another_accounts_address_are_taken()
    {
        var (admin, target, current) = await AdminAndTargetAsync();
        var someoneElses = Address("upptagen");
        await AdminAccountsKit.OpenActiveAsync(factory, someoneElses, Ct);

        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, target, current.ToUpperInvariant(), Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.EmailTaken);
        await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, target, someoneElses, Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.EmailTaken);
        SentTo(current, current.ToUpperInvariant(), someoneElses).ShouldBeEmpty();
        (await AccountEmailChangeKit.AuditRowsAsync(factory, target, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_address_another_accounts_change_holds_is_refused_and_that_change_survives()
    {
        var (admin, first, _) = await AdminAndTargetAsync();
        var second = await AdminAccountsKit.OpenActiveAsync(factory, Address("andra"), Ct);
        var newEmail = Address("ny");
        (await AccountEmailChangeKit.RequestAsync(factory, admin, first, newEmail, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);
        await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        await ReauthTestHelpers.LetTheTargetCooldownLapseAsync(factory, newEmail);

        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, second, newEmail, Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.AccountEmailChangePendingForAnotherAccount);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(first), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── T-13: one cap per new address, whoever asks ──

    [Fact]
    public async Task A_self_service_request_and_an_administrators_share_the_new_addresss_cooldown()
    {
        var (admin, target, _) = await AdminAndTargetAsync();
        var newEmail = Address("delad");
        var owner = Address("sjalv");
        var ownerSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, owner, ct: Ct);
        await ReauthTestHelpers.RequestAddressChangeAsync(factory, factory.CreateClient(), ownerSession, owner, newEmail, Ct);

        await ShouldBeProblemAsync(
            await AccountEmailChangeKit.RequestAsync(factory, admin, target, newEmail, Ct),
            HttpStatusCode.Conflict,
            AuthErrorCodes.ChangeEmailCooldown);
        factory.Emails.AccountEmailChangeNotices.ShouldNotContain(notice => notice.ToEmail == newEmail);
    }

    [Fact]
    public async Task An_administrators_request_spends_the_cooldown_a_self_service_request_then_meets()
    {
        var (admin, target, _) = await AdminAndTargetAsync();
        var newEmail = Address("delad");
        (await AccountEmailChangeKit.RequestAsync(factory, admin, target, newEmail, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);
        var owner = Address("sjalv");
        var ownerSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, owner, ct: Ct);
        var client = factory.CreateClient();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, client, ownerSession, owner, Ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-email")
        {
            Content = JsonContent.Create(new { reauthGrant = grant, newEmail }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerSession);

        await ShouldBeProblemAsync(
            await client.SendAsync(request, Ct), HttpStatusCode.Conflict, AuthErrorCodes.ChangeEmailCooldown);
    }
}
