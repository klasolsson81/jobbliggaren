using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts;

/// <summary>
/// The address term's shape, shared by the search and the counts. Validation runs before the admin check,
/// so these rules read nothing, and their messages never quote the term: the 400 body carries them.
/// </summary>
internal static class AccountAddressTermRules
{
    /// <summary>The longest address Identity stores.</summary>
    public const int MaxLength = 254;

    public static IRuleBuilderOptions<T, string?> AccountAddressTerm<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(MaxLength)
            .WithMessage("Sökningen får vara högst 254 tecken.")
            .Must(term => term is null || !term.Any(char.IsControl))
            .WithMessage("Sökningen innehåller tecken som inte kan användas.");
}
