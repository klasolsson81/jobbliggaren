using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// GitHub's server-side endpoints, scripted (#1745). The ONLY thing a test stubs on the GitHub login path: the
/// production adapter runs over this handler, so every identity a test sees has passed the adapter's own parse and
/// address rule (AGENTS.md §5 <c>Tests:</c>; test-writer, 6b form round §3.1).
/// <para>
/// The endpoints behave as GitHub documents them ("Authorizing OAuth apps", "Troubleshooting OAuth app access token
/// request errors", the REST reference for <c>/user</c> and <c>/user/emails</c> at API version 2026-03-10, all read
/// 2026-09-26):
/// <list type="bullet">
///   <item>The token endpoint takes a form-encoded POST and answers JSON only when <c>Accept</c> names
///   <c>application/json</c>, form-encoded otherwise. A refused code is answered with a SUCCESS status and an
///   <c>error</c> member (<c>bad_verification_code</c>, <c>incorrect_client_credentials</c>,
///   <c>redirect_uri_mismatch</c>, <c>unverified_user_email</c>).</item>
///   <item>The REST endpoints refuse a request without a <c>User-Agent</c> (403), an unknown API version (400) and a
///   Bearer this handler did not issue or has revoked (401). <c>/user/emails</c> honours <c>per_page</c> (default 30,
///   max 100) and names further pages in a <c>Link</c> header.</item>
///   <item>The revocation endpoint takes Basic <c>client_id:client_secret</c> and a JSON <c>access_token</c>, and
///   answers 204.</item>
/// </list>
/// DECLARED, not documented by GitHub: a verifier whose S256 does not equal the flow's challenge is answered
/// <c>bad_verification_code</c> (GitHub names no PKCE error code), and a code is consumed by its first exchange
/// (GitHub documents only that a code expires after ten minutes). No test asserts on either. Any other host throws,
/// so no test can reach the network.
/// </para>
/// </summary>
internal sealed class ScriptedGitHub(string clientId, string clientSecret) : HttpMessageHandler
{
    public const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    public const string UserEndpoint = "https://api.github.com/user";
    public const string EmailsEndpoint = "https://api.github.com/user/emails";

    // The two API versions GitHub's REST reference lists as supported, read 2026-09-26.
    private static readonly string[] SupportedApiVersions = ["2022-11-28", "2026-03-10"];

    private readonly object _gate = new();
    private readonly Dictionary<string, Grant> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Grant> _tokens = new(StringComparer.Ordinal);
    private readonly List<CapturedRequest> _requests = [];
    private readonly List<string> _revoked = [];
    private int _issued;

    public string ClientId { get; } = clientId;

    /// <summary>Every request the adapter sent, in order.</summary>
    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>The access tokens a revocation request invalidated, in order.</summary>
    public IReadOnlyList<string> Revoked
    {
        get
        {
            lock (_gate)
                return [.. _revoked];
        }
    }

    /// <summary>The token endpoint answers this documented error, with <see cref="TokenErrorStatus"/>.</summary>
    public string? TokenError { get; set; }

    /// <summary>The status a <see cref="TokenError"/> is answered with. GitHub's own practice is 200.</summary>
    public HttpStatusCode TokenErrorStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Forces the token endpoint to answer this status with a body that is not a token.</summary>
    public HttpStatusCode? TokenStatus { get; set; }

    /// <summary>Replaces the token endpoint's successful body, for shapes a middlebox could produce.</summary>
    public string? TokenBody { get; set; }

    /// <summary>A token without <c>expires_in</c> or a refresh token: an app that opted out of expiring tokens.</summary>
    public bool NonExpiring { get; set; }

    /// <summary>Forces <c>/user</c> to answer this status.</summary>
    public HttpStatusCode? UserStatus { get; set; }

    /// <summary>Forces <c>/user/emails</c> to answer this status.</summary>
    public HttpStatusCode? EmailsStatus { get; set; }

    /// <summary>Forces the revocation endpoint to answer this status.</summary>
    public HttpStatusCode? RevokeStatus { get; set; }

