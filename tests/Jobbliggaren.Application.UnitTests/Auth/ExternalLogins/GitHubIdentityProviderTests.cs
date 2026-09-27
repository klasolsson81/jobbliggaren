using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.ExternalLogins;

/// <summary>
/// #1745 (ADR 0142 D8, Amendment (16)) — the GitHub adapter against <see cref="ScriptedGitHub"/>: the authorization
/// URL, the token exchange, the <c>/user</c> and <c>/user/emails</c> reads, the address rule, the revocation, the
/// failure answers and what reaches the log. Row names follow the 6b form round's table (test-writer §3.2, as amended
/// by the reading §2). GitHub's address is always the ASSERTED strength: the flag records that someone once proved
/// the inbox, and a code must prove it now. A shape GitHub does not document is declared as such and asserts only
/// that the adapter refuses it.
/// </summary>
public sealed class GitHubIdentityProviderTests : IDisposable
{
    private const string ClientId = "Iv23-scripted-client-id";

    // Not shaped like a real secret; asserted never to reach the log. gitleaks:allow
    private const string ClientSecret = "test-github-client-secret"; // gitleaks:allow

    // Long and distinctive, so it cannot collide with a status or a count in the log surface.
    private const long Id = 58323117;
    private const string Login = "anna-berg-gh";
    private const string Primary = "anna@firma.example";
    private const string Code = "scripted-github-code";

    private static readonly Uri SiteBase = new("https://jobbliggaren.example");
    private const string RedirectUri = "https://jobbliggaren.example/api/auth/oauth/github/callback";
    private const string RevocationEndpoint = $"https://api.github.com/applications/{ClientId}/token";

    private readonly ScriptedGitHub _github = new(ClientId, ClientSecret);
    private readonly RecordingLogger<GitHubIdentityProvider> _logger = new();
    private readonly PkceVerifier _verifier = PkceVerifier.Generate();

