using System.Net;
using System.Web;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.ExternalLogins;

/// <summary>
/// #1744 (ADR 0142 D8) — the Google adapter against <see cref="ScriptedGoogle"/>: the authorization URL, the token
/// request, the userinfo read, the authority rule, the failure answers and what reaches the log. The rows follow the
/// 6a form round's table (test-writer 11b); a shape Google does not document is declared as such and asserts only
/// that the adapter refuses it.
/// </summary>
public sealed class GoogleIdentityProviderTests : IDisposable
{
    private const string ClientId = "test-client-id.apps.googleusercontent.com";

    // Not shaped like a real secret; asserted never to reach the log. gitleaks:allow
    private const string ClientSecret = "test-google-client-secret"; // gitleaks:allow

    private const string Sub = "110248495921238986420";
    private const string Code = "4/0AVGzR1scripted-code";

    private static readonly Uri SiteBase = new("https://jobbliggaren.example");
    private const string RedirectUri = "https://jobbliggaren.example/api/auth/oauth/google/callback";

    private readonly ScriptedGoogle _google = new();
    private readonly RecordingLogger<GoogleIdentityProvider> _logger = new();
    private readonly PkceVerifier _verifier = PkceVerifier.Generate();

    private GoogleIdentityProvider CreateSut(Uri? siteBase = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GoogleIdentityProvider.HttpClientName)
            .Returns(_ => new HttpClient(_google, disposeHandler: false));

