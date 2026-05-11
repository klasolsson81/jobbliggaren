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
///
/// <para>
/// H-3 SoC-split (arch-audit 2026-05-11): role-resolution flyttad till
/// <see cref="SessionRoleClaimsTransformation"/>. Auth-handler:n hanterar bara
/// session-id-parse + Redis-lookup + identity-konstruktion. Roller appliceras
/// post-authentication via IClaimsTransformation-extension-punkten.
/// </para>
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<SessionAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionStore sessionStore)
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

        // Roll-claims appliceras post-authentication av SessionRoleClaimsTransformation
        // (H-3 SoC-split). Per-request-fetch-modellen bibehållen — roll-revoke verkar
        // omedelbart utan session-cache (senior-cto-advisor 2026-05-11 A1).

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Store session-id so endpoints (e.g. logout) can retrieve it without re-parsing the header.
        Context.Items["SessionId"] = sessionId;

        return AuthenticateResult.Success(ticket);
    }
}
