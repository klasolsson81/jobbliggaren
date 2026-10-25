using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// LinkedIn over <see cref="HttpClient"/> (#1746, ADR 0142 D8): "Sign In with LinkedIn using OpenID Connect", scope
/// <c>openid email</c>. LinkedIn's web flow takes no PKCE and refuses a verifier. The id_token is read over TLS from
/// the fixed token endpoint and never verified by signature (OIDC Core §3.1.3.7 (6)); the identity comes from userinfo.
/// The access token is used once and dropped: LinkedIn documents no revocation. Nothing that identifies the person,
/// nor any credential, is logged.
/// <para>
/// <b>LinkedIn is never the mailbox.</b> Its verified primary address is admitted as a <see cref="VerifiedEmail"/> by
/// Klas's decision (Amendment (20)), over security-auditor's M-1, which stands. The flag is the JSON <c>true</c>; every
/// other shape, the string <c>"true"</c> included, is refused fail-closed, and so is Apple's relay domain.
/// </para>
/// </summary>
internal sealed partial class LinkedInIdentityProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<LinkedInOAuthOptions> options,
    ExternalLoginCallbacks callbacks,
    ILogger<LinkedInIdentityProvider> logger) : IExternalIdentityProvider
{
    internal const string HttpClientName = "linkedin-oauth";

    // Constants from LinkedIn's discovery document (www.linkedin.com/oauth/.well-known/openid-configuration) and its
    // "3-Legged OAuth Flow" page, read 2026-09-27, never fetched at run time.
    internal static readonly Uri AuthorizationEndpoint = new("https://www.linkedin.com/oauth/v2/authorization");
    internal static readonly Uri TokenEndpoint = new("https://www.linkedin.com/oauth/v2/accessToken");
    internal static readonly Uri UserInfoEndpoint = new("https://api.linkedin.com/v2/userinfo");

    internal const string Scope = "openid email";

    // Apple's relay forwards only mail from the domains the relying party registered, and that party is LinkedIn: an
    // account born on it would have no inbox for us.
    internal const string AppleRelayDomain = "privaterelay.appleid.com";

    private const string ErrorInvalidRequest = "invalid_request";
    private const string ErrorInvalidRedirectUri = "invalid_redirect_uri";
    private const string ErrorInvalidClient = "invalid_client";

    public ExternalProviderKey Key => ExternalProviderKey.LinkedIn;

    private string RedirectUri => callbacks.For(Key).AbsoluteUri;

    public Uri BuildAuthorizeUrl(OAuthState state, PkceChallenge challenge)
    {
        (string Name, string Value)[] parameters =
        [
            ("response_type", "code"),
            ("client_id", options.Value.ClientId),
            ("redirect_uri", RedirectUri),
            ("scope", Scope),
            ("state", state.Reveal()),
        ];

        var query = string.Join('&', parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        return new Uri($"{AuthorizationEndpoint.AbsoluteUri}?{query}");
    }

    public async Task<ExternalExchange> ExchangeAsync(
        AuthorizationCode code, PkceVerifier verifier, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            return await RedeemCodeAsync(client, code, ct) is { } redeemed
                ? await ReadUserInfoAsync(client, redeemed, ct)
                : new ExternalExchange.Failed();
        }
        catch (HttpRequestException)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Transport, 0);
            return new ExternalExchange.Failed();
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // The client's own timeout, not the caller asking to stop: the caller's cancellation propagates.
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Timeout, 0);
            return new ExternalExchange.Failed();
        }
        catch (JsonException)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Malformed, 0);
            return new ExternalExchange.Failed();
        }
    }

    private async Task<Redemption?> RedeemCodeAsync(HttpClient client, AuthorizationCode code, CancellationToken ct)
    {
        // client_secret_post, and never a code_verifier: LinkedIn's web flow refuses one.
        using var content = new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code.Reveal()),
            new("client_id", options.Value.ClientId),
            new("client_secret", options.Value.ClientSecret),
            new("redirect_uri", RedirectUri),
        ]);

        using var response = await client.PostAsync(TokenEndpoint, content, ct);
        var status = (int)response.StatusCode;

        // LinkedIn answers a refused code under a 4xx with an `error` member. It is read under any status and wins over
        // a token beside it; only the closed class is logged, never the value or the description.
        using var body = await TryParseAsync(response, ct);
        var root = body?.RootElement;
        if (root is { ValueKind: JsonValueKind.Object } errorRoot && errorRoot.TryGetProperty("error", out var error))
        {
            LogExchangeFailed(logger, Key.Value, TokenErrorCause(error), status);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.TokenRefused, status);
            return null;
        }

        if (root is not { ValueKind: JsonValueKind.Object } tokenRoot
            || StringOrNull(tokenRoot, "access_token") is not { Length: > 0 } accessToken)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Malformed, status);
            return null;
        }

        if (!TryReadIdTokenSubject(tokenRoot, out var idTokenSubject, out var failure))
        {
            LogExchangeFailed(logger, Key.Value, failure, status);
            return null;
        }

        return new Redemption(accessToken, idTokenSubject);
    }

    // A body that is not JSON is no body: an error page in front of LinkedIn reads like an empty answer.
    private static async Task<JsonDocument?> TryParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExchangeFailure TokenErrorCause(JsonElement error) =>
        error.ValueKind != JsonValueKind.String ? ExchangeFailure.TokenRefused : error.GetString() switch
        {
            ErrorInvalidRequest => ExchangeFailure.TokenInvalidRequest,
            ErrorInvalidRedirectUri => ExchangeFailure.TokenInvalidRedirectUri,
            ErrorInvalidClient => ExchangeFailure.TokenInvalidClient,
            _ => ExchangeFailure.TokenRefused,
        };

    // The id_token must be for our client. `iss` and `exp` are not read (senior-cto-advisor, 6c form round, §7).
    private bool TryReadIdTokenSubject(JsonElement tokenRoot, out ExternalSubject subject, out ExchangeFailure failure)
    {
        subject = default;
        failure = ExchangeFailure.IdTokenAbsent;
        if (!tokenRoot.TryGetProperty("id_token", out var idToken) || idToken.ValueKind == JsonValueKind.Null)
            return false;

        failure = ExchangeFailure.IdTokenMalformed;
        using var payload = idToken.ValueKind == JsonValueKind.String && idToken.GetString() is { } text
            ? PayloadOf(text)
            : null;
        if (payload is null)
            return false;

        var claims = payload.RootElement;
        failure = ExchangeFailure.AudienceMismatch;
        if (!IsOurAudience(claims))
            return false;

        failure = ExchangeFailure.IdTokenSubjectUnusable;
        if (ExternalSubject.TryCreate(StringOrNull(claims, "sub")) is not { } idTokenSubject)
            return false;

        subject = idTokenSubject;
        return true;
    }

    // Three segments and a base64url JSON object, or nothing: a malformed token is a closed cause, never an exception.
    private static JsonDocument? PayloadOf(string idToken)
    {
        var segments = idToken.Split('.');
        if (segments.Length != 3 || !Base64Url.IsValid(segments[1]))
            return null;

        try
        {
            var document = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // OIDC Core §2 allows a string or an array; ours must be the only audience (§3.1.3.7 (3)).
    private bool IsOurAudience(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var audience))
            return false;

        if (audience.ValueKind == JsonValueKind.String)
            return string.Equals(audience.GetString(), options.Value.ClientId, StringComparison.Ordinal);

        return audience.ValueKind == JsonValueKind.Array
               && audience.GetArrayLength() == 1
               && audience[0].ValueKind == JsonValueKind.String
               && string.Equals(audience[0].GetString(), options.Value.ClientId, StringComparison.Ordinal);
    }

    private async Task<ExternalExchange> ReadUserInfoAsync(HttpClient client, Redemption redeemed, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", redeemed.AccessToken);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.UserInfoRefused, (int)response.StatusCode);
            return new ExternalExchange.Failed();
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || ExternalSubject.TryCreate(StringOrNull(root, "sub")) is not { } subject)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.SubjectUnusable, (int)response.StatusCode);
            return new ExternalExchange.Failed();
        }

        // OIDC Core §5.3.2: an answer about another member than the id_token's is never used.
        if (!string.Equals(subject.Reveal(), redeemed.IdTokenSubject.Reveal(), StringComparison.Ordinal))
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.SubjectMismatch, (int)response.StatusCode);
            return new ExternalExchange.Failed();
        }

        if (!TryVerified(root, out var email, out var refusal))
        {
            LogEmailNotUsable(logger, Key.Value, refusal);
            return new ExternalExchange.AddressRefused();
        }

        return new ExternalExchange.Identified(
            new ExternalIdentity(Key, subject, email));
    }

    // Fail-closed, in the order the claims are read: the flag, then the address. LinkedIn marks both optional, and
    // each absence has its own cause, so the first real login can be read from the log without PII.
    private static bool TryVerified(
        JsonElement root, [NotNullWhen(true)] out VerifiedEmail? email, out EmailRefusal refusal)
    {
        email = null;
        var hasAddress = root.TryGetProperty("email", out var address);
        if (!root.TryGetProperty("email_verified", out var verified))
        {
            refusal = hasAddress ? EmailRefusal.FlagAbsent : EmailRefusal.AddressAbsent;
            return false;
        }

        refusal = verified.ValueKind switch
        {
            JsonValueKind.String => EmailRefusal.FlagIsString,
            JsonValueKind.False => EmailRefusal.False,
            JsonValueKind.True => hasAddress ? EmailRefusal.AddressUnparsable : EmailRefusal.AddressAbsent,
            _ => EmailRefusal.FlagNotBoolean,
        };
        if (verified.ValueKind != JsonValueKind.True || !hasAddress)
            return false;

        var raw = address.ValueKind == JsonValueKind.String ? address.GetString() : null;
        if (raw is not null && IsAppleRelay(raw))
        {
            refusal = EmailRefusal.NotAMailbox;
            return false;
        }

        email = raw is not null && StorableAddress.IsStorable(raw) ? VerifiedEmail.TryCreate(raw) : null;
        return email is not null;
    }

    // The domain after the last '@', compared case-blind: a refusing suffix may refuse more, never less.
    private static bool IsAppleRelay(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && string.Equals(address[(at + 1)..], AppleRelayDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record Redemption(string AccessToken, ExternalSubject IdTokenSubject);

    internal enum ExchangeFailure
    {
        Transport,
        Timeout,
        Malformed,
        TokenRefused,
        TokenInvalidRequest,
        TokenInvalidRedirectUri,
        TokenInvalidClient,
        IdTokenAbsent,
        IdTokenMalformed,
        AudienceMismatch,
        IdTokenSubjectUnusable,
        UserInfoRefused,
        SubjectUnusable,
        SubjectMismatch,
    }

    internal enum EmailRefusal
    {
        FlagAbsent,
        FlagIsString,
        FlagNotBoolean,
        False,
        AddressAbsent,
        NotAMailbox,
        AddressUnparsable,
    }

    // #1744's template, shared with the Google and GitHub adapters: never the code, a token, the subject or an address.
    [LoggerMessage(1022, LogLevel.Warning,
        "External login exchange failed at the provider: Provider={Provider} Cause={Cause} Status={Status}")]
    private static partial void LogExchangeFailed(ILogger logger, string provider, ExchangeFailure cause, int status);

    [LoggerMessage(1023, LogLevel.Warning,
        "External login refused: the provider's address is not usable: Provider={Provider} Cause={Cause}")]
    private static partial void LogEmailNotUsable(ILogger logger, string provider, EmailRefusal cause);
}
