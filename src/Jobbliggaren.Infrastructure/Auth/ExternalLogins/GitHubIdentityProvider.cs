using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// GitHub over <see cref="HttpClient"/> (#1745, ADR 0142 D8 and Amendment (16)): the authorization URL, the token
/// exchange, then the REST API for the account's numeric id and its email list. PKCE S256 and scope
/// <c>user:email</c> alone. The access token is used for the two reads and then revoked; nothing that identifies the
/// person, nor any credential, is logged.
/// <para>
/// <b>GitHub is never the mailbox</b>, so its verified primary address is an <see cref="AssertedEmail"/>, never a
/// <see cref="VerifiedEmail"/>: it chooses where a login code goes, and only a code binds this login to an account.
/// The address is the list's one primary entry, verified as the JSON <c>true</c>, and not GitHub's noreply domain;
/// every other shape is refused fail-closed. <c>/user</c>'s <c>email</c> is the public profile field and is never read.
/// </para>
/// </summary>
internal sealed partial class GitHubIdentityProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<GitHubOAuthOptions> options,
    ExternalLoginCallbacks callbacks,
    ILogger<GitHubIdentityProvider> logger) : IExternalIdentityProvider
{
    internal const string HttpClientName = "github-oauth";

    // Constants from GitHub's documentation ("Authorizing OAuth apps" and the REST reference, read 2026-09-26), never
    // discovered at run time.
    internal static readonly Uri AuthorizationEndpoint = new("https://github.com/login/oauth/authorize");
    internal static readonly Uri TokenEndpoint = new("https://github.com/login/oauth/access_token");
    internal static readonly Uri UserEndpoint = new("https://api.github.com/user");
    internal static readonly Uri EmailsEndpoint = new("https://api.github.com/user/emails?per_page=100");

    internal const string Scope = "user:email";

    // Requests without a User-Agent are rejected by GitHub's REST API; the version is pinned so the fields read here
    // cannot change under a default.
    internal const string UserAgent = "Jobbliggaren";
    internal const string ApiVersion = "2026-03-10";
    private const string ApiMediaType = "application/vnd.github+json";

    // A GitHub-hosted placeholder that receives no mail: an account born on it would have no inbox.
    internal const string NoReplyDomain = "users.noreply.github.com";

    private const string ErrorBadVerificationCode = "bad_verification_code";
    private const string ErrorIncorrectClientCredentials = "incorrect_client_credentials";
    private const string ErrorRedirectUriMismatch = "redirect_uri_mismatch";
    private const string ErrorUnverifiedUserEmail = "unverified_user_email";

    public ExternalProviderKey Key => ExternalProviderKey.GitHub;

    private string RedirectUri => callbacks.For(Key).AbsoluteUri;

    public Uri BuildAuthorizeUrl(OAuthState state, PkceChallenge challenge)
    {
        (string Name, string Value)[] parameters =
        [
            ("client_id", options.Value.ClientId),
            ("redirect_uri", RedirectUri),
            ("scope", Scope),
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
        string? accessToken = null;
        try
        {
            var redeemed = await RedeemCodeAsync(client, code, verifier, ct);
            if (redeemed.Token is not { } token)
                return redeemed.Refusal!;

            accessToken = token;
            if (await ReadSubjectAsync(client, token, ct) is not { } subject)
                return new ExternalExchange.Failed();

            return await ReadAddressAsync(client, token, subject, ct);
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
        finally
        {
            // Revoked on every path that holds a token, refusal included: GitHub's token may not expire, and it is
            // never stored, so nothing needs it after the reads.
            if (accessToken is not null)
                await RevokeAsync(client, accessToken);
        }
    }

    private async Task<(string? Token, ExternalExchange? Refusal)> RedeemCodeAsync(
        HttpClient client, AuthorizationCode code, PkceVerifier verifier, CancellationToken ct)
    {
        // The secret, the code and the verifier travel in the body, never in the URI.
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("client_id", options.Value.ClientId),
                new("client_secret", options.Value.ClientSecret),
                new("code", code.Reveal()),
                new("code_verifier", verifier.Reveal()),
                new("redirect_uri", RedirectUri),
            ]),
        };

        // Without it GitHub answers form-encoded.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The body is not read: a provider's error body may echo what it refused.
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.TokenRefused, (int)response.StatusCode);
            return (null, new ExternalExchange.Failed());
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Malformed, (int)response.StatusCode);
            return (null, new ExternalExchange.Failed());
        }

        // GitHub reports a refused code with a success status and an `error` member; it wins over any token beside it.
        // Only the closed class is logged, never the value, its description or its URI.
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String
                && string.Equals(error.GetString(), ErrorUnverifiedUserEmail, StringComparison.Ordinal))
            {
                LogEmailNotUsable(logger, Key.Value, EmailRefusal.UnverifiedAtToken);
                return (null, new ExternalExchange.AddressRefused());
            }

            LogExchangeFailed(logger, Key.Value, TokenErrorCause(error), (int)response.StatusCode);
            return (null, new ExternalExchange.Failed());
        }

        if (!root.TryGetProperty("access_token", out var token)
            || token.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(token.GetString()))
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.Malformed, (int)response.StatusCode);
            return (null, new ExternalExchange.Failed());
        }

        return (token.GetString(), null);
    }

    private static ExchangeFailure TokenErrorCause(JsonElement error) =>
        error.ValueKind != JsonValueKind.String ? ExchangeFailure.TokenRefused : error.GetString() switch
        {
            ErrorBadVerificationCode => ExchangeFailure.TokenBadVerificationCode,
            ErrorIncorrectClientCredentials => ExchangeFailure.TokenIncorrectClientCredentials,
            ErrorRedirectUriMismatch => ExchangeFailure.TokenRedirectUriMismatch,
            _ => ExchangeFailure.TokenRefused,
        };

    // Only `id`: never `login`, which can be changed and then taken by someone else, and never `email`.
    private async Task<ExternalSubject?> ReadSubjectAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        using var request = ApiRequest(HttpMethod.Get, UserEndpoint, accessToken);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.UserRefused, (int)response.StatusCode);
            return null;
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = body.RootElement;

        // A positive integer, written in invariant decimal, so the stored key is the same text at every login.
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.Number
            && id.TryGetInt64(out var value)
            && value > 0
            && ExternalSubject.TryCreate(value.ToString(CultureInfo.InvariantCulture)) is { } subject)
        {
            return subject;
        }

        LogExchangeFailed(logger, Key.Value, ExchangeFailure.SubjectUnusable, (int)response.StatusCode);
        return null;
    }

    private async Task<ExternalExchange> ReadAddressAsync(
        HttpClient client, string accessToken, ExternalSubject subject, CancellationToken ct)
    {
        using var request = ApiRequest(HttpMethod.Get, EmailsEndpoint, accessToken);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            LogExchangeFailed(logger, Key.Value, ExchangeFailure.EmailsRefused, (int)response.StatusCode);
            return new ExternalExchange.Failed();
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var (address, refusal) = PrimaryVerified(body.RootElement);
        if (address is null)
        {
            LogEmailNotUsable(logger, Key.Value, refusal!.Value);
            return new ExternalExchange.AddressRefused();
        }

        return new ExternalExchange.Identified(
            new ExternalIdentity(Key, subject, new ExternalAddress.Asserted(address)));
    }

    // Fail-closed, and the whole list is refused for a shape GitHub does not document on any entry: the list must be
    // an array of objects, each with a JSON boolean `primary`; exactly one entry is primary; its `verified` is the
    // JSON true; its address is not the noreply domain and is storable. Pagination is not followed, so a primary
    // beyond the one page read is refused like a missing one.
    private static (AssertedEmail? Address, EmailRefusal? Refusal) PrimaryVerified(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return (null, EmailRefusal.ListMalformed);

        JsonElement? primary = null;
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                return (null, EmailRefusal.ListMalformed);

            if (!entry.TryGetProperty("primary", out var isPrimary)
                || isPrimary.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return (null, EmailRefusal.NotBoolean);
            }

            if (isPrimary.ValueKind == JsonValueKind.False)
                continue;

            if (primary is not null)
                return (null, EmailRefusal.PrimaryAmbiguous);

            primary = entry;
        }

        if (primary is not { } chosen)
            return (null, EmailRefusal.NoPrimary);

        if (!chosen.TryGetProperty("verified", out var verified)
            || verified.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, EmailRefusal.NotBoolean);
        }

        if (verified.ValueKind == JsonValueKind.False)
            return (null, EmailRefusal.False);

        var raw = chosen.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
            ? email.GetString()
            : null;
        if (raw is null)
            return (null, EmailRefusal.AddressUnparsable);

        if (IsNoReply(raw))
            return (null, EmailRefusal.NotAMailbox);

        return StorableAddress.IsStorable(raw) && AssertedEmail.TryCreate(raw) is { } address
            ? (address, null)
            : (null, EmailRefusal.AddressUnparsable);
    }

    // The domain after the last '@', compared case-blind: a refusing suffix may refuse more, never less.
    private static bool IsNoReply(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && string.Equals(address[(at + 1)..], NoReplyDomain, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RevokeAsync(HttpClient client, string accessToken)
    {
        var endpoint = new Uri(
            $"https://api.github.com/applications/{Uri.EscapeDataString(options.Value.ClientId)}/token");
        try
        {
            // Not the caller's token: a revocation runs to its own end, bounded by the client's timeout.
            using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint)
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["access_token"] = accessToken }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{options.Value.ClientId}:{options.Value.ClientSecret}")));
            AddApiHeaders(request);

            using var response = await client.SendAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                LogRevocationFailed(logger, Key.Value, RevocationFailure.Refused, (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            LogRevocationFailed(logger, Key.Value, RevocationFailure.Transport, 0);
        }
        catch (TaskCanceledException)
        {
            LogRevocationFailed(logger, Key.Value, RevocationFailure.Timeout, 0);
        }
    }

    private static HttpRequestMessage ApiRequest(HttpMethod method, Uri endpoint, string accessToken)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        AddApiHeaders(request);
        return request;
    }

    private static void AddApiHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ApiMediaType));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
    }

    internal enum ExchangeFailure
    {
        Transport,
        Timeout,
        Malformed,
        TokenRefused,
        TokenBadVerificationCode,
        TokenIncorrectClientCredentials,
        TokenRedirectUriMismatch,
        UserRefused,
        SubjectUnusable,
        EmailsRefused,
    }

    internal enum EmailRefusal
    {
        UnverifiedAtToken,
        ListMalformed,
        NotBoolean,
        False,
        NoPrimary,
        PrimaryAmbiguous,
        NotAMailbox,
        AddressUnparsable,
    }

    internal enum RevocationFailure
    {
        Transport,
        Timeout,
        Refused,
    }

    // #1744's template, shared with the Google adapter: never the code, a token, the id or an address.
    [LoggerMessage(1022, LogLevel.Warning,
        "External login exchange failed at the provider: Provider={Provider} Cause={Cause} Status={Status}")]
    private static partial void LogExchangeFailed(ILogger logger, string provider, ExchangeFailure cause, int status);

    [LoggerMessage(1023, LogLevel.Warning,
        "External login refused: the provider's address is not usable: Provider={Provider} Cause={Cause}")]
    private static partial void LogEmailNotUsable(ILogger logger, string provider, EmailRefusal cause);

    // #1745 — the outcome never depends on it; a failed revocation leaves a token nothing stored.
    [LoggerMessage(1028, LogLevel.Warning,
        "External login token revocation failed: Provider={Provider} Cause={Cause} Status={Status}")]
    private static partial void LogRevocationFailed(ILogger logger, string provider, RevocationFailure cause, int status);
}
