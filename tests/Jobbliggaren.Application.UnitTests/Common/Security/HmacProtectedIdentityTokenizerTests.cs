using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// HmacProtectedIdentityTokenizer (#544, ADR 0090 D5) — the at-rest token key for a
/// personnummer-shaped (enskild-firma) <c>company_watches.organization_number</c>. A sole proprietor's
/// org.nr <i>equals</i> their personnummer, so it must never sit in plaintext — while the follow key
/// must stay deterministically equality-matchable by <c>CompanyWatchScanJob</c> (a DEK breaks SQL
/// <c>IN</c>; a keyed HMAC does not).
/// </summary>
/// <remarks>
/// Mirrors <see cref="HmacIdentifierPseudonymizerTests"/> but pins the property that makes this a
/// DISTINCT port: it tokenises the org.nr <b>VERBATIM</b> (no Trim/ToLower). The audit pseudonymiser
/// normalises <c>Trim().ToLowerInvariant()</c> because one operator can type an email five ways;
/// sharing that normalisation here would couple this live at-rest key to the audit-log key, and a
/// future audit-normalisation change would silently orphan every stored watch token — the product's
/// cardinal sin (a watch that matches nothing forever). "A rule with two normalisers is two rules."
/// </remarks>
public class HmacProtectedIdentityTokenizerTests
{
    // A personnummer-shaped (third digit 0) org.nr — the enskild-firma case this port exists for.
    private const string OrgNr = "9001011234";

    private static readonly byte[] PepperA = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] PepperB = RandomNumberGenerator.GetBytes(32);

    private static HmacProtectedIdentityTokenizer Tokenizer(byte[] pepper) =>
        new(Options.Create(new CompanyWatchPseudonymizationOptions
        {
            PepperBase64 = Convert.ToBase64String(pepper),
        }));

    // ================================================================================
    // Deterministic — CompanyWatchScanJob equality-matches on it.
    // ================================================================================

    [Fact]
    public void The_same_org_nr_yields_the_same_token_every_time()
    {
        var sut = Tokenizer(PepperA);

        // Determinism is load-bearing: the scan re-derives HMAC(ad.org.nr) and equality-matches it
        // against the stored token. If the token were non-deterministic the follow would match nothing.
        sut.Tokenize(OrgNr).ShouldBe(sut.Tokenize(OrgNr));
    }

    [Fact]
    public void Two_callers_with_the_same_pepper_produce_an_identical_token()
    {
        // Two separately-constructed instances under the same pepper agree — the executor writes the
        // token, a later scan re-derives it in a different process; both must land on the same value.
        Tokenizer(PepperA).Tokenize(OrgNr).ShouldBe(Tokenizer(PepperA).Tokenize(OrgNr));
    }

    [Fact]
    public void Different_org_nrs_yield_different_tokens()
    {
        var sut = Tokenizer(PepperA);

        sut.Tokenize("9001011234").ShouldNotBe(sut.Tokenize("8512319876"));
    }

    // ================================================================================
    // KEYED under a SEPARATE pepper — not a plain digest, not the audit pepper.
    // ================================================================================

    /// <summary>
    /// The pepper must actually key the output. If it did not, anyone who clones this PUBLIC repo
    /// (ADR 0072) could re-derive the token — and the small personnummer space makes an unkeyed digest
    /// trivially brute-force-reversible, which would <i>look</i> protected while protecting nothing.
    /// </summary>
    [Fact]
    public void A_different_pepper_yields_a_different_token_for_the_same_org_nr()
    {
        Tokenizer(PepperA).Tokenize(OrgNr).ShouldNotBe(Tokenizer(PepperB).Tokenize(OrgNr),
            "the watch pepper is what makes the token non-reversible. It must be load-bearing.");
    }

    [Fact]
    public void The_token_is_KEYED_and_is_not_a_plain_digest_of_the_org_nr()
    {
        var unkeyed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(OrgNr)));

        Tokenizer(PepperA).Tokenize(OrgNr).ShouldNotBe(unkeyed,
            "swap HMAC-SHA256(pepper) for a plain SHA-256 and this goes red — an unkeyed digest of a "
            + "10-digit personnummer is reversible by brute force in milliseconds.");
    }

    [Fact]
    public void The_token_is_a_full_lowercase_SHA256_hex_digest()
    {
        var token = Tokenizer(PepperA).Tokenize(OrgNr);

        token.Length.ShouldBe(64, "HMAC-SHA256 is 32 bytes = 64 lowercase hex chars (Convert.ToHexStringLower).");
        token.ShouldBe(token.ToLowerInvariant());
        token.ShouldNotContain(OrgNr, Case.Sensitive, "the token never contains the plaintext org.nr.");
    }

    // ================================================================================
    // VERBATIM — the whole reason this is a separate port from the audit pseudonymiser.
    // ================================================================================

    /// <summary>
    /// <b>The assertion that separates this port from <see cref="HmacIdentifierPseudonymizer"/>.</b>
    /// The audit pseudonymiser normalises surrounding whitespace (and case) to ONE pseudonym; this
    /// tokeniser must NOT. An org.nr is already canonical (10 ASCII digits, enforced by
    /// <c>OrganizationNumber.Create</c>) so no normalisation is needed — and adding one would re-key
    /// every existing token and orphan every stored watch. If a future edit adds <c>Trim()</c> here,
    /// this goes red.
    /// </summary>
    [Fact]
    public void The_tokeniser_does_NOT_trim_it_is_verbatim_unlike_the_audit_pseudonymiser()
    {
        var sut = Tokenizer(PepperA);

        sut.Tokenize(" 9001011234 ").ShouldNotBe(sut.Tokenize("9001011234"),
            "VERBATIM: the tokeniser must not Trim. The audit port normalises whitespace to one "
            + "pseudonym by contract; this port must not, or a normalisation change would silently "
            + "orphan every stored watch token (a rule with two normalisers is two rules).");
    }

    /// <summary>
    /// <b>The exact-output golden pin.</b> Under a FIXED pepper (bytes 0..31) and a FIXED org.nr, the
    /// token is this exact lowercase-hex string (cross-checked with python3 hmac + openssl). Any change
    /// to the normalisation, the encoding (UTF-8), the algorithm, or the hex casing fails loudly here
    /// rather than silently orphaning production tokens. NOT circular: the literal was computed by an
    /// independent tool, so a regression in the C# path cannot move the goalpost with it.
    /// </summary>
    [Fact]
    public void A_fixed_pepper_and_org_nr_produce_this_exact_lowercase_hex_token()
    {
        // Pepper = the 32 bytes 0,1,...,31. org.nr = "9001011234".
        var sut = Tokenizer([.. Enumerable.Range(0, 32).Select(i => (byte)i)]);

        sut.Tokenize("9001011234").ShouldBe(
            "91febfd18014665c2a686bb4e29c4400a806e46badb333758482bafa873f2e95",
            "golden HMAC-SHA256(key=0x00..0x1f, msg=UTF8(\"9001011234\")) — cross-checked with "
            + "python3 hmac and openssl. A future normalisation/encoding change must fail HERE, not "
            + "silently in company_watches.");
    }

    // ================================================================================
    // Refuses the un-tokenisable input rather than hashing it.
    // ================================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_whitespace_org_nr_is_refused_rather_than_silently_hashed(string value)
    {
        Should.Throw<ArgumentException>(() => Tokenizer(PepperA).Tokenize(value));
    }
}
