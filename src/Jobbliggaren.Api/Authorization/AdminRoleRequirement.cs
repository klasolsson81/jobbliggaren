using Microsoft.AspNetCore.Authorization;

namespace Jobbliggaren.Api.Authorization;

/// <summary>
/// Authorization requirement satisfied only when the current user holds the Admin role (#746 PR-B).
/// Handled by <see cref="AdminRoleAuthorizationHandler"/>, which resolves roles ON DEMAND — so only
/// admin-policy-gated requests pay the identity query, not every authenticated request. This replaces
/// the eager <c>SessionRoleClaimsTransformation</c> that ran on every authenticated request even though
/// the Admin path is the sole consumer of <c>ClaimTypes.Role</c> (epic #737, findings d2 + d4).
/// </summary>
public sealed class AdminRoleRequirement : IAuthorizationRequirement;
