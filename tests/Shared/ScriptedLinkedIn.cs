using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// LinkedIn's server-side endpoints, scripted (#1746). The ONLY thing a test stubs on the LinkedIn login path: the
/// production adapter runs over this handler, so every identity a test sees has passed the adapter's own parse and
/// address rule (AGENTS.md §5 <c>Tests:</c>; test-writer, 6c form round §3.1).
/// <para>
/// The endpoints behave as LinkedIn documents them ("LinkedIn 3-Legged OAuth Flow", updated 2026-05-15, and "Sign In
/// with LinkedIn using OpenID Connect", updated 2024-08-08, both read 2026-09-27): the token endpoint takes a
/// form-encoded POST and refuses a code under a 4xx with an <c>error</c> member (401 <c>invalid_request</c> for a code
/// it cannot find, 400 <c>invalid_redirect_uri</c>, 400 <c>invalid_request</c> for a missing parameter); a token lives
/// 5 184 000 seconds and comes with an id_token under scope <c>openid</c>; userinfo answers a Bearer it issued, and 401
/// otherwise.
/// </para>
/// <para>
/// DECLARED, not documented by LinkedIn: a <c>code_verifier</c> in a confidential client's request, and a wrong client
/// secret, are answered 401 <c>invalid_client</c> (a third-party measurement of 2026-09-21,
/// UsefulSoftwareCo/executor#2087); the error body is <c>{error, error_description}</c> (RFC 6749 §5.2); a code is
/// consumed by its first exchange; and the id_token echoes the authorization request's <c>nonce</c>, which LinkedIn's
/// id_token table does not list and OIDC Core §2 requires ("If present in the Authentication Request, Authorization
/// Servers MUST include a nonce Claim"). Its <c>iss</c> is the live discovery document's value. The id_token is built
/// here from JSON at run time and its signature segment is a marker nothing verifies. Any other host throws, so no test
/// can reach the network, fetch a JWKS, or revoke.
/// </para>
/// </summary>
internal sealed class ScriptedLinkedIn(string clientId, string clientSecret) : HttpMessageHandler
{
    public const string TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";
    public const string UserInfoEndpoint = "https://api.linkedin.com/v2/userinfo";

    /// <summary>The live discovery document's issuer (read 2026-09-27).</summary>
    public const string DiscoveryIssuer = "https://www.linkedin.com/oauth";

    /// <summary>The issuer LinkedIn's id_token table and its discovery example still show (read 2026-09-27).</summary>
    public const string DocumentedIssuer = "https://www.linkedin.com";

    /// <summary>A distinctive text inside every scripted error description, so a test can show it is never logged.</summary>
    public const string ErrorDescriptionMarker = "scripted-description-marker";

    private const long IssuedAt = 1790000000;

    private readonly object _gate = new();
    private readonly Dictionary<string, Grant> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Grant> _tokens = new(StringComparer.Ordinal);
    private readonly List<CapturedRequest> _requests = [];
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

    /// <summary>Forces the token endpoint to answer this status with a body that is not a token.</summary>
    public HttpStatusCode? TokenStatus { get; set; }

    /// <summary>Replaces the token endpoint's successful body, for shapes a middlebox could produce.</summary>
    public string? TokenBody { get; set; }

    /// <summary>The access token's length; LinkedIn says about 500 and to plan for at least 1 000.</summary>
    public int TokenLength { get; set; } = 500;

    /// <summary>Changes the issued id_token's claims before they are encoded; returning null leaves the id_token out.</summary>
    public Func<JsonObject, JsonObject?>? IdTokenClaims { get; set; }

    /// <summary>Replaces the issued id_token's text whole, for tokens that are not three base64url segments.</summary>
    public string? IdTokenText { get; set; }

    /// <summary>Forces userinfo to answer this status.</summary>
    public HttpStatusCode? UserInfoStatus { get; set; }

    /// <summary>Thrown instead of answering ANY request, for a transport failure or a client timeout.</summary>
    public Exception? Throw { get; set; }

    /// <summary>Runs when userinfo is reached, before it answers (a caller cancelling mid-flow).</summary>
    public Action? OnUserInfo { get; set; }

    /// <summary>
    /// Registers a code LinkedIn handed the browser and the userinfo document its token will read. The id_token issued
    /// for it echoes <paramref name="nonce"/> when given, and carries <paramref name="idTokenSubject"/>, or else the
    /// document's <c>sub</c>, as its subject; <paramref name="redirectUri"/>, when given, must match the exchange's.
    /// </summary>
    public void Expect(
        string code, string userInfoJson, string? nonce = null, string? redirectUri = null, string? idTokenSubject = null)
    {
        lock (_gate)
            _codes[code] = new Grant(userInfoJson, nonce, redirectUri, idTokenSubject ?? SubjectOf(userInfoJson));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        var contentType = request.Content?.Headers.ContentType?.MediaType;
        var isForm = contentType == "application/x-www-form-urlencoded";
        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri!,
            contentType,
            isForm ? ParseForm(body) : new Dictionary<string, string>(StringComparer.Ordinal),
            request.Headers.Authorization?.ToString(),
            request.Headers.Accept.ToString());
        lock (_gate)
            _requests.Add(captured);