        return new GoogleIdentityProvider(
            factory,
            Options.Create(new GoogleOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }),
            new ExternalLoginCallbacks(siteBase ?? SiteBase),
            _logger);
    }

    public void Dispose() => _google.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ExternalIdentity?> ExchangeAsync(string userInfoJson)
    {
        _google.Expect(Code, userInfoJson, _verifier.ToChallenge().Value, RedirectUri);
        return CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct);
    }

    // ---------- the authorization URL ----------

    [Fact]
    public void BuildAuthorizeUrl_ShouldCarryTheS256FlowAndNoOfflineAccess_WhenAFlowStarts()
    {
        var state = OAuthState.Generate();
        var challenge = _verifier.ToChallenge();

        var url = CreateSut().BuildAuthorizeUrl(state, challenge);
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://accounts.google.com/o/oauth2/v2/auth");
        query.AllKeys.Order(StringComparer.Ordinal).ShouldBe(
        [
            "client_id", "code_challenge", "code_challenge_method", "redirect_uri", "response_type", "scope", "state",
        ]);
        query["client_id"].ShouldBe(ClientId);
        query["redirect_uri"].ShouldBe(RedirectUri);
        query["response_type"].ShouldBe("code");
        query["scope"].ShouldBe("openid email");
        query["state"].ShouldBe(state.Reveal());
        query["code_challenge"].ShouldBe(challenge.Value);
        query["code_challenge_method"].ShouldBe("S256");
    }

    [Theory]
    [InlineData("http://localhost:3000", "http://localhost:3000/api/auth/oauth/google/callback")]
    [InlineData("https://dev.jobbliggaren.se/", "https://dev.jobbliggaren.se/api/auth/oauth/google/callback")]
    public void BuildAuthorizeUrl_ShouldBuildTheRedirectFromTheSiteBase_WhenTheBaseHasOrLacksATrailingSlash(
        string siteBase, string expected)
    {
        var url = CreateSut(new Uri(siteBase)).BuildAuthorizeUrl(OAuthState.Generate(), _verifier.ToChallenge());

        HttpUtility.ParseQueryString(url.Query)["redirect_uri"].ShouldBe(expected);
    }

    // ---------- the exchange ----------

    [Fact]
    public async Task ExchangeAsync_ShouldPostTheCodeAndVerifierInTheBodyAndReadUserInfoWithTheBearer_WhenGoogleAccepts()
    {
        var identity = await ExchangeAsync(GoogleUserInfoShapes.Gmail(Sub, "anna.berg"));

        identity.ShouldNotBeNull();
        _google.Requests.Count.ShouldBe(2);

        var token = _google.Requests[0];
        token.Method.ShouldBe(HttpMethod.Post);
        token.Uri.AbsoluteUri.ShouldBe("https://oauth2.googleapis.com/token");
        token.Uri.Query.ShouldBeEmpty();
        token.Form.Keys.Order(StringComparer.Ordinal).ShouldBe(
            ["client_id", "client_secret", "code", "code_verifier", "grant_type", "redirect_uri"]);
        token.Form["grant_type"].ShouldBe("authorization_code");
        token.Form["code"].ShouldBe(Code);
        token.Form["code_verifier"].ShouldBe(_verifier.Reveal());
        token.Form["client_secret"].ShouldBe(ClientSecret);
        token.Form["redirect_uri"].ShouldBe(RedirectUri);
        token.Authorization.ShouldBeNull();

        var userInfo = _google.Requests[1];
        userInfo.Method.ShouldBe(HttpMethod.Get);
        userInfo.Uri.AbsoluteUri.ShouldBe("https://openidconnect.googleapis.com/v1/userinfo");
        userInfo.Authorization.ShouldStartWith("Bearer scripted-access-token-");
    }

    [Fact]
    public async Task ExchangeAsync_ShouldVerifyTheAddress_WhenTheAccountIsGmail()
    {
        var identity = await ExchangeAsync(GoogleUserInfoShapes.Gmail(Sub, "anna.berg"));

        identity!.Provider.ShouldBe(ExternalProviderKey.Google);
        identity.Subject.Reveal().ShouldBe(Sub);
        identity.Email!.Value.Value.ShouldBe("anna.berg@gmail.com");
    }

    [Fact]
    public async Task ExchangeAsync_ShouldVerifyTheAddress_WhenTheAccountIsWorkspace()
    {
        var identity = await ExchangeAsync(
            GoogleUserInfoShapes.Workspace(Sub, "anna@firma.example", hostedDomain: "firma.example"));

        identity!.Email!.Value.Value.ShouldBe("anna@firma.example");
    }

    [Fact]
    public async Task ExchangeAsync_ShouldKeepTheIdentityButNotTheAddress_WhenGoogleIsNotAuthoritativeForIt()
    {
        var identity = await ExchangeAsync(GoogleUserInfoShapes.ThirdPartyVerified(Sub, "anna@outlook.example"));

        identity!.Subject.Reveal().ShouldBe(Sub);
        identity.Email.ShouldBeNull();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains("NotAuthoritative"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldNotVerifyTheAddress_WhenGoogleHasNotVerifiedIt()
    {
        var identity = await ExchangeAsync(GoogleUserInfoShapes.Unverified(Sub, "anna.berg@gmail.com"));

        identity!.Email.ShouldBeNull();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains("False"));
    }

    [Theory]
    [InlineData("anna@gmail.com.evil.example")]
    [InlineData("anna@notgmail.com")]
    [InlineData("anna@evilgmail.com")]
    [InlineData("anna@googlemail.com")]
    public async Task ExchangeAsync_ShouldNotVerifyTheAddress_WhenItOnlyLooksLikeGmail(string address)
    {
        // Reachable: anyone can open a Google account on an address they hold. Google's rule names gmail.com alone.
        var identity = await ExchangeAsync(GoogleUserInfoShapes.ThirdPartyVerified(Sub, address));

        identity!.Email.ShouldBeNull();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldTolerateClaimsItDoesNotRead_WhenGoogleSendsTheProfile()
    {
        // The documented shapes carry name, picture and the given/family names; the adapter reads none of them.
        var identity = await ExchangeAsync(GoogleUserInfoShapes.Gmail(Sub, "anna.berg"));

        identity!.ToString().ShouldNotContain("Test Person");
    }

    // ---------- declared unreachable: shapes Google does not document; only the refusal is asserted ----------

    [Theory]
    [InlineData("""{"sub":"110248495921238986420","email":"anna@gmail.com","email_verified":"true"}""", "NotBoolean")]
    [InlineData("""{"sub":"110248495921238986420","email":"anna@gmail.com"}""", "NotBoolean")]
    [InlineData("""{"sub":"110248495921238986420","email":"anna@firma.example","email_verified":true,"hd":""}""", "NotAuthoritative")]
    [InlineData("""{"sub":"110248495921238986420","email":"ANNA@GMAIL.COM","email_verified":true}""", "NotAuthoritative")]
    [InlineData("""{"sub":"110248495921238986420","email_verified":true}""", "AddressUnparsable")]
    [InlineData("""{"sub":"110248495921238986420","email":"anna","email_verified":true,"hd":"firma.example"}""", "AddressUnparsable")]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenTheShapeIsOneGoogleDoesNotDocument(
        string userInfoJson, string cause)
    {
        var identity = await ExchangeAsync(userInfoJson);

        identity!.Email.ShouldBeNull();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains(cause));
    }

    [Theory]
    [InlineData("""{"email":"anna@gmail.com","email_verified":true}""")]
    [InlineData("""{"sub":"","email":"anna@gmail.com","email_verified":true}""")]
    [InlineData("""{"sub":12345,"email":"anna@gmail.com","email_verified":true}""")]
    [InlineData("""[]""")]
    public async Task ExchangeAsync_ShouldAnswerNull_WhenTheSubjectIsMissingOrUnusable(string userInfoJson)
    {
        (await ExchangeAsync(userInfoJson)).ShouldBeNull();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("SubjectUnusable"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNull_WhenTheSubjectIsLongerThanOidcAllows()
    {
        var tooLong = new string('7', ExternalSubject.MaximumLength + 1);

        (await ExchangeAsync(GoogleUserInfoShapes.Gmail(tooLong, "anna"))).ShouldBeNull();
    }

    // ---------- failures: one answer, one request per endpoint, no retry ----------

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNullAfterOneRequest_WhenTheCodeIsRefused()
    {
        _google.TokenStatus = HttpStatusCode.BadRequest;

        (await CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct)).ShouldBeNull();

        _google.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("TokenRefused"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNullAfterOneRequest_WhenTheTokenEndpointFails()
    {
        _google.TokenStatus = HttpStatusCode.InternalServerError;

        (await CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct)).ShouldBeNull();

        _google.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNull_WhenTheCodeIsUnknownOrTheVerifierDoesNotMatch()
    {
        _google.Expect(Code, GoogleUserInfoShapes.Gmail(Sub, "anna"), PkceVerifier.Generate().ToChallenge().Value);

        (await CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNullAfterOneRequestEach_WhenUserInfoRefusesTheToken()
    {
        _google.UserInfoStatus = HttpStatusCode.Unauthorized;

        (await ExchangeAsync(GoogleUserInfoShapes.Gmail(Sub, "anna"))).ShouldBeNull();

        _google.Requests.Count.ShouldBe(2);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("UserInfoRefused"));
    }

    [Theory]
    [InlineData("""{"token_type":"Bearer"}""")]
    [InlineData("<html>not json</html>")]
    public async Task ExchangeAsync_ShouldAnswerNull_WhenTheTokenBodyIsNotGooglesShape(string body)
    {
        // Declared unreachable from Google itself; a middlebox or a captive portal can produce it.
        _google.TokenBody = body;

        (await ExchangeAsync(GoogleUserInfoShapes.Gmail(Sub, "anna"))).ShouldBeNull();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNull_WhenTheTransportFails()
    {
        _google.Throw = new HttpRequestException("scripted");

        (await CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct)).ShouldBeNull();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Transport"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAnswerNull_WhenTheClientTimesOutWithoutTheCallerCancelling()
    {
        _google.Throw = new TaskCanceledException("scripted", new TimeoutException());

        (await CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct)).ShouldBeNull();
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Timeout"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldPropagateTheCancellation_WhenTheCallerCancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, cancelled.Token));
    }

    // ---------- what reaches the log ----------

    [Fact]
    public async Task ExchangeAsync_ShouldLogNoCredentialAndNoIdentifier_OnAnyBranch()
    {
        await ExchangeAsync(GoogleUserInfoShapes.ThirdPartyVerified(Sub, "anna@outlook.example"));
        _google.TokenStatus = HttpStatusCode.BadRequest;
        await CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct);

        _logger.Records.ShouldNotBeEmpty("otherwise the assertions below are vacuous");
        var surface = string.Join(
            "\n",
            _logger.Records.Select(r =>
                r.Message + "|" + string.Join("|", r.Properties.Select(p => $"{p.Key}={p.Value}"))));

        foreach (var secret in new[]
                 {
                     Code, _verifier.Reveal(), ClientSecret, Sub, "anna@outlook.example", "scripted-access-token",
                 })
        {
            surface.ShouldNotContain(secret);
        }
    }
}
