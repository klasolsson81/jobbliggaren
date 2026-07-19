using System.Security.Claims;
using Jobbliggaren.Api.Authorization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// Unit tests for <see cref="AdminRoleAuthorizationHandler"/> (#746 PR-B) — deterministic, no host or
/// containers. Covers the branches integration cannot cheaply reach: the SECURITY-BEARING fail-closed
/// path (a role-resolution exception must deny, never grant or 500), the fixture fast-path (a principal
/// already carrying the Admin claim authorizes with no DB call), idempotency (exactly one query across
/// repeated evaluations), and the defensive guards.
/// </summary>
public class AdminRoleAuthorizationHandlerTests
{
    private readonly IUserAccountService _userAccountService = Substitute.For<IUserAccountService>();
    private readonly Guid _userId = Guid.NewGuid();

    private AdminRoleAuthorizationHandler CreateHandler() =>
        new(_userAccountService, Substitute.For<IHttpContextAccessor>(), NullLogger<AdminRoleAuthorizationHandler>.Instance);

    private static AuthorizationHandlerContext ContextFor(ClaimsPrincipal principal) =>
        new([new AdminRoleRequirement()], principal, resource: null);

    private ClaimsPrincipal AuthenticatedPrincipal(params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, _userId.ToString()) };
        claims.AddRange(extra);
        // A non-null authenticationType makes IsAuthenticated == true; roleType defaults to ClaimTypes.Role.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
    }

    [Fact]
    public async Task Succeeds_and_attaches_role_when_user_is_admin()
    {
        IReadOnlyList<string> adminRoles = [Roles.Admin];
        _userAccountService.GetRolesAsync(_userId, Arg.Any<CancellationToken>()).Returns(adminRoles);
        var principal = AuthenticatedPrincipal();
        var context = ContextFor(principal);

        await CreateHandler().HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue();
        // Role attached to the live principal → the downstream Mediator AdminAuthorizationBehavior sees it.
        principal.IsInRole(Roles.Admin).ShouldBeTrue();
    }

    [Fact]
    public async Task Does_not_succeed_when_user_has_no_admin_role()
    {
        IReadOnlyList<string> noRoles = [];
        _userAccountService.GetRolesAsync(_userId, Arg.Any<CancellationToken>()).Returns(noRoles);

        var context = ContextFor(AuthenticatedPrincipal());
        await CreateHandler().HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Fails_closed_when_role_resolution_throws()
    {
        _userAccountService.GetRolesAsync(_userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("identity DB down"));

        var context = ContextFor(AuthenticatedPrincipal());

        // The handler catches (logs) and returns without Succeed → requirement unmet → 403. It must NOT
        // rethrow (a 500 + info leak) and must NOT succeed (privilege escalation).
        await Should.NotThrowAsync(async () => await CreateHandler().HandleAsync(context));

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Fast_path_succeeds_without_a_db_call_when_principal_already_carries_admin_role()
    {
        var context = ContextFor(AuthenticatedPrincipal(new Claim(ClaimTypes.Role, Roles.Admin)));

        await CreateHandler().HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue();
        await _userAccountService.DidNotReceive().GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_resolve_for_unauthenticated_principal()
    {
        var context = ContextFor(new ClaimsPrincipal(new ClaimsIdentity())); // no auth type → not authenticated

        await CreateHandler().HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
        await _userAccountService.DidNotReceive().GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_resolve_when_user_id_claim_is_invalid()
    {
        var context = ContextFor(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], authenticationType: "TestAuth")));

        await CreateHandler().HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
        await _userAccountService.DidNotReceive().GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolves_exactly_once_across_repeated_evaluations_via_the_sentinel()
    {
        IReadOnlyList<string> noRoles = [];
        _userAccountService.GetRolesAsync(_userId, Arg.Any<CancellationToken>()).Returns(noRoles);
        var principal = AuthenticatedPrincipal();
        var handler = CreateHandler();

        // Two evaluations against the SAME principal (status-code re-execution): the sentinel added on the
        // first pass short-circuits the second, so the identity query runs exactly once.
        await handler.HandleAsync(ContextFor(principal));
        await handler.HandleAsync(ContextFor(principal));

        await _userAccountService.Received(1).GetRolesAsync(_userId, Arg.Any<CancellationToken>());
    }
}
