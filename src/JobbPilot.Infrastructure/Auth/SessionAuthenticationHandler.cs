using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using JobbPilot.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Auth;

/// <summary>
/// Authenticates requests via opaque Redis session-id from Authorization: Bearer header.
///
/// Timing model: SessionId is 256-bit CSPRNG. Redis GET is O(1) hash-table lookup.
/// Timing variance (hit vs miss) is dominated by network jitter — not exploitable for
/// session-id enumeration. Constant-time comparison is not applicable here because
/// we use the session-id as a lookup key, not as a value to compare against a known secret.
///
/// Scheme name "Bearer" reflects wire-format (RFC 6750), not token type.
/// Renamed to "Session" in Fas 1 when JWT classes are removed (ADR 0017).
/// </summary>
public sealed partial class SessionAuthenticationHandler(
    IOptionsMonitor<SessionAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionStore sessionStore,
    IUserAccountService userAccountService)
    : AuthenticationHandler<SessionAuthenticationSchemeOptions>(options, logger, encoder)
{
    private const int MinSessionIdLength = 16;
    private const int MaxSessionIdLength = 256;

    private static readonly Regex Base64UrlRegex =
        new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            return AuthenticateResult.NoResult();

        var headerValue = headerValues.ToString();
        if (!AuthenticationHeaderValue.TryParse(headerValue, out var auth))
            return AuthenticateResult.NoResult();

        if (!"Bearer".Equals(auth.Scheme, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        if (string.IsNullOrWhiteSpace(auth.Parameter))
            return AuthenticateResult.Fail("Empty bearer token");

        if (auth.Parameter.Length is < MinSessionIdLength or > MaxSessionIdLength)
            return AuthenticateResult.Fail("Bearer token length out of bounds");

        if (!Base64UrlRegex.IsMatch(auth.Parameter))
            return AuthenticateResult.Fail("Bearer token contains invalid characters");

        // SessionStoreUnavailableException intentionally NOT caught here —
        // it bubbles to the 503-mapping middleware in Program.cs.
        var sessionId = SessionId.FromRaw(auth.Parameter);
        var session = await sessionStore.GetAsync(sessionId, Context.RequestAborted);

        if (session is null)
            return AuthenticateResult.Fail("Session not found or expired");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            // TODO Fas 1: byt JwtRegisteredClaimNames.Sub till NameIdentifier
            //   när JWT-klasser raderas. Behålls nu för bakåtkompatibilitet med CurrentUser.cs.
            new(JwtRegisteredClaimNames.Sub, session.UserId.ToString()),
            new("session_id_prefix", session.Id.ToString()), // 6-char prefix + "…", never raw value
        };

        // Per-request roll-fetch (senior-cto-advisor-beslut 2026-05-11, A1 över A2).
        // Roll-revoke verkar omedelbart — ingen session-cache (CTO Regel 1: security-first).
        // Kostnad: 1 DB-query per autentiserat request. UserManager har request-scope-cache
        // (samma request kostar bara en query oavsett hur många GetRolesAsync-anrop).
        // Om volym blir verifierat problem: lokal cache i SessionAuthenticationHandler
        // med kort TTL kan införas — utan att röra Session-kontraktet.
        //
        // Sec-Minor-2 (security-auditor 2026-05-11): role-fetch-fel ska INTE bubbla
        // till exception-middleware (→ 500). Fail som AuthenticateResult.Fail → 401
        // för att hålla felet inom auth-protokollet och inte avslöja infra-state.
        IReadOnlyList<string> roles;
        try
        {
            roles = await userAccountService.GetRolesAsync(session.UserId, Context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRoleResolutionFailed(Logger, ex, session.Id.ToString());
            return AuthenticateResult.Fail("Role resolution failed");
        }

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Store session-id so endpoints (e.g. logout) can retrieve it without re-parsing the header.
        Context.Items["SessionId"] = sessionId;

        return AuthenticateResult.Success(ticket);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Role resolution failed for session {SessionPrefix}")]
    private static partial void LogRoleResolutionFailed(ILogger logger, Exception ex, string sessionPrefix);
}
