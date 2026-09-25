using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// An address the provider is authoritative for and asserts as verified (ADR 0142 D8): "verified" is carried by
/// the TYPE, so no caller can read a string and forget a flag. Only a provider adapter decides that an address
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
        if (string.IsNullOrEmpty(address) || address.Length > EmailAddressRules.MaximumLength)
            return null;

        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == address.Length - 1 || address.IndexOf('@', at + 1) >= 0)
            return null;

        return new VerifiedEmail(address);
    }
}
