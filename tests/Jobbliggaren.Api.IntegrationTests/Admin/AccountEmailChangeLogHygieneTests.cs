using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1975 (security-auditor T-11, ADR 0151 D6's form) — on one host, across an admitted request, a request whose code
/// mail is refused, a malformed and a wrong presentation, a completion, and a completion whose teardown fails, no log
/// record carries an address, a code or a grant in any letter case: not its message, its state, its exception or its
/// scopes. The capture is first shown to see the completion's own request scope, so an absence here can fail.
/// </summary>
[Collection("Api")]
public sealed class AccountEmailChangeLogHygieneTests(ApiFactory factory)
{
    /// <summary>In every address the flow is given, so one search finds any of them.</summary>
    private const string Sentinel = "logsentinel1975";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _token = AdminAccountsKit.NewToken();

    private string Address(string label) => AdminAccountsKit.Address(_token, $"{label}.{Sentinel}");

    [Fact]
    public async Task No_address_code_or_grant_reaches_a_log_record_anywhere_in_the_flow()
    {
        var host = factory.GetRegistrationsClosedHost();
        var anonymous = host.CreateClient();
        var (adminClient, _, adminSession) = await AdminAccountsKit.AdminAsync(host, _token, Ct);
        var adminEmail = AdminAccountsKit.Address(_token, "admin");
        var grants = new List<string>();
        var codes = new List<string>();

        var owner = Address("agare");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(host, owner, ct: Ct);
        var ownerId = await AdminAccountsKit.UserIdAsync(host, owner, Ct);
        var newEmail = Address("ny");
        grants.Add(await ReauthTestHelpers.MintGrantAsync(factory, adminClient, adminSession, adminEmail, Ct));
        (await adminClient.PostAsJsonAsync(AccountEmailChangeKit.Path(ownerId), new { newEmail, reauthGrant = grants[^1] }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        codes.Add(factory.Emails.LoginChallenges.Last(mail => mail.ToEmail == newEmail).Content
            .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>().Code.Reveal());

        // A second administrator, because the first one's next code waits out the re-authentication cooldown.
        var token = AdminAccountsKit.NewToken();
        var (otherAdminClient, _, otherAdminSession) = await AdminAccountsKit.AdminAsync(host, token, Ct);
        var otherId = await AdminAccountsKit.OpenActiveAsync(host, Address("annan"), Ct);
        grants.Add(await ReauthTestHelpers.MintGrantAsync(
            factory, otherAdminClient, otherAdminSession, AdminAccountsKit.Address(token, "admin"), Ct));
        using (factory.Emails.Refusing(RecordedEmailKind.LoginChallenge))
        {
            (await otherAdminClient.PostAsJsonAsync(
                    AccountEmailChangeKit.Path(otherId), new { newEmail = Address("nyare"), reauthGrant = grants[^1] }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, ownerId, newEmail, owner, Ct);
        codes.Add(change.Code.Reveal());
        (await AccountEmailChangeKit.CompleteAsync(anonymous, owner, newEmail, "12345", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await AccountEmailChangeKit.CompleteAsync(anonymous, owner, newEmail, Other(codes[^1]), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Gone);
        (await AccountEmailChangeKit.CompleteAsync(anonymous, owner.ToUpperInvariant(), newEmail, codes[^1], Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var second = Address("andra");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(host, second, ct: Ct);
        var secondId = await AdminAccountsKit.UserIdAsync(host, second, Ct);
        var secondNew = Address("andrany");
        var secondChange = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, secondId, secondNew, second, Ct);
        codes.Add(secondChange.Code.Reveal());
        using (factory.SessionTeardownFaults.FailingFor(secondId))
        {
            (await AccountEmailChangeKit.CompleteAsync(anonymous, second, secondNew, codes[^1], Ct)).StatusCode
                .ShouldBe(HttpStatusCode.InternalServerError);
        }

        var logs = factory.ClosedHostLogs.ToList();
        logs.ShouldContain(log => log.Scopes.Any(scope => scope.Contains(AccountEmailChangeKit.CompletePath, StringComparison.Ordinal)));
        logs.ShouldContain(log => log.EventId.Id == 2060 && log.State.Contains($"TargetUserId={secondId}"));

        Carrying(logs, text => text.Contains(Sentinel, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty("an address");
        foreach (var grant in grants)
            Carrying(logs, text => text.Contains(grant, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty("a grant");
        // A code is six digits, so it is looked for as a token of its own and not inside an id or a longer number.
        foreach (var code in codes)
            Carrying(logs, text => Regex.IsMatch(text, $"(?<![0-9A-Fa-f]){code}(?![0-9A-Fa-f])")).ShouldBeEmpty("a code");
    }

    private static string[] Carrying(List<CapturedLog> logs, Func<string, bool> carries) =>
        [.. logs.Where(log => carries(log.AllText)).Select(log => $"{log.Category}: {log.Message}")];

    private static string Other(string code) => code == "000000" ? "111111" : "000000";
}
