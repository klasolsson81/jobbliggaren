using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// HmacIdentifierPseudonymizer (#842, ADR 0090 D5) — the primitive that lets the accountability
/// record (Art. 5(2)/30) name an erasure request without storing the identifier the request asked
/// us to erase.
/// </summary>
/// <remarks>
/// Writing the recruiter's address into the audit row for her own erasure request would make that
/// request the last place her address survives. These tests pin the two properties that make the
/// output a pseudonym rather than an obfuscation: it is <b>keyed</b> (not a plain digest), and it
/// is <b>stable under the normalisation a human operator's typing will vary by</b>.
/// </remarks>
public class HmacIdentifierPseudonymizerTests
{
    private const string Identifier = "anna.karlsson@example.com";

    private static readonly byte[] PepperA = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] PepperB = RandomNumberGenerator.GetBytes(32);

    private static HmacIdentifierPseudonymizer Pseudonymizer(byte[] pepper) =>
        new(Options.Create(new AuditPseudonymizationOptions
        {
            PepperBase64 = Convert.ToBase64String(pepper),
        }));

    // ================================================================================
    // It is a PSEUDONYM, not a fig leaf.
    // ================================================================================

    /// <summary>
    /// <b>The assertion that separates a pseudonym from an obfuscation.</b> An unkeyed digest of an
    /// email is dictionary-reversible in milliseconds: the address space is small and the input is
    /// guessable. Under Art. 4(5) the output is pseudonymous data only because it cannot be
    /// re-derived without the server-held pepper. Swap HMAC-SHA256(pepper) for a plain SHA-256 —
    /// the "md5" the old runbook proposed, in spirit — and this test goes red.
    /// </summary>
    [Fact]
    public void The_pseudonym_is_KEYED_and_is_not_a_plain_digest_of_the_identifier()
    {
        var unkeyed = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(Identifier)));

        var pseudonym = Pseudonymizer(PepperA).Pseudonymize(Identifier);

        pseudonym.ShouldNotBe(unkeyed,
            "an unkeyed digest of an email is reversible by dictionary attack — it looks protected "
            + "while protecting nothing, which is the defect class this whole issue is about.");
    }

    /// <summary>
    /// The pepper must actually key the output. If it did not, every deployment (and every reader
    /// of this PUBLIC repo) would derive the same hashes, and the pepper would be decoration.
    /// </summary>
    [Fact]
    public void A_different_pepper_yields_a_different_pseudonym_for_the_same_identifier()
    {
        var a = Pseudonymizer(PepperA).Pseudonymize(Identifier);
        var b = Pseudonymizer(PepperB).Pseudonymize(Identifier);

        a.ShouldNotBe(b, "the pepper is what makes the output non-reversible. It must be load-bearing.");
    }

    [Fact]
    public void The_pseudonym_never_contains_the_identifier_and_is_a_full_SHA256_hex_digest()
    {
        var pseudonym = Pseudonymizer(PepperA).Pseudonymize(Identifier);

        pseudonym.ShouldNotContain("anna", Case.Insensitive);
        pseudonym.ShouldNotContain("example.com", Case.Insensitive);
        pseudonym.Length.ShouldBe(64, "HMAC-SHA256 is 32 bytes = 64 lowercase hex chars.");
        pseudonym.ShouldBe(pseudonym.ToLowerInvariant());
    }

    // ================================================================================
    // ONE person must yield ONE pseudonym.
    // ================================================================================

    [Fact]
    public void The_same_identifier_yields_the_same_pseudonym_every_time()
    {
        var sut = Pseudonymizer(PepperA);

        sut.Pseudonymize(Identifier).ShouldBe(sut.Pseudonymize(Identifier));
    }

    /// <summary>
    /// An operator retyping an identifier will vary the case and the trailing space. Two pseudonyms
    /// for one person would defeat the point of recording one — the audit trail must be able to
    /// answer "did we already serve this person?" without holding her address.
    /// </summary>
    [Theory]
    [InlineData("anna.karlsson@example.com")]
    [InlineData("Anna.Karlsson@Example.com")]
    [InlineData("ANNA.KARLSSON@EXAMPLE.COM")]
    [InlineData("  anna.karlsson@example.com  ")]
    public void Case_and_surrounding_whitespace_normalise_to_ONE_pseudonym(string variant)
    {
        var sut = Pseudonymizer(PepperA);

        sut.Pseudonymize(variant).ShouldBe(sut.Pseudonymize(Identifier),
            "one person, one pseudonym — regardless of how the operator typed it.");
    }

    [Fact]
    public void Different_identifiers_yield_different_pseudonyms()
    {
        var sut = Pseudonymizer(PepperA);

        sut.Pseudonymize("anna.karlsson@example.com")
            .ShouldNotBe(sut.Pseudonymize("bengt.karlsson@example.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_identifier_is_refused_rather_than_silently_hashed(string identifier)
    {
        var sut = Pseudonymizer(PepperA);

        Should.Throw<ArgumentException>(() => sut.Pseudonymize(identifier));
    }

    // ================================================================================
    // GUARD THE GUARD.
    // ================================================================================

    /// <summary>
    /// <b>The pseudonymiser does NOT defend itself, and this test says so out loud.</b> HMAC-SHA256
    /// accepts a zero-length key and returns a digest without complaint, so an empty pepper produces
    /// a stable-looking hash that is effectively unkeyed — reversible, while looking protected.
    /// <para>
    /// The ONLY thing preventing that is <see cref="AuditPseudonymizationOptionsValidator"/> plus
    /// <c>ValidateOnStart()</c> in <c>DependencyInjection</c>. This test pins the exposure so that
    /// anyone who ever removes that startup guard learns what it was holding up, rather than
    /// discovering it in <c>audit_log</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void An_empty_pepper_still_produces_output_which_is_WHY_the_startup_guard_is_load_bearing()
    {
        var unguarded = Pseudonymizer([]);

        var pseudonym = unguarded.Pseudonymize(Identifier);
        var hmacWithEmptyKey = Convert.ToHexStringLower(
            HMACSHA256.HashData(Array.Empty<byte>(), Encoding.UTF8.GetBytes(Identifier)));

        pseudonym.ShouldBe(hmacWithEmptyKey,
            "an empty pepper is an UNKEYED hash — anyone can recompute it. Nothing in this class "
            + "prevents it; AuditPseudonymizationOptionsValidator + ValidateOnStart is the guard, "
            + "and it must never be removed.");

        // And the guard rejects exactly that configuration.
        new AuditPseudonymizationOptionsValidator()
            .Validate(null, new AuditPseudonymizationOptions { PepperBase64 = string.Empty })
            .Failed.ShouldBeTrue();
    }
}
