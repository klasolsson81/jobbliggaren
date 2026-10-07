using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// An address a provider's adapter admits as the user's own (ADR 0142 D8): Google's where Google is authoritative for
/// the mailbox (Amendment (14)), GitHub's and LinkedIn's verified primary address by Klas's decisions (Amendments (18)
/// and (20)). "Verified" is
/// carried by the TYPE, so no caller can read a string and forget a flag. Only a provider adapter decides that an address
/// qualifies; this factory adds the bounds every stored address meets, so an address longer than a validator
/// admits never reaches a grant's padded payload. <see cref="ToString"/> prints no part of it.
/// </summary>
public sealed record VerifiedEmail
{
    private VerifiedEmail(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => "VerifiedEmail(redacted)";

    /// <summary>Null for anything that is not one address of a local part and a domain within the length bound.</summary>
    public static VerifiedEmail? TryCreate(string? address)
    {
        if (!EmailAddressRules.IsUsableInboxAddress(address))
            return null;

        return new VerifiedEmail(address!);
    }
}
