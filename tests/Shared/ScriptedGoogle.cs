using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// Google's two server-side endpoints, scripted (#1744). The ONLY thing a test stubs on the external-login path:
/// the production adapter runs over this handler, so every identity a test sees has passed the adapter's own
/// parse and authority rule (test-writer form round, Major 1; AGENTS.md §5 <c>Tests:</c>).
/// <para>
/// The token endpoint behaves as Google documents it: form-encoded POST, a code valid once, <c>invalid_grant</c> for
/// an unknown or used code, and, when the test registered them, the S256 of the presented verifier must equal the
/// flow's challenge and the <c>redirect_uri</c> must equal the one the flow was started with (Google, "OAuth 2.0 for
/// Web Server Applications", read 2026-09-25). userinfo answers only a Bearer this handler issued. Any other host
/// throws, so no test can reach the network.
/// </para>
/// </summary>
internal sealed class ScriptedGoogle : HttpMessageHandler
{
    private readonly Dictionary<string, Grant> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private int _issued;

    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>Forces the token endpoint to answer this status (with Google's error body shape).</summary>
    public HttpStatusCode? TokenStatus { get; set; }

    /// <summary>Forces the userinfo endpoint to answer this status.</summary>
    public HttpStatusCode? UserInfoStatus { get; set; }

    /// <summary>Replaces the token endpoint's successful body, for shapes a middlebox could produce.</summary>
    public string? TokenBody { get; set; }

    /// <summary>Thrown instead of answering, for transport failures and client timeouts.</summary>
    public Exception? Throw { get; set; }

    /// <summary>Registers a code Google handed the browser, and the userinfo document its token will read.</summary>
    public void Expect(string code, string userInfoJson, string? codeChallenge = null, string? redirectUri = null) =>
        _codes[code] = new Grant(userInfoJson, codeChallenge, redirectUri);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var form = request.Content is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ParseForm(await request.Content.ReadAsStringAsync(ct));
        Requests.Add(new CapturedRequest(
            request.Method, request.RequestUri!, form, request.Headers.Authorization?.ToString()));

        if (Throw is { } exception)
            throw exception;

        return request.RequestUri!.GetLeftPart(UriPartial.Path) switch
        {
            "https://oauth2.googleapis.com/token" when request.Method == HttpMethod.Post => Token(form),
            "https://openidconnect.googleapis.com/v1/userinfo" when request.Method == HttpMethod.Get =>
                UserInfo(request.Headers.Authorization?.Parameter),
            _ => throw new InvalidOperationException($"ScriptedGoogle refuses {request.Method} {request.RequestUri}."),
        };
    }

    private HttpResponseMessage Token(Dictionary<string, string> form)
    {
        if (TokenStatus is { } forced)
            return Json(forced, """{"error":"invalid_grant","error_description":"scripted"}""");

        // The code is consumed by the attempt, as Google's is: a second exchange of it is refused.
        if (!form.TryGetValue("code", out var code) || !_codes.Remove(code, out var grant))
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"scripted"}""");

        var valid = form.GetValueOrDefault("grant_type") == "authorization_code"
                    && !string.IsNullOrEmpty(form.GetValueOrDefault("client_id"))
                    && !string.IsNullOrEmpty(form.GetValueOrDefault("client_secret"))
                    && form.TryGetValue("code_verifier", out var verifier)
                    && (grant.CodeChallenge is null
                        || PkceVerifier.FromRaw(verifier).ToChallenge().Value == grant.CodeChallenge)
                    && (grant.RedirectUri is null || form.GetValueOrDefault("redirect_uri") == grant.RedirectUri);

        if (!valid)
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"scripted"}""");

        var token = $"scripted-access-token-{++_issued}";
        _tokens[token] = grant.UserInfoJson;
        return Json(HttpStatusCode.OK, TokenBody ?? new JsonObject
        {
            ["access_token"] = token,
            ["expires_in"] = 3599,
            ["scope"] = "openid https://www.googleapis.com/auth/userinfo.email",
            ["token_type"] = "Bearer",
        }.ToJsonString());
    }

    private HttpResponseMessage UserInfo(string? bearer)
    {
        if (UserInfoStatus is { } forced)
            return Json(forced, """{"error":"invalid_token"}""");

        return bearer is not null && _tokens.TryGetValue(bearer, out var json)
            ? Json(HttpStatusCode.OK, json)
            : Json(HttpStatusCode.Unauthorized, """{"error":"invalid_token"}""");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty,
                StringComparer.Ordinal);

    internal sealed record CapturedRequest(
        HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Form, string? Authorization);

    private sealed record Grant(string UserInfoJson, string? CodeChallenge, string? RedirectUri);
}
