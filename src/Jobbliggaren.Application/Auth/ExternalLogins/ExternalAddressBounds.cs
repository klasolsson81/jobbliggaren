using System.Diagnostics.CodeAnalysis;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// The one shape an address from a provider must have, whichever type carries it (ADR 0142 D8): one local part and
/// one domain, within the bound every stored address meets, so no such address overruns a grant's padded payload.
/// </summary>
internal static class ExternalAddressBounds
{
    internal static bool IsWithinBounds([NotNullWhen(true)] string? address)
    {
        if (string.IsNullOrEmpty(address) || address.Length > EmailAddressRules.MaximumLength)
            return false;

        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at != address.Length - 1 && address.IndexOf('@', at + 1) < 0;
    }
}
