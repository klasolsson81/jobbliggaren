using System.Security.Claims;
using JobbPilot.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Infrastructure.Auth;

/// <summary>
/// Lägger till <c>ClaimTypes.Role</c>-claims på autentiserade ClaimsPrincipals
/// per request. Körs av ASP.NET Core authentication-middleware efter
/// <see cref="SessionAuthenticationHandler"/> men före authorization-checks.
///
/// <para>
/// H-3 SoC-split (arch-audit 2026-05-11): role-resolution flyttades från
/// SessionAuthenticationHandler så auth-handler:n bara hanterar session-validation.
/// Roller är cross-cutting concern som naturligt hör hemma i
/// <see cref="IClaimsTransformation"/>-extension-punkten — inte i protokoll-handler:n.
/// </para>
///
/// <para>
/// Bibehåller per-request-fetch-modellen från senior-cto-advisor-beslut 2026-05-11 (A1):
/// roll-revoke verkar omedelbart, ingen cache (security-first). Implementeringen är
/// idempotent — om Role-claims redan finns på principalen (t.ex. testfixture som
/// promoterar direkt utan ny request) skippar transformationen DB-anropet.
/// </para>
/// </summary>
public sealed partial class SessionRoleClaimsTransformation(
    IUserAccountService userAccountService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<SessionRoleClaimsTransformation> logger) : IClaimsTransformation
{
    // Sentinel-claim som markerar att transformation redan kört för denna principal.
    // ClaimsTransformation kan köras flera gånger per request (ASP.NET-pattern,
    // särskilt vid status-code re-execution). Idempotency-check på faktiska
    // Role-claims är otillförlitlig — användare som promoteras mitt under session
    // har stale roller på principalen och skulle skippas felaktigt.
    private const string RolesResolvedClaim = "jobbpilot:roles_resolved";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        // Idempotent re-run-guard: sentinel-claim sätts post-fetch (även om 0 roller).
        if (principal.HasClaim(c => c.Type == RolesResolvedClaim))
            return principal;

        // Defensiv cast — alla auth-handlers i JobbPilot konstruerar ClaimsIdentity,
        // men annan IIdentity-impl skulle bryta AddClaim-anropet nedan.
        if (principal.Identity is not ClaimsIdentity identity)
            return principal;

        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return principal;

        // Använd HttpContext.RequestAborted så role-fetch respekterar client-disconnect
        // (resurs-läckage-skydd — Identity-DB-query avbryts om request kanceleras).
        var ct = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

        IReadOnlyList<string> roles;
        try
        {
            roles = await userAccountService.GetRolesAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sec-Minor-2-paritet (security-auditor 2026-05-11): role-fetch-fel ska
            // INTE avslöja infra-state. Logga, returnera principal utan Role-claims
            // → authorization-policy fail → 403 (eller 401 i AuthorizationBehavior).
            // Auth-protokollet håller sig stängt.
            LogRoleResolutionFailed(logger, ex, userId);
            return principal;
        }

        // In-place AddClaim på ClaimsIdentity — ASP.NET-request-pipelinen är
        // single-threaded per request så detta är säkert. Sentinel sätts oavsett
        // roll-antal så idempotency-guarden träffar nästa transformation-pass.
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(RolesResolvedClaim, "1"));

        return principal;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Role resolution failed for user {UserId}")]
    private static partial void LogRoleResolutionFailed(ILogger logger, Exception ex, Guid userId);
}