        if (Throw is { } exception)
            throw exception;

        var path = request.RequestUri!.GetLeftPart(UriPartial.Path);
        if (path == TokenEndpoint && request.Method == HttpMethod.Post)
            return Token(captured, isForm);

        if (path == UserInfoEndpoint && request.Method == HttpMethod.Get)
        {
            OnUserInfo?.Invoke();
            ct.ThrowIfCancellationRequested();
            return UserInfo(captured);
        }

        throw new InvalidOperationException($"ScriptedLinkedIn refuses {request.Method} {request.RequestUri}.");
    }

    private HttpResponseMessage Token(CapturedRequest request, bool isForm)
    {
        if (TokenStatus is { } forced)
            return Json(forced, """{"message":"scripted"}""");

        var form = request.Form;
        string[] required = ["grant_type", "code", "client_id", "client_secret", "redirect_uri"];
        if (!isForm || required.Any(name => !form.ContainsKey(name)))
            return Error(HttpStatusCode.BadRequest, "invalid_request");

        if (form.ContainsKey("code_verifier")
            || form["client_id"] != ClientId
            || form["client_secret"] != clientSecret)
        {
            return Error(HttpStatusCode.Unauthorized, "invalid_client");
        }

        Grant? grant;
        lock (_gate)
        {
            if (!_codes.Remove(form["code"], out grant))
                return Error(HttpStatusCode.Unauthorized, "invalid_request");
        }

        if (grant.RedirectUri is not null && form["redirect_uri"] != grant.RedirectUri)
            return Error(HttpStatusCode.BadRequest, "invalid_redirect_uri");

        string token;
        lock (_gate)
        {
            _issued++;
            var stem = $"scripted-access-token-{_issued}-";
            token = stem + new string('x', Math.Max(0, TokenLength - stem.Length));
            _tokens[token] = grant;
        }

        if (TokenBody is { } replaced)
            return Json(HttpStatusCode.OK, replaced);

        var answer = new JsonObject
        {
            ["access_token"] = token,
            ["expires_in"] = 5184000,
            ["scope"] = "email,openid",
        };
        if (IdTokenText is { } text)
            answer["id_token"] = text;
        else if (IdToken(grant) is { } idToken)
            answer["id_token"] = idToken;

        return Json(HttpStatusCode.OK, answer.ToJsonString());
    }

    private string? IdToken(Grant grant)
    {
        var claims = new JsonObject
        {
            ["iss"] = DiscoveryIssuer,
            ["aud"] = ClientId,
            ["iat"] = IssuedAt,
            ["exp"] = IssuedAt + 3600,
            ["sub"] = grant.IdTokenSubject,
        };
        if (grant.Nonce is not null)
            claims["nonce"] = grant.Nonce;

        var payload = IdTokenClaims is { } change ? change(claims) : claims;
        if (payload is null)
            return null;

        return string.Join(
            '.',
            Encode("""{"alg":"RS256","kid":"scripted"}"""),
            Encode(payload.ToJsonString()),
            Encode("scripted-signature-never-verified"));
    }

    private static string Encode(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    private HttpResponseMessage UserInfo(CapturedRequest request)
    {
        if (UserInfoStatus is { } forced)
            return Json(forced, """{"message":"scripted"}""");

        var bearer = request.Authorization is { } authorization && authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..]
            : null;
        Grant? grant;
        lock (_gate)
        {
            if (bearer is null || !_tokens.TryGetValue(bearer, out grant))
                return Json(HttpStatusCode.Unauthorized, """{"serviceErrorCode":65600,"message":"Invalid access token","status":401}""");
        }

        return Json(HttpStatusCode.OK, grant.UserInfoJson);
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string error) =>
        Json(status, new JsonObject { ["error"] = error, ["error_description"] = ErrorDescriptionMarker }.ToJsonString());

    private static string SubjectOf(string userInfoJson)
    {
        try
        {
            return JsonNode.Parse(userInfoJson) is JsonObject document
                   && document["sub"] is JsonValue value
                   && value.TryGetValue<string>(out var sub)
                ? sub
                : LinkedInUserInfoShapes.DocumentedSub;
        }
        catch (System.Text.Json.JsonException)
        {
            return LinkedInUserInfoShapes.DocumentedSub;
        }
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
        HttpMethod Method,
        Uri Uri,
        string? ContentType,
        IReadOnlyDictionary<string, string> Form,
        string? Authorization,
        string Accept);

    private sealed record Grant(string UserInfoJson, string? Nonce, string? RedirectUri, string IdTokenSubject);
}
