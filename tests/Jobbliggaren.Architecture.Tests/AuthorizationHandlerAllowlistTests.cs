using Jobbliggaren.Api.Authorization;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Architecture rule for <see cref="IAuthorizationHandler"/> implementations (#746 PR-B).
///
/// <para>
/// An <c>IAuthorizationHandler</c> can grant privileges — <c>AdminRoleAuthorizationHandler</c> resolves
/// roles and attaches <c>ClaimTypes.Role</c> to the principal to satisfy the Admin policy — so it is
/// exactly as security-critical as the <c>IClaimsTransformation</c> surface it replaced. This locks the
/// set of allowed handlers so a new privilege-granting handler breaks the build until it is deliberately
/// reviewed and allowlisted. The guard the role-resolution logic used to have
/// (<see cref="ClaimsTransformationAllowlistTests"/>) follows the logic to its new home — the guarded
/// surface is moved, never silenced.
/// </para>
/// </summary>
public class AuthorizationHandlerAllowlistTests
{
    [Fact]
    public void IAuthorizationHandler_implementations_must_be_in_allowlist()
    {
        var allowed = new[] { "AdminRoleAuthorizationHandler" };

        // Scan both Api (where AdminRoleAuthorizationHandler lives) AND Infrastructure (which may reference
        // Microsoft.AspNetCore.Authorization too), so a privilege-granting handler in either layer is caught.
        var implementations = new[] { typeof(AdminRoleRequirement).Assembly, typeof(AppDbContext).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IAuthorizationHandler).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        // Counterfactual anchor: the guard must be wired to a real handler — if AdminRoleAuthorizationHandler
        // is renamed/removed the sanity fails, so the empty-diff below can never be vacuously green.
        implementations.ShouldContain(
            "AdminRoleAuthorizationHandler",
            "Sanity: the Admin role authorization handler must exist in the scanned assemblies.");

        var unauthorized = implementations.Where(impl => !allowed.Contains(impl)).ToList();

        unauthorized.ShouldBeEmpty(
            "A new IAuthorizationHandler requires explicit review before being added to this allowlist — " +
            "such a handler can grant privileges (attach Role claims, satisfy a policy). " +
            $"Unauthorized: {string.Join(", ", unauthorized)}");
    }
}
