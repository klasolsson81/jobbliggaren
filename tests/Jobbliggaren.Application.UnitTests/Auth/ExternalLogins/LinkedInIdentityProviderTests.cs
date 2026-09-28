using System.Buffers.Text;
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
/// #1746 (ADR 0142 D8) — the LinkedIn adapter against <see cref="ScriptedLinkedIn"/>: the authorization URL, the token
/// exchange, the id_token's binding to the flow, the userinfo read, the address rule, the failure answers and what
/// reaches the log. Row names follow the 6c form round's table (test-writer §3.2). The address the rule admits binds a
/// login as Google's does, by Klas's decision. A shape LinkedIn does not document is declared as such and asserts only
/// that the adapter refuses it.
/// </summary>
public sealed class LinkedInIdentityProviderTests : IDisposable
{
    private const string ClientId = "li-scripted-client-id";

    // Not shaped like a real secret; asserted never to reach the log. gitleaks:allow
    private const string ClientSecret = "test-linkedin-client-secret"; // gitleaks:allow

    private const string Sub = LinkedInUserInfoShapes.DocumentedSub;
    private const string Primary = "anna@firma.example";
    private const string Code = "scripted-linkedin-code";

    private static readonly Uri SiteBase = new("https://jobbliggaren.example");
    private const string RedirectUri = "https://jobbliggaren.example/api/auth/oauth/linkedin/callback";

    private readonly ScriptedLinkedIn _linkedin = new(ClientId, ClientSecret);
    private readonly RecordingLogger<LinkedInIdentityProvider> _logger = new();
    private readonly PkceVerifier _verifier = PkceVerifier.Generate();

    private LinkedInIdentityProvider CreateSut(Uri? siteBase = null, string clientSecret = ClientSecret) =>
        new(
            new NamedClientFactory(LinkedInIdentityProvider.HttpClientName, _linkedin),
            Options.Create(new LinkedInOAuthOptions { ClientId = ClientId, ClientSecret = clientSecret }),
            new ExternalLoginCallbacks(siteBase ?? SiteBase),
            _logger);

    public void Dispose() => _linkedin.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Nonce => _verifier.ToChallenge().Value;

    private static string Member() => LinkedInUserInfoShapes.Member(Sub, Primary);

