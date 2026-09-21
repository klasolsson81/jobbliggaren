using System.Globalization;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Whether an address may become an account's STORED address. A predicate over character classes, not a charset:
/// it says nothing about which letters an address may hold and everything about characters that break an identity
/// derived from the address (<see cref="SubjectFingerprint"/> trims, Identity's lookup does not) or that cannot be
/// stored or shown faithfully (security-auditor MA-1, 2026-09-21).
/// </summary>
internal static class StorableAddress
{
    internal static bool IsStorable(string address) => !address.Any(IsForbidden);

    // Per char, which is exhaustive only because a surrogate is itself refused: no astral character reaches
    // the category test as a pair.
    private static bool IsForbidden(char c) =>
        char.IsControl(c)
        || char.IsWhiteSpace(c)
        || char.IsSurrogate(c)
        || char.GetUnicodeCategory(c) == UnicodeCategory.Format;
}