    /// <summary>Thrown instead of answering ANY request, for a transport failure or a client timeout.</summary>
    public Exception? Throw { get; set; }

    /// <summary>Thrown instead of answering the revocation request only.</summary>
    public Exception? RevokeThrow { get; set; }

    /// <summary>Runs when <c>/user/emails</c> is reached, before it answers (a caller cancelling mid-flow).</summary>
    public Action? OnEmails { get; set; }

    /// <summary>
    /// Registers a code GitHub handed the browser, the <c>/user</c> document and the <c>/user/emails</c> list its token
    /// will read, and, when given, the flow's S256 challenge and redirect URI the exchange must match.
    /// </summary>
    public void Expect(
        string code, string userJson, string emailsJson, string? codeChallenge = null, string? redirectUri = null)
    {
        lock (_gate)
            _codes[code] = new Grant(userJson, emailsJson, codeChallenge, redirectUri);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        var isForm = request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded";
        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri!,
            isForm ? ParseForm(body) : new Dictionary<string, string>(StringComparer.Ordinal),
            isForm ? null : body,
            request.Headers.Authorization?.ToString(),
            request.Headers.UserAgent.ToString(),
            request.Headers.Accept.ToString(),
            request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? string.Join(",", versions) : null);
        lock (_gate)
            _requests.Add(captured);

        if (Throw is { } exception)
            throw exception;

        var path = request.RequestUri!.GetLeftPart(UriPartial.Path);
        if (path == TokenEndpoint && request.Method == HttpMethod.Post)
            return Token(captured);

        if (path == UserEndpoint && request.Method == HttpMethod.Get)
            return Api(captured, UserStatus, grant => grant.UserJson);

        if (path == EmailsEndpoint && request.Method == HttpMethod.Get)
        {
            OnEmails?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Paged(captured, EmailsStatus, grant => Page(grant.EmailsJson, captured.Uri));
        }

        if (path == $"https://api.github.com/applications/{Uri.EscapeDataString(ClientId)}/token"
            && request.Method == HttpMethod.Delete)
        {
            if (RevokeThrow is { } revokeException)
                throw revokeException;
            return Revoke(captured);
        }

