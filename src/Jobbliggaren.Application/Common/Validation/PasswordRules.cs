using FluentValidation;

namespace Jobbliggaren.Application.Common.Validation;

/// <summary>
/// Single source of truth for password-strength validation. Every command that accepts a
/// <em>new</em> password (registration, change-password) applies <see cref="Password{T}"/> so the
/// FluentValidation floor cannot drift below what ASP.NET Identity enforces.
///
/// <para>
/// The length mirrors <c>IdentityOptions.Password.RequiredLength = 12</c> (Infrastructure
/// <c>DependencyInjection.AddIdentityAndSessions</c>). The Application layer cannot reference
/// Infrastructure, so the value is duplicated here deliberately — if the Identity option changes,
/// change <see cref="MinimumLength"/> in lockstep. Historically the register validator used a
/// stray <c>MinimumLength(8)</c> below Identity's 12, so an 8–11 char password passed validation
/// only to fail at <c>UserManager.CreateAsync</c> with a worse (Identity-internal) error path.
/// </para>
/// </summary>
public static class PasswordRules
{
    /// <summary>Minimum password length. Mirrors Identity's <c>RequiredLength = 12</c>.</summary>
    public const int MinimumLength = 12;

    /// <summary>
    /// Applies the shared password-strength policy to a field carrying a <em>new</em> password:
    /// non-empty and at least <see cref="MinimumLength"/> characters.
    ///
    /// <para>
    /// Do NOT use this on a re-authentication / current-password field — those stay
    /// <c>NotEmpty()</c>-only, so no length rule can fail on (and thereby echo) a supplied
    /// credential through the <c>ValidationException</c> path.
    /// </para>
    /// </summary>
    public static IRuleBuilderOptions<T, string?> Password<T>(this IRuleBuilder<T, string?> ruleBuilder)
        => ruleBuilder
            .NotEmpty()
            .MinimumLength(MinimumLength);
}