    private Task<ExternalExchange> ExchangeAsync(
        string userInfoJson, CancellationToken? ct = null, string? idTokenSubject = null, string redirectUri = RedirectUri)
    {
        _linkedin.Expect(Code, userInfoJson, Nonce, redirectUri, idTokenSubject);
        return CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, ct ?? Ct);
    }

    private Task<ExternalExchange> ExchangeUnscriptedAsync() =>
        CreateSut().ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct);

    private static VerifiedEmail AdmittedAddress(ExternalExchange exchange) =>
        exchange.ShouldBeOfType<ExternalExchange.Identified>().Identity.Email;

    private bool LoggedCause(int eventId, string cause) =>
        _logger.Records.Any(r => r.EventId.Id == eventId && r.Message.Contains($"Cause={cause}", StringComparison.Ordinal));

    private void ClaimsWith(string name, JsonNode? value) =>
        _linkedin.IdTokenClaims = claims =>
        {
            claims[name] = value?.DeepClone();
            return claims;
        };

    private static string UserInfo(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(Member())!.AsObject();
        change(document);
        return document.ToJsonString();
    }

    // Built at run time from JSON: no id_token is ever spelled in source (test-writer Minor 7).
    private static string Segment(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    // ---------- A: the authorization URL ----------

    [Fact]
    public void BuildAuthorizeUrl_ShouldCarryTheCodeFlowTheEmailScopeAndTheChallengeAsTheNonce_WhenAFlowStarts()
    {
        var state = OAuthState.Generate();
        var challenge = _verifier.ToChallenge();

        var url = CreateSut().BuildAuthorizeUrl(state, challenge);
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://www.linkedin.com/oauth/v2/authorization");
        // Exactly these keys: no code_challenge and no code_challenge_method, which LinkedIn's web flow does not take.
        query.AllKeys.Order(StringComparer.Ordinal).ShouldBe(
            ["client_id", "nonce", "redirect_uri", "response_type", "scope", "state"]);
        query["response_type"].ShouldBe("code");
        query["client_id"].ShouldBe(ClientId);
        query["redirect_uri"].ShouldBe(RedirectUri);
        query["scope"].ShouldBe("openid email");
        query["state"].ShouldBe(state.Reveal());
        query["nonce"].ShouldBe(challenge.Value);
        query["nonce"].ShouldNotBe(_verifier.Reveal());
    }

    [Theory]
    [InlineData("http://localhost:3000", "http://localhost:3000/api/auth/oauth/linkedin/callback")]
    [InlineData("https://dev.jobbliggaren.se/", "https://dev.jobbliggaren.se/api/auth/oauth/linkedin/callback")]
    public void BuildAuthorizeUrl_ShouldBuildTheRedirectFromTheSiteBase_WhenTheBaseHasOrLacksATrailingSlash(
        string siteBase, string expected)
    {
        var url = CreateSut(new Uri(siteBase)).BuildAuthorizeUrl(OAuthState.Generate(), _verifier.ToChallenge());

        HttpUtility.ParseQueryString(url.Query)["redirect_uri"].ShouldBe(expected);
    }

    [Fact]
    public void Key_ShouldBeLinkedIn_WhenRead() => CreateSut().Key.ShouldBe(ExternalProviderKey.LinkedIn);

    [Fact]
    public void HttpClientName_ShouldBeItsOwnAndNoOtherProvidersName_WhenRead()
    {
        // A form pin: the names share one configuration today; the scripted factory also refuses any other name.
        LinkedInIdentityProvider.HttpClientName.ShouldBe("linkedin-oauth");
        LinkedInIdentityProvider.HttpClientName.ShouldNotBe(GoogleIdentityProvider.HttpClientName);
        LinkedInIdentityProvider.HttpClientName.ShouldNotBe(GitHubIdentityProvider.HttpClientName);
    }

    // ---------- T: the token exchange ----------

    [Fact]
    public async Task ExchangeAsync_ShouldPostTheCodeInAFormWithTheSecretAndNoVerifier_WhenLinkedInAccepts()
    {
        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>();

        var token = _linkedin.Requests[0];
        token.Method.ShouldBe(HttpMethod.Post);
        token.Uri.AbsoluteUri.ShouldBe(ScriptedLinkedIn.TokenEndpoint);
        token.Uri.Query.ShouldBeEmpty();
        token.ContentType.ShouldBe("application/x-www-form-urlencoded");
        token.Form.Keys.Order(StringComparer.Ordinal).ShouldBe(
            ["client_id", "client_secret", "code", "grant_type", "redirect_uri"]);
        token.Form["grant_type"].ShouldBe("authorization_code");
        token.Form["client_id"].ShouldBe(ClientId);
        token.Form["client_secret"].ShouldBe(ClientSecret);
        token.Form["code"].ShouldBe(Code);
        token.Form["redirect_uri"].ShouldBe(RedirectUri);
        token.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFailAfterOneRequestAsInvalidRequest_WhenLinkedInCannotFindTheCode()
    {
        // Documented: an unknown or used code is 401 invalid_request, "authorization code not found".
        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=TokenInvalidRequest")
                                           && r.Message.Contains("Status=401") && r.Message.Contains("Provider=linkedin"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFailAfterOneRequestAsInvalidRedirectUri_WhenTheRedirectDiffersFromTheCodes()
    {
        // Documented: 400 invalid_redirect_uri.
        (await ExchangeAsync(Member(), redirectUri: "https://elsewhere.example/api/auth/oauth/linkedin/callback"))
            .ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=TokenInvalidRedirectUri")
                                           && r.Message.Contains("Status=400"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFailAfterOneRequestAsInvalidClient_WhenTheSecretIsWrong()
    {
        // DECLARED (executor#2087, RFC 6749 §5.2): 401 invalid_client. It shares its status with an unknown code, so
        // only the class tells the two apart at activation.
        _linkedin.Expect(Code, Member(), Nonce, RedirectUri);

        (await CreateSut(clientSecret: "another-test-secret").ExchangeAsync(AuthorizationCode.FromRaw(Code), _verifier, Ct))
            .ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=TokenInvalidClient")
                                           && r.Message.Contains("Status=401"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldReadAnErrorItDoesNotKnowAsTheGeneralClass_UnderASuccessStatus()
    {
        // DECLARED: an error LinkedIn may add, under a success status; it wins over the absent token.
        _linkedin.TokenBody = """{"error":"scripted_new_error","error_description":"scripted-description-marker"}""";

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "TokenRefused").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFailAndUseNoToken_WhenTheBodyCarriesBothAnErrorAndAToken()
    {
        // DECLARED: a body LinkedIn does not document. The error wins, and the token beside it is never used.
        _linkedin.TokenBody = """{"error":"invalid_request","access_token":"scripted-access-token-9"}""";

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "TokenInvalidRequest").ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ExchangeAsync_ShouldFailAfterOneRequest_WhenTheTokenEndpointFails(HttpStatusCode status)
    {
        _linkedin.TokenStatus = status;

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=TokenRefused")
                                           && r.Message.Contains($"Status={(int)status}"));
    }

    [Theory]
    [InlineData("<html>not json</html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"access_token":""}""")]
    [InlineData("""{"access_token":5}""")]
    [InlineData("""{"access_token":null}""")]
    public async Task ExchangeAsync_ShouldFailAsMalformed_WhenTheTokenBodyIsNotLinkedInsShape(string body)
    {
        // DECLARED: not a shape LinkedIn answers with; a middlebox or a captive portal can produce it.
        _linkedin.TokenBody = body;

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "Malformed").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldCarryATokenOfTheLengthLinkedInSaysToPlanFor()
    {
        // "3-Legged OAuth Flow": about 500 characters, plan for at least 1 000.
        _linkedin.TokenLength = 1000;

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>();

        var bearer = _linkedin.Requests[1].Authorization.ShouldNotBeNull();
        bearer.ShouldStartWith("Bearer scripted-access-token-1-");
        bearer.Length.ShouldBe("Bearer ".Length + 1000);
    }

    // ---------- N: the id_token binds the code to this flow ----------

    [Fact]
    public async Task ExchangeAsync_ShouldIdentify_WhenTheIdTokenEchoesTheFlowsNonceForThisClient() =>
        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>();

    public static TheoryData<string> NoncesThatAreNotTheFlows => new()
    {
        "another flow's challenge", "the verifier itself", "one letter in another case", "a trailing space", "empty",
        "a number",
    };

    [Theory]
    [MemberData(nameof(NoncesThatAreNotTheFlows))]
    public async Task ExchangeAsync_ShouldRefuseAfterOneRequest_WhenTheIdTokenNonceIsNotTheFlows(string form)
    {
        // A code minted for another flow (RFC 9700 §4.5): the access token is never used. OIDC Core §2: case-sensitive.
        JsonNode? nonce = form switch
        {
            "another flow's challenge" => PkceVerifier.Generate().ToChallenge().Value,
            "the verifier itself" => _verifier.Reveal(),
            "one letter in another case" => FlipFirstLetter(Nonce),
            "a trailing space" => Nonce + " ",
            "empty" => "",
            _ => 5,
        };
        ClaimsWith("nonce", nonce);

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "NonceMismatch").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseAfterOneRequest_WhenTheIdTokenCarriesNoNonce()
    {
        // OIDC Core §3.1.3.7 (11): a nonce sent MUST come back. LinkedIn's id_token table does not list the claim, so
        // this is the cause the first real login would show if LinkedIn drops it.
        _linkedin.IdTokenClaims = claims =>
        {
            claims.Remove("nonce");
            return claims;
        };

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "NonceAbsent").ShouldBeTrue();
    }

    public static TheoryData<string> AudiencesThatAreNotOnlyOurs => new()
    {
        "another client", "our id and more", "our id in capitals", "absent", "a number", "ours and another",
        "another alone", "an empty list",
    };

    [Theory]
    [MemberData(nameof(AudiencesThatAreNotOnlyOurs))]
    public async Task ExchangeAsync_ShouldRefuseAfterOneRequest_WhenTheIdTokenIsNotForThisClientAlone(string form)
    {
        // OIDC Core §2 allows a string or an array; §3.1.3.7 (3): ours must be the only audience.
        _linkedin.IdTokenClaims = claims =>
        {
            switch (form)
            {
                case "another client": claims["aud"] = "li-other-client-id"; break;
                case "our id and more": claims["aud"] = ClientId + "x"; break;
                case "our id in capitals": claims["aud"] = ClientId.ToUpperInvariant(); break;
                case "absent": claims.Remove("aud"); break;
                case "a number": claims["aud"] = 5; break;
                case "ours and another": claims["aud"] = new JsonArray(ClientId, "li-other-client-id"); break;
                case "another alone": claims["aud"] = new JsonArray("li-other-client-id"); break;
                default: claims["aud"] = new JsonArray(); break;
            }

            return claims;
        };

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "AudienceMismatch").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldIdentify_WhenTheAudienceIsAListOfOurIdAlone()
    {
        ClaimsWith("aud", new JsonArray(ClientId));

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseAfterOneRequestAsAbsent_WhenTheTokenResponseCarriesNoIdToken()
    {
        _linkedin.IdTokenClaims = _ => null;

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "IdTokenAbsent").ShouldBeTrue();
    }

    public static TheoryData<string> IdTokensThatAreNotThreeSegmentsOfAnObject => new()
    {
        "one segment", "two segments", "four segments", "a payload that is not base64url", "a JSON array",
        "not JSON",
    };

    [Theory]
    [MemberData(nameof(IdTokensThatAreNotThreeSegmentsOfAnObject))]
    public async Task ExchangeAsync_ShouldRefuseAfterOneRequestAsMalformed_WhenTheIdTokenCannotBeRead(string form)
    {
        // DECLARED: never from LinkedIn over TLS; a malformed token is a closed cause, never an exception.
        var header = Segment("""{"alg":"RS256"}""");
        _linkedin.IdTokenText = form switch
        {
            "one segment" => "scripted",
            "two segments" => $"{header}.{Segment("{}")}",
            "four segments" => $"{header}.{Segment("{}")}.x.y",
            "a payload that is not base64url" => $"{header}.%%%.x",
            "a JSON array" => $"{header}.{Segment("[]")}.x",
            _ => $"{header}.{Segment("not json")}.x",
        };

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "IdTokenMalformed").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseAsMalformed_WhenTheIdTokenIsNotAString()
    {
        _linkedin.TokenBody = """{"access_token":"scripted-access-token-9","id_token":5}""";

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        LoggedCause(1022, "IdTokenMalformed").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldRefuse_WhenUserInfoAnswersForAnotherMemberThanTheIdToken()
    {
        // OIDC Core §5.3.2: token substitution.
        (await ExchangeAsync(Member(), idTokenSubject: "Zz9other")).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(2);
        LoggedCause(1022, "SubjectMismatch").ShouldBeTrue();
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1023);
    }

    public static TheoryData<string> IdTokenSubjectsThatNameNoMember => new()
    {
        "absent", "null", "a number", "one character too long",
    };

    [Theory]
    [MemberData(nameof(IdTokenSubjectsThatNameNoMember))]
    public async Task ExchangeAsync_ShouldRefuseAfterOneRequest_WhenTheIdTokenNamesNoMember(string form)
    {
        // DECLARED: LinkedIn's id_token table lists `sub`, and OIDC Core §2 makes it REQUIRED. Only the refusal is
        // asserted, before the access token is spent.
        var tooLong = new string('A', ExternalSubject.MaximumLength + 1);
        ExternalSubject.TryCreate(tooLong).ShouldBeNull();
        _linkedin.IdTokenClaims = claims =>
        {
            switch (form)
            {
                case "absent": claims.Remove("sub"); break;
                case "null": claims["sub"] = null; break;
                case "a number": claims["sub"] = 5; break;
                default: claims["sub"] = tooLong; break;
            }

            return claims;
        };

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(1);
        LoggedCause(1022, "IdTokenSubjectUnusable").ShouldBeTrue();
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1023);
    }

    [Theory]
    [InlineData(ScriptedLinkedIn.DiscoveryIssuer)]
    [InlineData(ScriptedLinkedIn.DocumentedIssuer)]
    public async Task ExchangeAsync_ShouldIdentify_WhateverIssuerLinkedInWrites(string issuer)
    {
        // senior-cto-advisor, 6c form round §7: `iss` is not read. The documentation and the discovery document
        // disagree today, and the value has changed before.
        ClaimsWith("iss", issuer);

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>();
    }

    // ---------- S: userinfo, the subject ----------

    [Fact]
    public async Task ExchangeAsync_ShouldReadTheSubjectFromUserInfoWithTheBearer_WhenLinkedInAccepts()
    {
        var identity = (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>().Identity;

        identity.Provider.ShouldBe(ExternalProviderKey.LinkedIn);
        identity.Subject.Reveal().ShouldBe(Sub);
        var userInfo = _linkedin.Requests[1];
        userInfo.Method.ShouldBe(HttpMethod.Get);
        userInfo.Uri.AbsoluteUri.ShouldBe(ScriptedLinkedIn.UserInfoEndpoint);
        userInfo.Authorization.ShouldNotBeNull().ShouldStartWith("Bearer scripted-access-token-1-");
        // No introspection, no revocation and no JWKS: LinkedIn documents no revocation, and nothing verifies a signature.
        _linkedin.Requests.Select(r => $"{r.Method} {r.Uri.GetLeftPart(UriPartial.Path)}").ShouldBe(
        [
            $"POST {ScriptedLinkedIn.TokenEndpoint}",
            $"GET {ScriptedLinkedIn.UserInfoEndpoint}",
        ]);
    }

    public static TheoryData<string> UnusableSubjects => new()
    {
        "absent", "null", "empty", "a number", "256 characters", "a space", "a letter outside ASCII", "a list body",
        "the openid email fields without sub",
    };

    [Theory]
    [MemberData(nameof(UnusableSubjects))]
    public async Task ExchangeAsync_ShouldFailWithoutReadingTheAddress_WhenTheSubjectIsMissingOrUnusable(string form)
    {
        // DECLARED: OIDC Core §5.3.2 says sub MUST be returned; LinkedIn's scope table ties it to `profile`
        // (test-writer Major 1), so its absence under `openid email` is the cause the first real login would show.
        var userInfo = form switch
        {
            "absent" => UserInfo(d => d.Remove("sub")),
            "null" => UserInfo(d => d["sub"] = null),
            "empty" => UserInfo(d => d["sub"] = ""),
            "a number" => UserInfo(d => d["sub"] = 12345),
            "256 characters" => UserInfo(d => d["sub"] = new string('a', 256)),
            "a space" => UserInfo(d => d["sub"] = "782 bbtaQ"),
            "a letter outside ASCII" => UserInfo(d => d["sub"] = "782bbtaé"),
            "a list body" => "[]",
            _ => new JsonObject { ["email"] = Primary, ["email_verified"] = false }.ToJsonString(),
        };

        (await ExchangeAsync(userInfo, idTokenSubject: Sub)).ShouldBeOfType<ExternalExchange.Failed>();

        LoggedCause(1022, "SubjectUnusable").ShouldBeTrue();
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1023);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ExchangeAsync_ShouldFailAfterTwoRequests_WhenUserInfoRefusesTheToken(HttpStatusCode status)
    {
        _linkedin.UserInfoStatus = status;

        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Failed>();

        _linkedin.Requests.Count.ShouldBe(2);
        _logger.Records.ShouldContain(r => r.EventId.Id == 1022 && r.Message.Contains("Cause=UserInfoRefused")
                                           && r.Message.Contains($"Status={(int)status}"));
    }

    [Fact]
    public async Task ExchangeAsync_ShouldKeepTheSubjectExactlyAsLinkedInSendsIt()
    {
        // A pairwise identifier is case-sensitive.
        var exchange = await ExchangeAsync(LinkedInUserInfoShapes.Member("AbC-12_x", Primary));

        exchange.ShouldBeOfType<ExternalExchange.Identified>().Identity.Subject.Reveal().ShouldBe("AbC-12_x");
    }

    // ---------- E: userinfo, the address rule (the JSON true, and nothing else) ----------

    [Fact]
    public async Task ExchangeAsync_ShouldAdmitTheAddress_WhenTheFlagIsTheJsonTrue() =>
        AdmittedAddress(await ExchangeAsync(Member())).Value.ShouldBe(Primary);

    [Fact]
    public async Task ExchangeAsync_ShouldAdmitTheAddress_WhenOnlyTheOpenIdEmailFieldsAreSent() =>
        AdmittedAddress(await ExchangeAsync(LinkedInUserInfoShapes.EmailScopeOnly(Sub, Primary))).Value.ShouldBe(Primary);

    [Fact]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenLinkedInHasNotVerifiedIt()
    {
        (await ExchangeAsync(LinkedInUserInfoShapes.Unverified(Sub, Primary)))
            .ShouldBeOfType<ExternalExchange.AddressRefused>();

        _logger.Records.ShouldContain(r => r.EventId.Id == 1023 && r.Message.Contains("Cause=False")
                                           && r.Message.Contains("Provider=linkedin"));
    }

    public static TheoryData<string, string> DocumentedAbsences => new()
    {
        { "neither", "AddressAbsent" },
        { "an address without its flag", "FlagAbsent" },
        { "a flag without its address", "AddressAbsent" },
    };

    [Theory]
    [MemberData(nameof(DocumentedAbsences))]
    public async Task ExchangeAsync_ShouldRefuseTheAddressWithItsOwnCause_WhenAnOptionalFieldIsAbsent(
        string form, string cause)
    {
        // Documented: "The 'email' and 'email_verified' fields are optional and may not be included in all responses."
        var userInfo = form switch
        {
            "neither" => LinkedInUserInfoShapes.WithoutAddress(Sub),
            "an address without its flag" => LinkedInUserInfoShapes.AddressWithoutFlag(Sub, Primary),
            _ => LinkedInUserInfoShapes.FlagWithoutAddress(Sub),
        };

        (await ExchangeAsync(userInfo)).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, cause).ShouldBeTrue();
    }

    public static TheoryData<string, string> FlagsThatAreNotTheJsonTrue => new()
    {
        { "\"true\"", "FlagIsString" },
        { "\"false\"", "FlagIsString" },
        { "\"True\"", "FlagIsString" },
        { "\"yes\"", "FlagIsString" },
        { "1", "FlagNotBoolean" },
        { "0", "FlagNotBoolean" },
        { "null", "FlagNotBoolean" },
        { "{}", "FlagNotBoolean" },
    };

    [Theory]
    [MemberData(nameof(FlagsThatAreNotTheJsonTrue))]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenTheFlagIsNotAJsonBoolean(string flag, string cause)
    {
        // DECLARED: LinkedIn documents a Boolean, and ADR 0142 D8's note of a string form names no source. Only the JSON
        // true passes (senior-cto-advisor, J); a string has its own class, so the first login can answer D8's note.
        (await ExchangeAsync(UserInfo(d => d["email_verified"] = JsonNode.Parse(flag))))
            .ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, cause).ShouldBeTrue();
    }

    public static TheoryData<string> UnparsableAddresses => new()
    {
        "a number", "null", "one part", "two at signs", "a space", "a control character", "one over the bound",
    };

    [Theory]
    [MemberData(nameof(UnparsableAddresses))]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenItIsNotOneStorableAddressWithinTheBound(string form)
    {
        // DECLARED, as Google's and GitHub's rows are.
        JsonNode? email = form switch
        {
            "a number" => 5,
            "null" => null,
            "one part" => "anna",
            "two at signs" => "anna@firma@example",
            "a space" => "anna berg@firma.example",
            "a control character" => $"anna{(char)7}berg@firma.example",
            _ => $"{new string('a', EmailAddressRules.MaximumLength - "@x.example".Length + 1)}@x.example",
        };

        (await ExchangeAsync(UserInfo(d => d["email"] = email))).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "AddressUnparsable").ShouldBeTrue();
    }

    [Theory]
    [InlineData("Anna.Berg@Firma.example")]
    [InlineData("perſon@example.com")]
    public async Task ExchangeAsync_ShouldKeepTheAddressExactlyAsLinkedInSendsIt(string address) =>
        AdmittedAddress(await ExchangeAsync(LinkedInUserInfoShapes.Member(Sub, address))).Value.ShouldBe(address);

    [Fact]
    public async Task ExchangeAsync_ShouldTakeTheAddressFromUserInfo_WhenTheIdTokenCarriesAnother()
    {
        ClaimsWith("email", "annan@firma.example");

        AdmittedAddress(await ExchangeAsync(Member())).Value.ShouldBe(Primary);
    }

    [Fact]
    public async Task ExchangeAsync_ShouldTryTheSubjectBeforeTheAddress()
    {
        (await ExchangeAsync(
                UserInfo(d =>
                {
                    d["sub"] = "";
                    d["email_verified"] = false;
                }),
                idTokenSubject: Sub))
            .ShouldBeOfType<ExternalExchange.Failed>();

        LoggedCause(1022, "SubjectUnusable").ShouldBeTrue();
        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1023);
    }

    [Theory]
    [InlineData("anna@privaterelay.appleid.com")]
    [InlineData("ANNA@PRIVATERELAY.APPLEID.COM")]
    [InlineData("anna@PrivateRelay.AppleId.com")]
    public async Task ExchangeAsync_ShouldRefuseTheAddress_WhenItIsApplesRelay(string relay)
    {
        // security-auditor Minor 1: the relay forwards only mail from LinkedIn. Whether LinkedIn can hand such an
        // address over is not measured, so only the refusal and its cause are asserted.
        (await ExchangeAsync(LinkedInUserInfoShapes.Member(Sub, relay))).ShouldBeOfType<ExternalExchange.AddressRefused>();

        LoggedCause(1023, "NotAMailbox").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldAdmitAnAddressThatOnlyContainsTheRelayDomain()
    {
        // Kills a Contains over the whole address: this domain is not Apple's.
        const string lookalike = "anna@privaterelay.appleid.com.evil.example";

        AdmittedAddress(await ExchangeAsync(LinkedInUserInfoShapes.Member(Sub, lookalike))).Value.ShouldBe(lookalike);
    }

    // ---------- F: failures and cancellation ----------

    [Fact]
    public async Task ExchangeAsync_ShouldFail_WhenTheTransportFails()
    {
        _linkedin.Throw = new HttpRequestException("scripted");

        (await ExchangeUnscriptedAsync()).ShouldBeOfType<ExternalExchange.Failed>();

        LoggedCause(1022, "Transport").ShouldBeTrue();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldFail_WhenTheClientTimesOutWithoutTheCallerCancelling()
    {
        _linkedin.Throw = new TaskCanceledException("scripted", new TimeoutException());

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
    public async Task ExchangeAsync_ShouldPropagateTheCancellation_WhenTheCallerCancelsMidFlow()
    {
        // Kills an inner catch of TaskCanceledException without its caller-token filter.
        using var caller = new CancellationTokenSource();
        _linkedin.OnUserInfo = caller.Cancel;

        await Should.ThrowAsync<OperationCanceledException>(() => ExchangeAsync(Member(), caller.Token));

        _logger.Records.ShouldNotContain(r => r.EventId.Id == 1022);
    }

    // ---------- L: what reaches the log ----------

    [Fact]
    public async Task ExchangeAsync_ShouldWriteNoLogLine_WhenLinkedInIdentifiesTheMember()
    {
        (await ExchangeAsync(Member())).ShouldBeOfType<ExternalExchange.Identified>();

        _logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExchangeAsync_ShouldLogNoCredentialNoIdentifierNoAddressAndNoProfile()
    {
        // A refused flag, a nonce that is not the flow's, a subject userinfo does not share, and a refused code with
        // LinkedIn's description: every line the adapter writes, over one surface.
        await ExchangeAsync(LinkedInUserInfoShapes.Unverified(Sub, Primary));
        ClaimsWith("nonce", PkceVerifier.Generate().ToChallenge().Value);
        await ExchangeAsync(Member());
        _linkedin.IdTokenClaims = null;
        await ExchangeAsync(Member(), idTokenSubject: "Zz9other");
        await ExchangeUnscriptedAsync();

        _logger.Records.ShouldNotBeEmpty("otherwise the assertions below are vacuous");
        var surface = string.Join(
            "\n",
            _logger.Records.Select(r =>
                r.Message + "|" + string.Join("|", r.Properties.Select(p => $"{p.Key}={p.Value}"))));

        foreach (var secret in new[]
                 {
                     Code, _verifier.Reveal(), Nonce, ClientSecret, "scripted-access-token", "scripted-signature",
                     Segment("""{"alg":"RS256","kid":"scripted"}"""), Sub, "Zz9other", Primary, "Anna Berg",
                     "media.licdn.com", ScriptedLinkedIn.ErrorDescriptionMarker,
                 })
        {
            surface.ShouldNotContain(secret);
        }
    }

    [Fact]
    public void Options_ShouldPrintNoPartOfTheSecret_WhenInterpolated() =>
        $"{new LinkedInOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }}".ShouldNotContain(ClientSecret);

    private static string FlipFirstLetter(string value)
    {
        var at = 0;
        while (!char.IsAsciiLetter(value[at]))
            at++;

        var letter = value[at];
        var flipped = char.IsUpper(letter) ? char.ToLowerInvariant(letter) : char.ToUpperInvariant(letter);
        return string.Concat(value.AsSpan(0, at), flipped.ToString(), value.AsSpan(at + 1));
    }
}
