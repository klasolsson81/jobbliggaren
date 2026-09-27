using System.Text;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// Whether a provider's verified address is the account's own (ADR 0142 D8, security-auditor M-1(a); rule signed by
/// security-auditor 2026-09-25, 6a form round S3). Case-insensitive only when BOTH strings are pure ASCII, otherwise
/// ordinal. Identity already holds two ASCII spellings that differ only in case as one account (the unique
/// normalised user name), so this cannot join two accounts the system keeps apart.
/// The external arm alone: a code and a link keep the ordinal rule, where the spelling is equal by construction.
/// An asserted address (#1745) is compared under this rule too, to a found link's account and to the address a code
/// proved; there it rests on the code-bound link, not on the provider's mailbox (security-auditor, signed 2026-09-26,
/// ADR 0142 Amendment (16)).
/// </summary>
internal static class ExternalAddressMatch
{
    public static bool IsSameAddress(string accountEmail, string providerEmail) =>
        Ascii.IsValid(accountEmail) && Ascii.IsValid(providerEmail)
            ? string.Equals(accountEmail, providerEmail, StringComparison.OrdinalIgnoreCase)
            : string.Equals(accountEmail, providerEmail, StringComparison.Ordinal);
}
