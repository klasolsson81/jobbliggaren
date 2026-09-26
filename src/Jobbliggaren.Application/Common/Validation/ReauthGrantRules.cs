using FluentValidation;

namespace Jobbliggaren.Application.Common.Validation;

/// <summary>
/// The one rule every <c>IReauthenticatingRequest</c> applies to its grant (#1739, ADR 0142 D5), so the three
/// implementers cannot drift: present, and no longer than a token this build mints. The bound is the same
/// one <c>CompleteLoginChallengeCommandValidator</c> puts on its grant token.
/// </summary>
public static class ReauthGrantRules
{
    /// <summary>The longest grant token a validator admits; a minted one is 22 characters.</summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// Non-empty and within <see cref="MaximumLength"/>. No format rule: the store answers a malformed grant
    /// exactly like an unknown one, and a validation message must never describe what a real grant looks like.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ReauthGrant<T>(this IRuleBuilder<T, string?> ruleBuilder)
        => ruleBuilder
            .NotEmpty()
            .MaximumLength(MaximumLength);
}
