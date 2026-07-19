using Jobbliggaren.Api.Authorization;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Architecture rule for <see cref="IClaimsTransformation"/> consumption (H-3 hardening 2026-05-11).
///
/// <para>
/// An <c>IClaimsTransformation</c> runs after authentication-success on every authenticated request and
/// can add Role claims or other privilege claims — a security-critical extension point (impersonation
/// promote, test-only role injectors, federated IdP claim mapping). This test locks the set of allowed
/// implementations so a new one breaks the build until it is deliberately allowlisted — the same pattern
/// as the audit-bypass gates (ADR 0024 D1).
/// </para>
///
/// <para>
/// Since #746 PR-B the allowlist is EMPTY: role resolution was moved out of the eager
/// <c>SessionRoleClaimsTransformation</c> (deleted) into the on-demand Api-layer
/// <c>AdminRoleAuthorizationHandler</c>, so no <c>IClaimsTransformation</c> exists any more. The guard is
/// therefore STRICTER now. The privilege-granting authorization handlers are guarded in parallel by
/// <see cref="AuthorizationHandlerAllowlistTests"/>, whose <c>ShouldContain</c> anchor exercises the same
/// <c>GetTypes()</c>/<c>IsAssignableFrom</c> reflection — so a silently-broken scan here (which would
/// otherwise stay vacuously green with an empty allowlist) would surface there.
/// </para>
/// </summary>
public class ClaimsTransformationAllowlistTests
{
    [Fact]
    public void No_IClaimsTransformation_may_exist_outside_the_allowlist()
    {
        var allowed = System.Array.Empty<string>();

        // Scan both Api and Infrastructure — an IClaimsTransformation in either layer must be reviewed and
        // allowlisted before it can add privilege claims.
        var implementations = new[] { typeof(AdminRoleRequirement).Assembly, typeof(AppDbContext).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IClaimsTransformation).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        var unauthorized = implementations.Where(impl => !allowed.Contains(impl)).ToList();

        unauthorized.ShouldBeEmpty(
            "A new IClaimsTransformation requires explicit review before being added to this allowlist. " +
            "The auth pipeline is security-critical — a transformation can add Role claims or other " +
            "privilege claims. Since #746 PR-B role resolution lives in AdminRoleAuthorizationHandler " +
            $"(guarded by AuthorizationHandlerAllowlistTests), so this allowlist is empty. Unauthorized: {string.Join(", ", unauthorized)}");
    }
}
