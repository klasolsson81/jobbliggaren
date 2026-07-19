using System.Security.Claims;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Jobbliggaren.Api.Authorization;

/// <summary>
/// Resolves the current user's roles ON DEMAND when the Admin policy is evaluated (#746 PR-B), and
/// attaches <c>ClaimTypes.Role</c> to the request principal so the downstream Mediator
/// <c>AdminAuthorizationBehavior</c> (which reads <c>ICurrentUser.IsInRole</c> → <c>HttpContext.User.IsInRole</c>)
/// still sees the role — defense-in-depth preserved.
///
/// <para>
/// This replaces the eager <c>SessionRoleClaimsTransformation</c> (an <c>IClaimsTransformation</c> that
/// ran on EVERY authenticated request). The sole consumer of <c>ClaimTypes.Role</c> is the Admin path,
/// so resolving here means non-admin requests — the whole /oversikt fan-out — and 429'd floods pay ZERO
/// identity queries (epic #737 findings d2/d4). In endpoint routing <c>AuthorizationHandlerContext.User</c>
/// is the same reference as <c>HttpContext.User</c>, so the in-place <c>AddClaim</c> is visible downstream.
/// </para>
///
/// <para>
/// Immediate-revoke (senior-cto-advisor A1 2026-05-11) is preserved: roles are resolved fresh per policy
/// evaluation, with NO caching. A role revoke takes effect on the next request.
/// </para>
/// </summary>
public sealed partial class AdminRoleAuthorizationHandler(
    IUserAccountService userAccountService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AdminRoleAuthorizationHandler> logger)
    : AuthorizationHandler<AdminRoleRequirement>
{
    // Sentinel marking that roles were already resolved this request, so a policy re-evaluation
    // (e.g. status-code re-execution) neither re-queries nor double-attaches Role claims. Mirrors the
    // idempotency sentinel the removed SessionRoleClaimsTransformation carried.
    private const string RolesResolvedClaim = "jobbliggaren:roles_resolved";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRoleRequirement requirement)
    {
        var principal = context.User;

        // Unauthenticated → never resolve; the requirement stays unmet → 401 challenge (never a DB call).
        if (principal.Identity?.IsAuthenticated != true)
            return;

        // Fixture-injection compat + fast path: a principal that ALREADY carries the Admin role claim
        // (a test host promoting directly; or an admin whose roles were resolved earlier this request)
        // authorizes WITHOUT a DB call. Idempotent — no double-attach on a re-run.
        if (principal.IsInRole(Roles.Admin))
        {
            context.Succeed(requirement);
            return;
        }

        // Idempotent re-run guard: if roles were already resolved this request and Admin was absent, the
        // user simply is not an admin — do not re-query.
        if (principal.HasClaim(c => c.Type == RolesResolvedClaim))
            return;

        // Defensive: every session principal is a ClaimsIdentity, but a foreign IIdentity would break AddClaim.
        if (principal.Identity is not ClaimsIdentity identity)
            return;

        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return;

        // RequestAborted (via IHttpContextAccessor, parity with the old transformation + CurrentUser) so
        // the role fetch respects client-disconnect. Read from the accessor rather than
        // context.Resource so it does not depend on the resource being an HttpContext (which the
        // SuppressUseHttpContextAsAuthorizationResource switch could change).
        var ct = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

        IReadOnlyList<string> roles;
        try
        {
            roles = await userAccountService.GetRolesAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed (Sec-Minor-2 parity): do not reveal infra state. Log, leave the requirement
            // unmet → 403. The auth protocol stays closed.
            LogRoleResolutionFailed(logger, ex, userId);
            return;
        }

        // In-place AddClaim on the request principal's identity — the ASP.NET request pipeline is
        // single-threaded per request. Sentinel is stamped regardless of role count so a re-run short-circuits.
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(RolesResolvedClaim, "1"));

        if (principal.IsInRole(Roles.Admin))
            context.Succeed(requirement);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Admin role resolution failed for user {UserId}")]
    private static partial void LogRoleResolutionFailed(ILogger logger, Exception ex, Guid userId);
}
