namespace JobbPilot.Application.Common.Authorization;

/// <summary>
/// Kanoniska roll-namn för JobbPilot. Refereras av AuthorizationPolicy-konfig
/// (Api), <see cref="Abstractions.IAdminRequest"/>-behavior (Application), och
/// Identity-seeder (Infrastructure). En källa till sanning — bryts magic-string-
/// anti-patternet (CLAUDE.md §5.1).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
}
