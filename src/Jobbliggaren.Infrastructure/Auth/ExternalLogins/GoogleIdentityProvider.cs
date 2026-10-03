using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// Google over <see cref="HttpClient"/> (#1744, ADR 0142 D8): the authorization URL, the token exchange and the
/// userinfo read, with no id_token, no JWKS and no JWT package. PKCE S256, scope <c>openid email</c>, no offline
/// access. The access token is used once for userinfo and dropped, and nothing that identifies the person, nor any
/// credential, is logged.
/// <para>
/// <b>The address is a <see cref="VerifiedEmail"/> only where Google is authoritative for it</b> (Google, "Verify the
/// Google ID token", read 2026-09-25): <c>email_verified</c> is the JSON <c>true</c>, AND the address ends in
/// <c>@gmail.com</c> or <c>hd</c> names a Workspace domain. A verified third-party address may have changed hands
/// since Google checked it, so it is refused the same way an unverified one is.
/// </para>
/// </summary>
internal sealed partial class GoogleIdentityProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleOAuthOptions> options,
    ExternalLoginCallbacks callbacks,
    ILogger<GoogleIdentityProvider> logger) : IExternalIdentityProvider
{
    internal const string HttpClientName = "google-oauth";

    // Constants from Google's discovery document (accounts.google.com/.well-known/openid-configuration, read
    // 2026-09-25), never fetched at run time.
    internal static readonly Uri AuthorizationEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");
    internal static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");
    internal static readonly Uri UserInfoEndpoint = new("https://openidconnect.googleapis.com/v1/userinfo");

    internal const string Scope = "openid email";

    // Our authorization request asks Google for its account chooser and its permission page (#1926, ADR 0142
    // Amendment (22)).
    internal const string Prompt = "select_account consent";

    private const string GmailSuffix = "@gmail.com";

    public ExternalProviderKey Key => ExternalProviderKey.Google;

    private string RedirectUri => callbacks.For(Key).AbsoluteUri;

    public Uri BuildAuthorizeUrl(OAuthState state, PkceChallenge challenge)
    {
        (string Name, string Value)[] parameters =
        [
            ("client_id", options.Value.ClientId),
            ("redirect_uri", RedirectUri),
            ("response_type", "code"),
            ("scope", Scope),
            ("prompt", Prompt),
            ("state", state.Reveal()),
            ("code_challenge", challenge.Value),
            ("code_challenge_method", PkceChallenge.Method),
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
            var accessToken = await RedeemCodeAsync(client, code, verifier, ct);
            return accessToken is null ? new ExternalExchange.Failed() : await ReadUserInfoAsync(client, accessToken, ct);
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

    private async Task<string?> RedeemCodeAsync(
        HttpClient client, AuthorizationCode code, PkceVerifier verifier, CancellationToken ct)
    {
        // client_secret_post: the secret, the code and the verifier travel in the body, never in the URI.
        using var content = new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code.Reveal()),
            new("code_verifier", verifier.Reveal()),
            new("client_id", options.Value.ClientId),
            new("client_secret", options.Value.ClientSecret),
            new("redirect_uri", RedirectUri),
        ]);

        using var response = await client.PostAsync(TokenEndpoint, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The body is not read: a provider's error body may echo what it refused.
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.TokenRefused, (int)response.StatusCode);
            return null;
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (body.RootElement.ValueKind != JsonValueKind.Object
            || !body.RootElement.TryGetProperty("access_token", out var token)
            || token.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(token.GetString()))
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Malformed, (int)response.StatusCode);
            return null;
        }

        return token.GetString();
    }

    private async Task<ExternalExchange> ReadUserInfoAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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

        if (!TryAuthoritative(root, out var email, out var refusal))
        {
            LogEmailNotUsable(logger, Key.Value, refusal);
            return new ExternalExchange.AddressRefused();
        }

        return new ExternalExchange.Identified(
            new ExternalIdentity(Key, subject, email));
    }

    // Fail-closed, in the order the claims are read: a verified flag that is not the JSON true, then an address that
    // cannot be stored, then a verified address Google is not authoritative for.
    private static bool TryAuthoritative(
        JsonElement root, [NotNullWhen(true)] out VerifiedEmail? email, out EmailRefusal refusal)
    {
        email = null;
        refusal = EmailRefusal.NotBoolean;
        if (!root.TryGetProperty("email_verified", out var verified)
            || verified.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        refusal = EmailRefusal.False;
        if (verified.ValueKind == JsonValueKind.False)
            return false;

        refusal = EmailRefusal.AddressUnparsable;
        var address = StringOrNull(root, "email");
        if (address is null || !StorableAddress.IsStorable(address) || VerifiedEmail.TryCreate(address) is not { } parsed)
            return false;

        refusal = EmailRefusal.NotAuthoritative;
        var workspace = !string.IsNullOrEmpty(StringOrNull(root, "hd"));
        if (!address.EndsWith(GmailSuffix, StringComparison.Ordinal) && !workspace)
            return false;

        email = parsed;
        return true;
    }

    private static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal enum ExchangeFailure
    {
        Transport,
        Timeout,
        Malformed,
        TokenRefused,
        UserInfoRefused,
        SubjectUnusable,
    }

    internal enum EmailRefusal
    {
        NotBoolean,
        False,
        AddressUnparsable,
        NotAuthoritative,
    }

    // #1744 — the provider, a cause class and an HTTP status only: never the code, a token, the subject or the
    // address. The template is the GitHub adapter's too (#1745), so one EventId reads alike for every provider.
    [LoggerMessage(1022, LogLevel.Warning,
        "External login exchange failed at the provider: Provider={Provider} Cause={Cause} Status={Status}")]
    private static partial void LogExchangeFailed(ILogger logger, string provider, ExchangeFailure cause, int status);

    // #1744 — the closed cause class only, so the claim's real shape can be read at activation without PII.
    [LoggerMessage(1023, LogLevel.Warning,
        "External login refused: the provider's address is not usable: Provider={Provider} Cause={Cause}")]
    private static partial void LogEmailNotUsable(ILogger logger, string provider, EmailRefusal cause);
}