    private GitHubIdentityProvider CreateSut(Uri? siteBase = null) =>
        new(
            new NamedClientFactory(GitHubIdentityProvider.HttpClientName, _github),
            Options.Create(new GitHubOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }),
            new ExternalLoginCallbacks(siteBase ?? SiteBase),
            _logger);

    public void Dispose() => _github.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string User(long id = Id, string? publicEmail = null) => GitHubApiShapes.User(id, Login, publicEmail);

    private Task<ExternalExchange> ExchangeAsync(string userJson, string emailsJson, CancellationToken? ct = null)
    {
        _github.Expect(Code, userJson, emailsJson, _verifier.ToChallenge().Value, RedirectUri);
        return CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, ct ?? Ct);
    }

    private Task<ExternalExchange> ExchangeUnscriptedAsync() =>
        CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct);

    private Task<ExternalExchange> ExchangeWithEmailsAsync(string emailsJson) => ExchangeAsync(User(), emailsJson);

    private static AssertedEmail AssertedAddress(ExternalExchange exchange) =>
        exchange.ShouldBeOfType<ExternalExchange.Identified>().Identity.Address
            .ShouldBeOfType<ExternalAddress.Asserted>().Email;

    private int RequestsTo(string endpoint) =>
        _github.Requests.Count(r => r.Uri.GetLeftPart(UriPartial.Path) == endpoint);

    private bool LoggedCause(int eventId, string cause) =>
        _logger.Records.Any(r => r.EventId.Id == eventId && r.Message.Contains($"Cause={cause}", StringComparison.Ordinal));

    private static string EmailList(params JsonObject[] entries) => new JsonArray([.. entries]).ToJsonString();

    private static JsonObject Entry(string address, JsonNode? primary, JsonNode? verified) =>
        new() { ["email"] = address, ["primary"] = primary, ["verified"] = verified, ["visibility"] = null };

    // ---------- A: the authorization URL ----------

    [Fact]
    public void BuildAuthorizeUrl_ShouldCarryTheS256FlowAndTheEmailScopeAlone_WhenAFlowStarts()
    {
        var state = OAuthState.Generate();
        var challenge = _verifier.ToChallenge();

        var url = CreateSut().BuildAuthorizeUrl(state, challenge);
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://github.com/login/oauth/authorize");
        // Exactly these keys: no response_type (GitHub documents none), no login, no prompt, no allow_signup.
        query.AllKeys.Order(StringComparer.Ordinal).ShouldBe(
            ["client_id", "code_challenge", "code_challenge_method", "redirect_uri", "scope", "state"]);
        query["client_id"].ShouldBe(ClientId);
        query["redirect_uri"].ShouldBe(RedirectUri);
        query["scope"].ShouldBe("user:email");
        query["state"].ShouldBe(state.Reveal());
        query["code_challenge"].ShouldBe(challenge.Value);
        query["code_challenge_method"].ShouldBe("S256");
    }

    [Theory]
    [InlineData("http://localhost:3000", "http://localhost:3000/api/auth/oauth/github/callback")]
    [InlineData("https://dev.jobbliggaren.se/", "https://dev.jobbliggaren.se/api/auth/oauth/github/callback")]
    public void BuildAuthorizeUrl_ShouldBuildTheRedirectFromTheSiteBase_WhenTheBaseHasOrLacksATrailingSlash(
        string siteBase, string expected)
    {
        var url = CreateSut(new Uri(siteBase)).BuildAuthorizeUrl(OAuthState.Generate(), _verifier.ToChallenge());

        HttpUtility.ParseQueryString(url.Query)["redirect_uri"].ShouldBe(expected);
    }

    [Fact]
    public void Key_ShouldBeGitHub_WhenRead() => CreateSut().Key.ShouldBe(ExternalProviderKey.GitHub);

    [Fact]
    public void HttpClientName_ShouldBeItsOwnAndNotGooglesName_WhenRead()
    {
        // A form pin (test-writer Minor 14): the two names share one configuration today, so a misspelling to
        // Google's would change no behaviour; the scripted factory also refuses any other name.
        GitHubIdentityProvider.HttpClientName.ShouldBe("github-oauth");
        GitHubIdentityProvider.HttpClientName.ShouldNotBe(GoogleIdentityProvider.HttpClientName);
    }

    // ---------- T: the token exchange ----------

    [Fact]
    public async Task ExchangeAsync_ShouldPostTheCodeAndVerifierInAFormAndAskForJson_WhenGitHubAccepts()
    {
        (await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Identified>();

        var token = _github.Requests[0];
        token.Method.ShouldBe(HttpMethod.Post);
        token.Uri.AbsoluteUri.ShouldBe(ScriptedGitHub.TokenEndpoint);
        token.Uri.Query.ShouldBeEmpty();
        // No grant_type: GitHub documents none.
        token.Form.Keys.Order(StringComparer.Ordinal).ShouldBe(
            ["client_id", "client_secret", "code", "code_verifier", "redirect_uri"]);
        token.Form["client_id"].ShouldBe(ClientId);
        token.Form["client_secret"].ShouldBe(ClientSecret);
        token.Form["code"].ShouldBe(Code);
        token.Form["code_verifier"].ShouldBe(_verifier.Reveal());
        token.Form["redirect_uri"].ShouldBe(RedirectUri);
        // Without it GitHub answers form-encoded, and the scripted endpoint does the same.
        token.Accept.ShouldContain("application/json");
        token.Authorization.ShouldBeNull();
    }

    public static TheoryData<string, string> DocumentedTokenErrors => new()
    {
        { "bad_verification_code", "TokenBadVerificationCode" },
        { "incorrect_client_credentials", "TokenIncorrectClientCredentials" },
        { "redirect_uri_mismatch", "TokenRedirectUriMismatch" },
        // Not documented: any other error GitHub may add is the general class.
        { "scripted_new_error", "TokenRefused" },
    };

    [Theory]
    [MemberData(nameof(DocumentedTokenErrors))]
    public async Task ExchangeAsync_ShouldFailAfterOneRequestWithItsOwnCause_WhenGitHubRefusesTheCodeWithASuccessStatus(
        string error, string cause)
    {
        // GitHub answers a refused code with 200 and an `error` member (dotnet-architect V2: one cause per error).
        _github.TokenError = error;

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        _github.Requests.Count.ShouldBe(1);
        _github.Revoked.ShouldBeEmpty();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains($"Cause={cause}")
                                           && r.Message.Contains("Status=200") && r.Message.Contains("Provider=github"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldReadTheClosedErrorUnderAFailureStatus_WhenACodeIsRefused()
    {
        // DECLARED: GitHub documents no status for these errors, and its practice is 200. Under a failure status the
        // same closed mapping reads `error` alone (security-auditor m-3), so the cause is still named and nothing
        // else of the body is used.
        _github.TokenError = "bad_verification_code";
        _github.TokenErrorStatus = HttpStatusCode.BadRequest;

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        _github.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=TokenBadVerificationCode")
                                           && r.Message.Contains("Status=400"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenUnverifiedUserEmailComesWithAFailureStatus()
    {
        // DECLARED as above: unverified_user_email is the address rule's refusal (400) under either status, so the
        // answer never depends on which status GitHub chose.
        _github.TokenError = "unverified_user_email";
        _github.TokenErrorStatus = HttpStatusCode.BadRequest;

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.AddressRefused>();

        _github.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains("Cause=UnverifiedAtToken"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseTheAddressWithoutReadingTheUser_WhenGitHubSaysThePrimaryIsUnverified()
    {
        // The reachable form of "an unverified primary" (test-writer Major 1): GitHub issues no token for a user whose
        // primary address is unverified ("Troubleshooting OAuth app access token request errors", read 2026-09-26).
        // senior-cto-advisor point 2: the address rule's refusal, answered 400, never the exchange's failure.
        _github.TokenError = "unverified_user_email";

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.AddressRefused>();

        _github.Requests.ShouldHaveSingleItem().Uri.AbsoluteUri.ShouldBe(ScriptedGitHub.TokenEndpoint);
        _github.Revoked.ShouldBeEmpty();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains("Cause=UnverifiedAtToken")
                                           && r.Message.Contains("Provider=github"));
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1022);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFailAndUseNoToken_WhenTheBodyCarriesBothAnErrorAndAToken()
    {
        // DECLARED: a body GitHub does not document. The error wins, and the token beside it is never used.
        _github.TokenBody = """{"error":"bad_verification_code","access_token":"scripted-access-token-1"}""";

        (await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Failed>();

        _github.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "TokenBadVerificationCode").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFailAfterOneRequest_WhenTheTokenEndpointFails()
    {
        _github.TokenStatus = HttpStatusCode.InternalServerError;

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        _github.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=TokenRefused")
                                           && r.Message.Contains("Status=500"));
    }

    [Theory]
    [InlineData("<html>not json</html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"access_token":""}""")]
    [InlineData("""{"access_token":5}""")]
    [InlineData("""{"access_token":null}""")]
    [InlineData("access_token=scripted&scope=user%3Aemail&token_type=bearer")]
    public async Task ExchangeAsync_ShouldFailAsMalformed_WhenTheTokenBodyIsNotGitHubsJsonShape(string body)
    {
        // DECLARED: not a shape GitHub answers a JSON request with; a middlebox or a captive portal can produce it.
        // The last row is GitHub's own answer to a request that forgot Accept: application/json.
        _github.TokenBody = body;

        (await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Failed>();

        _github.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "Malformed").ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExchangeAsync_ShouldIdentifyAndNeverSendTheRefreshToken_WhetherOrNotTheTokenExpires(bool nonExpiring)
    {
        // Expiring tokens are the default only for apps created from 2026-08-14 (security-auditor, 6b form round);
        // both forms are run and neither is claimed as the default.
        _github.NonExpiring = nonExpiring;

        (await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Identified>();

        foreach (var request in _github.Requests.Skip(1))
        {
            (request.Authorization ?? string.Empty).ShouldNotContain("scripted-refresh-token");
            (request.JsonBody ?? string.Empty).ShouldNotContain("scripted-refresh-token");
        }

        _logger.Records.ShouldAllBe(r => !r.Message.Contains("scripted-refresh-token"));
    }

    // ---------- S: /user, the subject ----------

    [Fact]
    public async Task ExchangeAsync_ShouldReadTheNumericIdAsTheSubject_AndNeverTheLogin()
    {
        var exchange = await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary));

        var identity = exchange.ShouldBeOfType<ExternalExchange.Identified>().Identity;
        identity.Provider.ShouldBe(ExternalProviderKey.GitHub);
        identity.Subject.Reveal().ShouldBe("58323117");

        var user = _github.Requests[1];
        user.Method.ShouldBe(HttpMethod.Get);
        user.Uri.AbsoluteUri.ShouldBe(ScriptedGitHub.UserEndpoint);
        user.Authorization.ShouldBe("Bearer scripted-access-token-1");
        user.UserAgent.ShouldBe("Jobbliggaren");
        user.Accept.ShouldBe("application/vnd.github+json");
        user.ApiVersion.ShouldBe("2026-03-10");
    }

    public static TheoryData<string> UnusableUsers => new()
    {
        """{"login":"anna-berg-gh"}""",
        """{"login":"anna-berg-gh","id":null}""",
        """{"login":"anna-berg-gh","id":"58323117"}""",
        """{"login":"anna-berg-gh","id":0}""",
        """{"login":"anna-berg-gh","id":-1}""",
        """{"login":"anna-berg-gh","id":1.5}""",
        """{"login":"anna-berg-gh","id":5.8e5}""",
        """{"login":"anna-berg-gh","id":1e3}""",
        """{"login":"anna-berg-gh","id":9223372036854775808}""",
        """{"login":"anna-berg-gh","id":true}""",
        """{"login":"anna-berg-gh","id":{}}""",
        "[]",
        "\"x\"",
    };

    [Theory]
    [MemberData(nameof(UnusableUsers))]
    public async Task ExchangeAsync_ShouldFailAndNeverReadTheEmails_WhenTheIdIsNotAPositiveInteger(string userJson)
    {
        // DECLARED: GitHub documents `id` as a positive integer. Only the refusal is asserted.
        (await ExchangeAsync(userJson, GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Failed>();

        RequestsTo(ScriptedGitHub.EmailsEndpoint).ShouldBe(0);
        LoggedCause(1022, "SubjectUnusable").ShouldBeTrue();
    }

    [Theory]
    [InlineData(9007199254740993, "9007199254740993")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    public async Task ExchangeAsync_ShouldKeepEveryDigitOfTheId_WhenItIsBeyondWhatADoubleHolds(long id, string expected)
    {
        // DECLARED boundary: 2^53 + 1 is not a double, so a read through double would change the stored key.
        var exchange = await ExchangeAsync(User(id), GitHubApiShapes.Emails.PrimaryVerified(Primary));

        exchange.ShouldBeOfType<ExternalExchange.Identified>().Identity.Subject.Reveal().ShouldBe(expected);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ExchangeAsync_ShouldFailAndNeverReadTheEmails_WhenUserRefusesTheToken(HttpStatusCode status)
    {
        _github.UserStatus = status;

        (await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Failed>();

        RequestsTo(ScriptedGitHub.UserEndpoint).ShouldBe(1);
        RequestsTo(ScriptedGitHub.EmailsEndpoint).ShouldBe(0);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=UserRefused")
                                           && r.Message.Contains($"Status={(int)status}"));
    }

    // ---------- E: /user/emails, the address rule (primary && verified, and nothing else) ----------

    [Fact]
    public async Task ExchangeAsync_ShouldAssertThePrimary_WhenItIsTheOneVerifiedAddress()
    {
        var exchange = await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary));

        AssertedAddress(exchange).Value.ShouldBe(Primary);

        var emails = _github.Requests[2];
        emails.Method.ShouldBe(HttpMethod.Get);
        emails.Uri.GetLeftPart(UriPartial.Path).ShouldBe(ScriptedGitHub.EmailsEndpoint);
        emails.Uri.Query.ShouldBe("?per_page=100");
        emails.Authorization.ShouldBe("Bearer scripted-access-token-1");
        emails.UserAgent.ShouldBe("Jobbliggaren");
        emails.Accept.ShouldBe("application/vnd.github+json");
        emails.ApiVersion.ShouldBe("2026-03-10");
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAssertThePrimaryAndNeverTheNoreplyAddress_WhenTheNoreplyEntryComesFirst()
    {
        var exchange = await ExchangeWithEmailsAsync(
            GitHubApiShapes.Emails.PrimaryVerifiedWithNoreply(Primary, Id, Login));

        AssertedAddress(exchange).Value.ShouldBe(Primary);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAssertThePrimary_WhenAnUnverifiedSecondaryComesFirst()
    {
        var exchange = await ExchangeWithEmailsAsync(
            GitHubApiShapes.Emails.PrimaryVerifiedWithUnverifiedSecondary(Primary, "ny@annan.example"));

        AssertedAddress(exchange).Value.ShouldBe(Primary);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAssertThePrimaryAndNeverThePublicProfileAddress()
    {
        var exchange = await ExchangeAsync(
            User(publicEmail: "publik@firma.example"), GitHubApiShapes.Emails.PrimaryVerified(Primary));

        AssertedAddress(exchange).Value.ShouldBe(Primary);
    }

    // DECLARED: GitHub issues no token for a user whose primary is unverified, so a primary answered with
    // verified:false is unreachable (test-writer Major 1). Only the refusal and its cause are asserted.
    [Fact]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenThePrimaryIsNotVerified()
    {
        (await ExchangeWithEmailsAsync(EmailList(Entry(Primary, true, false))))
            .ShouldBeOfType<ExternalExchange.AddressRefused>();

        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains("Cause=False")
                                           && r.Message.Contains("Provider=github"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenOnlyANonPrimaryAddressIsVerified()
    {
        // DECLARED. Kills "the first verified address, primary or not".
        (await ExchangeWithEmailsAsync(EmailList(Entry(Primary, true, false), Entry("b@firma.example", false, true))))
            .ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "False").ShouldBeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"email":"b@firma.example","primary":false,"verified":true,"visibility":null}]""")]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenNoEntryIsPrimary(string emailsJson)
    {
        // DECLARED: a token implies a verified primary. Kills "primary || verified".
        (await ExchangeWithEmailsAsync(emailsJson)).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "NoPrimary").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenTwoEntriesArePrimary()
    {
        // DECLARED. Kills both "the first primary" and "the last primary".
        (await ExchangeWithEmailsAsync(
                EmailList(Entry(Primary, true, true), Entry("b@firma.example", true, true))))
            .ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "PrimaryAmbiguous").ShouldBeTrue();
    }

    public static TheoryData<string> FlagsThatAreNotBooleans => new()
    {
        """[{"email":"anna@firma.example","primary":true,"verified":"true"}]""",
        """[{"email":"anna@firma.example","primary":true,"verified":"false"}]""",
        """[{"email":"anna@firma.example","primary":true,"verified":1}]""",
        """[{"email":"anna@firma.example","primary":true,"verified":null}]""",
        """[{"email":"anna@firma.example","primary":true}]""",
        """[{"email":"anna@firma.example","primary":"true","verified":true}]""",
        """[{"email":"anna@firma.example","Primary":true,"verified":true}]""",
        """[{"email":"b@firma.example","primary":"false","verified":true},{"email":"anna@firma.example","primary":true,"verified":true}]""",
    };

    [Theory]
    [MemberData(nameof(FlagsThatAreNotBooleans))]
    public async Task ExchangeAsync_ShouldRefuseTheWholeList_WhenAFlagIsNotAJsonBoolean(string emailsJson)
    {
        // DECLARED: both flags are required JSON booleans. The string "false" kills "anything not false is true".
        (await ExchangeWithEmailsAsync(emailsJson)).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "NotBoolean").ShouldBeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"message":"x"}""")]
    [InlineData("\"x\"")]
    [InlineData("5")]
    [InlineData("""["anna@firma.example"]""")]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenTheListIsNotAnArrayOfObjects(string emailsJson)
    {
        // DECLARED: not from GitHub; a middlebox can produce it. dotnet-architect V4: refused (400), not failed.
        (await ExchangeWithEmailsAsync(emailsJson)).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "ListMalformed").ShouldBeTrue();
    }

    public static TheoryData<string> UnparsablePrimaries => new()
    {
        """[{"primary":true,"verified":true}]""",
        """[{"email":5,"primary":true,"verified":true}]""",
        """[{"email":"anna","primary":true,"verified":true}]""",
        """[{"email":"anna@firma@example","primary":true,"verified":true}]""",
        """[{"email":"anna berg@firma.example","primary":true,"verified":true}]""",
        """[{"email":"anna\u0007berg@firma.example","primary":true,"verified":true}]""",
        $$"""[{"email":"{{new string('a', EmailAddressRules.MaximumLength - "@x.example".Length + 1)}}@x.example","primary":true,"verified":true}]""",
    };

    [Theory]
    [MemberData(nameof(UnparsablePrimaries))]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenThePrimaryIsNotOneStorableAddressWithinTheBound(
        string emailsJson)
    {
        // DECLARED, as Google's rows are: missing, not a string, one part, two '@', a space, a control character,
        // one character over the bound every stored address meets.
        (await ExchangeWithEmailsAsync(emailsJson)).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "AddressUnparsable").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldReadOnePageAndRefuse_WhenThePrimaryIsBeyondIt()
    {
        // DECLARED: GitHub documents neither direction for a primary past the first page. Pagination is not
        // followed, so a primary on page two is refused like a missing one, after exactly one read.
        var entries = Enumerable.Range(1, 100).Select(i => Entry($"a{i}@firma.example", false, true))
            .Append(Entry(Primary, true, true))
            .ToArray();

        (await ExchangeWithEmailsAsync(EmailList(entries))).ShouldBeOfType<ExternalExchange.AddressRefused>();

        RequestsTo(ScriptedGitHub.EmailsEndpoint).ShouldBe(1);
        LoggedCause(1023, "NoPrimary").ShouldBeTrue();
    }

    [Theory]
    [InlineData("58323117+anna-berg-gh@users.noreply.github.com")]
    [InlineData("anna@USERS.NOREPLY.GITHUB.COM")]
    [InlineData("anna@Users.NoReply.GitHub.com")]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenThePrimaryIsGitHubsNoreplyDomain(string noreply)
    {
        // senior-cto-advisor, sa m-1: the domain after the last '@', compared case-blind, has its own cause; an
        // account born on it would have no inbox. GitHub documents no noreply primary, so this is declared.
        (await ExchangeWithEmailsAsync(EmailList(Entry(noreply, true, true))))
            .ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "NotAMailbox").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAssertAnAddressThatOnlyContainsTheNoreplyDomain()
    {
        // Kills a Contains or an EndsWith over the whole address: this domain is not GitHub's.
        const string lookalike = "anna@users.noreply.github.com.evil.example";

        AssertedAddress(await ExchangeWithEmailsAsync(EmailList(Entry(lookalike, true, true))))
            .Value.ShouldBe(lookalike);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ExchangeAsync_ShouldFail_WhenTheEmailsEndpointRefuses(HttpStatusCode status)
    {
        // dotnet-architect V3: a refused list is the exchange's failure (410), not the address rule's.
        _github.EmailsStatus = status;

        (await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary)))
            .ShouldBeOfType<ExternalExchange.Failed>();

        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=EmailsRefused")
                                           && r.Message.Contains($"Status={(int)status}"));
    }

    // ---------- F: order, one request each, cancellation, revocation ----------

    [Fact]
    public async Task ExchangeAsync_ShouldSendOneRequestPerEndpointInOrderAndRevokeLast_WhenGitHubAccepts()
    {
        await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary));

        _github.Requests.Select(r => $"{r.Method} {r.Uri.GetLeftPart(UriPartial.Path)}").ShouldBe(
        [
            $"POST {ScriptedGitHub.TokenEndpoint}",
            $"GET {ScriptedGitHub.UserEndpoint}",
            $"GET {ScriptedGitHub.EmailsEndpoint}",
            $"DELETE {RevocationEndpoint}",
        ]);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRevokeTheTokenWithTheAppsBasicCredentials_WhenTheReadsAreDone()
    {
        // sa m-2 in dotnet-architect V3's form: DELETE /applications/{client_id}/token, Basic, a JSON body.
        await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary));

        var revocation = _github.Requests[^1];
        revocation.Method.ShouldBe(HttpMethod.Delete);
        revocation.Uri.AbsoluteUri.ShouldBe(RevocationEndpoint);
        revocation.Authorization.ShouldBe(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}")));
        JsonNode.Parse(revocation.JsonBody!)!.AsObject().Select(p => p.Key).ShouldBe(["access_token"]);
        _github.Revoked.ShouldBe(["scripted-access-token-1"]);
    }

    public static TheoryData<string> PathsThatHoldAToken => new()
    {
        "user refused", "subject unusable", "emails refused", "address refused",
    };

    [Theory]
    [MemberData(nameof(PathsThatHoldAToken))]
    public async Task ExchangeAsync_ShouldRevokeTheToken_OnEveryPathThatHoldsOne(string path)
    {
        var user = User();
        var emails = GitHubApiShapes.Emails.PrimaryVerified(Primary);
        switch (path)
        {
            case "user refused":
                _github.UserStatus = HttpStatusCode.Unauthorized;
                break;
            case "subject unusable":
                user = """{"login":"anna-berg-gh","id":0}""";
                break;
            case "emails refused":
                _github.EmailsStatus = HttpStatusCode.Forbidden;
                break;
            case "address refused":
                emails = EmailList(Entry(Primary, true, false));
                break;
        }

        await ExchangeAsync(user, emails);

        _github.Revoked.ShouldBe(["scripted-access-token-1"]);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRevokeNothing_WhenNoTokenWasIssued()
    {
        _github.TokenError = "bad_verification_code";

        await ExchangeUnscriptedAsync();

        RequestsTo(RevocationEndpoint).ShouldBe(0);
    }

    public static TheoryData<string> RevocationFailures => new() { "refused", "transport", "timeout" };

    [Theory]
    [MemberData(nameof(RevocationFailures))]
    public async Task ExchangeAsync_ShouldKeepTheOutcomeAndLogOneCause_WhenTheRevocationFails(string failure)
    {
        // The outcome never depends on the revocation: nothing stored the token.
        var cause = failure switch
        {
            "refused" => "Refused",
            "transport" => "Transport",
            _ => "Timeout",
        };
        switch (failure)
        {
            case "refused":
                _github.RevokeStatus = HttpStatusCode.InternalServerError;
                break;
            case "transport":
                _github.RevokeThrow = new HttpRequestException("scripted");
                break;
            default:
                _github.RevokeThrow = new TaskCanceledException("scripted", new TimeoutException());
                break;
        }

        var exchange = await ExchangeWithEmailsAsync(GitHubApiShapes.Emails.PrimaryVerified(Primary));

        AssertedAddress(exchange).Value.ShouldBe(Primary);
        var line = _logger.Records.Where(r => r.EventId.Id == 1028).ShouldHaveSingleItem();
        line.Message.ShouldContain($"Cause={cause}");
        line.Message.ShouldContain("Provider=github");
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1022 || r.EventId.Id == 1023);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFail_WhenTheTransportFails()
    {
        _github.Throw = new HttpRequestException("scripted");

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        LoggedCause(1022, "Transport").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFail_WhenTheClientTimesOutWithoutTheCallerCancelling()
    {
        _github.Throw = new TaskCanceledException("scripted", new TimeoutException());

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        LoggedCause(1022, "Timeout").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldPropagateTheCancellation_WhenTheCallerCancelsBeforeTheExchange()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, cancelled.Token));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldPropagateTheCancellationAndStillRevoke_WhenTheCallerCancelsMidFlow()
    {
        // Kills an inner catch of TaskCanceledException without its caller-token filter: the caller asks to stop
        // while the email list is read, which is not the client's own timeout. The revocation runs to its own end.
        using var caller = new CancellationTokenSource();
        _github.OnEmails = caller.Cancel;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            ExchangeAsync(User(), GitHubApiShapes.Emails.PrimaryVerified(Primary), caller.Token));

        _github.Revoked.ShouldBe(["scripted-access-token-1"]);
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1022);
    }

    // ---------- L: what reaches the log ----------

    [Fact]
    public async Task ExchangeAsync_ShouldLogNoCredentialNoIdentifierAndNoAddress()
    {
        // A success whose revocation fails, a refused list carrying a secondary and a noreply address, and a refused
        // code with GitHub's description: every line the adapter writes, over one surface.
        _github.RevokeStatus = HttpStatusCode.InternalServerError;
        await ExchangeAsync(User(publicEmail: "publik@firma.example"), GitHubApiShapes.Emails.PrimaryVerified(Primary));
        _github.RevokeStatus = null;
        await ExchangeAsync(
            User(),
            EmailList(
                Entry(GitHubApiShapes.NoReplyAddress(Id, Login), false, true),
                Entry("andra@firma.example", false, true),
                Entry(Primary, true, false)));
        _github.TokenError = "bad_verification_code";
        await ExchangeUnscriptedAsync();

        _logger.Records.ShouldNotBeEmpty("otherwise the assertions below are vacuous");
        var surface = string.Join(
            "\n",
            _logger.Records.Select(r =>
                r.Message + "|" + string.Join("|", r.Properties.Select(p => $"{p.Key}={p.Value}"))));

        foreach (var secret in new[]
                 {
                     Code, _verifier.Reveal(), ClientSecret,
                     Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}")),
                     "scripted-access-token", "scripted-refresh-token", Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     Login, Primary, "andra@firma.example", "publik@firma.example", "users.noreply.github.com",
                     ScriptedGitHub.ErrorDescriptionMarker, "docs.github.com",
                 })
        {
            surface.ShouldNotContain(secret);
        }
    }

    [Fact]
    public void Options_ShouldPrintNoPartOfTheSecret_WhenInterpolated() =>
        $"{new GitHubOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }}".ShouldNotContain(ClientSecret);
}