        throw new InvalidOperationException($"ScriptedGitHub refuses {request.Method} {request.RequestUri}.");
    }

    private HttpResponseMessage Token(CapturedRequest request)
    {
        if (TokenStatus is { } forced)
            return Json(forced, """{"message":"scripted"}""");

        if (TokenError is { } error)
            return Error(error, request, TokenErrorStatus);

        var form = request.Form;
        if (form.GetValueOrDefault("client_id") != ClientId || form.GetValueOrDefault("client_secret") != clientSecret)
            return Error("incorrect_client_credentials", request, HttpStatusCode.OK);

        Grant? grant;
        lock (_gate)
        {
            if (!form.TryGetValue("code", out var code) || !_codes.Remove(code, out grant))
                return Error("bad_verification_code", request, HttpStatusCode.OK);
        }

        if (grant.RedirectUri is not null && form.GetValueOrDefault("redirect_uri") != grant.RedirectUri)
            return Error("redirect_uri_mismatch", request, HttpStatusCode.OK);

        if (grant.CodeChallenge is not null
            && (!form.TryGetValue("code_verifier", out var verifier)
                || PkceVerifier.FromRaw(verifier).ToChallenge().Value != grant.CodeChallenge))
        {
            return Error("bad_verification_code", request, HttpStatusCode.OK);
        }

        string token;
        string refresh;
        lock (_gate)
        {
            _issued++;
            token = $"scripted-access-token-{_issued}";
            refresh = $"scripted-refresh-token-{_issued}";
            _tokens[token] = grant;
        }

        if (TokenBody is { } replaced)
            return Json(HttpStatusCode.OK, replaced);

        var answer = new JsonObject { ["access_token"] = token };
        if (!NonExpiring)
        {
            answer["expires_in"] = 28800;
            answer["refresh_token"] = refresh;
            answer["refresh_token_expires_in"] = 15897600;
        }

        answer["scope"] = "user:email";
        answer["token_type"] = "bearer";

        // GitHub's default is form-encoded; JSON only when the request asks for it.
        return request.Accept.Contains("application/json", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, answer.ToJsonString())
            : Form(answer);
    }

    private static HttpResponseMessage Error(string error, CapturedRequest request, HttpStatusCode status)
    {
        var answer = new JsonObject
        {
            ["error"] = error,
            ["error_description"] = ErrorDescriptionMarker,
            ["error_uri"] = "https://docs.github.com/apps/managing-oauth-apps/troubleshooting-oauth-app-access-token-request-errors",
        };
        return request.Accept.Contains("application/json", StringComparison.Ordinal)
            ? Json(status, answer.ToJsonString())
            : new HttpResponseMessage(status) { Content = FormContent(answer) };
    }

    /// <summary>A distinctive text inside every scripted error description, so a test can show it is never logged.</summary>
    public const string ErrorDescriptionMarker = "scripted-description-marker";

    private HttpResponseMessage Api(CapturedRequest request, HttpStatusCode? forced, Func<Grant, string> read) =>
        Paged(request, forced, grant => (read(grant), null));

    private HttpResponseMessage Paged(
        CapturedRequest request, HttpStatusCode? forced, Func<Grant, (string Body, string? Link)> read)
    {
        if (string.IsNullOrWhiteSpace(request.UserAgent))
        {
            return Json(
                HttpStatusCode.Forbidden,
                """{"message":"Request forbidden by administrative rules. Please make sure your request has a User-Agent header"}""");
        }

        if (request.ApiVersion is { } version && !SupportedApiVersions.Contains(version, StringComparer.Ordinal))
            return Json(HttpStatusCode.BadRequest, """{"message":"Unsupported 'X-GitHub-Api-Version'"}""");

        if (forced is { } status)
            return Json(status, """{"message":"scripted"}""");

        Grant? grant = null;
        var bearer = request.Authorization is { } authorization && authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..]
            : null;
        lock (_gate)
        {
            if (bearer is null || _revoked.Contains(bearer) || !_tokens.TryGetValue(bearer, out grant))
                return Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        }

        var (body, link) = read(grant);
        var response = Json(HttpStatusCode.OK, body);
        if (link is not null)
            response.Headers.Add("Link", link);
        return response;
    }

    // /user/emails honours per_page (default 30, max 100) and names the next page in a Link header, as GitHub does.
    private static (string Body, string? Link) Page(string emailsJson, Uri uri)
    {
        if (JsonNode.Parse(emailsJson) is not JsonArray all)
            return (emailsJson, null);

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var perPage = Math.Min(int.TryParse(query["per_page"], out var asked) && asked > 0 ? asked : 30, 100);
        if (all.Count <= perPage)
            return (emailsJson, null);

        var first = new JsonArray([.. all.Take(perPage).Select(entry => entry?.DeepClone())]);
        return (first.ToJsonString(), $"<{EmailsEndpoint}?per_page={perPage}&page=2>; rel=\"next\"");
    }

    private HttpResponseMessage Revoke(CapturedRequest request)
    {
        if (RevokeStatus is { } forced)
            return Json(forced, """{"message":"scripted"}""");

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{clientSecret}"));
        if (request.Authorization != expected)
            return Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        string? token;
        try
        {
            token = JsonNode.Parse(request.JsonBody ?? string.Empty)?["access_token"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            token = null;
        }

        lock (_gate)
        {
            if (token is null || !_tokens.ContainsKey(token))
                return Json(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed"}""");
            _revoked.Add(token);
        }

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Form(JsonObject answer) =>
        new(HttpStatusCode.OK) { Content = FormContent(answer) };

    private static FormUrlEncodedContent FormContent(JsonObject answer) =>
        new(answer.Select(p => new KeyValuePair<string, string>(p.Key, p.Value!.ToString())));

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty,
                StringComparer.Ordinal);

    internal sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Form,
        string? JsonBody,
        string? Authorization,
        string UserAgent,
        string Accept,
        string? ApiVersion);

    private sealed record Grant(string UserJson, string EmailsJson, string? CodeChallenge, string? RedirectUri);
}
