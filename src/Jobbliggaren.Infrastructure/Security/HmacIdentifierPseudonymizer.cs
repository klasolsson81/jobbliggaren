using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Security;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256(server pepper) — the house pseudonymisation primitive bound by ADR 0090 D5 and,
/// until #842, never built. Used by the Art. 17 erasure command so the accountability record can
/// name the request without storing the identifier the request asked us to erase.
/// </summary>
/// <remarks>
/// Singleton: the pepper is read once at construction and the instance is stateless thereafter.
/// The pepper is never logged and never surfaced in an exception message (CLAUDE.md §5).
/// <para>
/// <b>Not a replacement for erasure.</b> The pseudonym proves we handled a request; it does not
/// let us find her ads again later, and it must never be used to (that would be the suppression
/// ledger this contract explicitly refused — the one design that leaves us holding <i>more</i> of
/// her data after her erasure request than before it).
/// </para>
/// </remarks>
internal sealed class HmacIdentifierPseudonymizer : IIdentifierPseudonymizer
{
    private readonly byte[] _pepper;

    public HmacIdentifierPseudonymizer(IOptions<AuditPseudonymizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Validated at startup by AuditPseudonymizationOptionsValidator (ValidateOnStart), so a
        // malformed or missing pepper never reaches this constructor.
        _pepper = Convert.FromBase64String(options.Value.PepperBase64);
    }

    public string Pseudonymize(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        // Normalize first: "Anna@Acme.se ", "anna@acme.se" and "ANNA@ACME.SE" are one person, and
        // two pseudonyms for one person would defeat the point of recording one.
        var normalized = identifier.Trim().ToLowerInvariant();
        var hash = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash);
    }
}
