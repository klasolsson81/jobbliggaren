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
/// implementations in Infrastructure so a new one breaks the build until it is deliberately allowlisted —
/// the same pattern as the audit-bypass gates (ADR 0024 D1).
/// </para>
///
/// <para>
/// Since #746 PR-B the allowlist is EMPTY: role resolution was moved out of the eager
/// <c>SessionRoleClaimsTransformation</c> (deleted) and into the on-demand Api-layer
/// <c>AdminRoleAuthorizationHandler</c>, so no <c>IClaimsTransformation</c> exists any more. The guard is
/// therefore STRICTER now — any newly introduced transformation must be reviewed and allowlisted. The
/// privilege-granting authorization handlers are guarded in parallel by
/// <see cref="AuthorizationHandlerAllowlistTests"/>.
/// </para>
/// </summary>
public class ClaimsTransformationAllowlistTests
{
    [Fact]
    public void IClaimsTransformation_implementations_in_Infrastructure_must_be_in_allowlist()
    {
        var allowed = System.Array.Empty<string>();

        var implementations = typeof(AppDbContext).Assembly
            .GetTypes()
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
