using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Security;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256(watch pepper) tokeniser for a personnummer-shaped org.nr (#544, ADR 0090 D5). Shares
/// the keyed-HMAC-SHA256 <i>mechanism</i> with <see cref="HmacIdentifierPseudonymizer"/> but is a
/// distinct port with its own pepper and its own (verbatim) normalisation — see
/// <see cref="IProtectedIdentityTokenizer"/> for why they must not be one.
/// </summary>
/// <remarks>
/// Singleton: the pepper is read once at construction and the instance is stateless thereafter. The
/// pepper is never logged and never surfaced in an exception message (CLAUDE.md §5).
/// </remarks>
internal sealed class HmacProtectedIdentityTokenizer : IProtectedIdentityTokenizer
{
    private readonly byte[] _pepper;

    public HmacProtectedIdentityTokenizer(IOptions<CompanyWatchPseudonymizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Validated at startup by CompanyWatchPseudonymizationOptionsValidator (ValidateOnStart), so
        // a malformed or missing pepper never reaches this constructor.
        _pepper = Convert.FromBase64String(options.Value.PepperBase64);
    }

    public string Tokenize(string organizationNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationNumber);

        // VERBATIM — an org.nr is already canonical (exactly 10 ASCII digits, enforced by
        // OrganizationNumber.Create). NO Trim/ToLower: that is the audit-email port's contract, and
        // "a rule with two normalisers is two rules". Sharing it would couple this live at-rest
        // watch key to the audit-log key.
        var hash = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(organizationNumber));
        return Convert.ToHexStringLower(hash);
    }
}
