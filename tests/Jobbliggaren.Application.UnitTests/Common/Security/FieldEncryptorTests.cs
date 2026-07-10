using System.Text;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// TD-13 FAS 3.5 batch C1 — svit A (architect §5).
/// Verifierar KmsEnvelopeEncryptor (ren symmetrisk AES-256-GCM via BCL AesGcm,
/// ingen AWS) mot IFieldEncryptor-kontraktet: sentinel-prefix v1: + base64,
/// nonce-unikhet, fail-closed auth-tag-verifiering (ADR 0049 Beslut 4,
/// CTO-domen 2026-05-18 — ingen klartext-fallback, ingen läcka i exception).
///
/// TDD-ordning (CLAUDE.md §2.4/§7): dessa tester är RÖDA tills C1-impl finns.
/// </summary>
public class FieldEncryptorTests
{
    // 32 byte = AES-256-nyckel. Determinerad i test för reproducerbarhet;
    // riktig DEK kommer från IDataKeyProvider i produktion.
    private static byte[] Dek()
    {
        var dek = new byte[32];
        for (var i = 0; i < dek.Length; i++)
        {
            dek[i] = (byte)(i * 7 + 3);
        }

        return dek;
    }

    // Konkret typ (CA1859) — testar KmsEnvelopeEncryptor mot IFieldEncryptor-kontraktet.
    private readonly KmsEnvelopeEncryptor _sut = new();

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsPlaintext()
    {
        // Svensk PII-sträng med åäö — får inte trasas i krypto-vägen (CLAUDE.md §10.2).
        const string plaintext =
            "Hej, jag heter Åsa Öberg och söker tjänsten som utvecklare på Ängsö AB.";
        var dek = Dek();

        var cipher = _sut.Encrypt(plaintext, dek);
        var roundTripped = _sut.Decrypt(cipher, dek);

        roundTripped.ShouldBe(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesSentinelPrefixedBase64()
    {
        const string plaintext = "personligt brev innehåll";

        var cipher = _sut.Encrypt(plaintext, Dek());

        cipher.ShouldStartWith("v1:");

        // Resten efter sentinel-prefixet ska vara giltig base64
        // (nonce(12) || ciphertext || tag(16)).
        var payload = cipher["v1:".Length..];
        Should.NotThrow(() => Convert.FromBase64String(payload));

        // Minst nonce(12) + tag(16) = 28 byte även för tom klartext.
        var decoded = Convert.FromBase64String(payload);
        decoded.Length.ShouldBeGreaterThanOrEqualTo(28);
    }

    [Fact]
    public void Decrypt_WithWrongDek_ThrowsAndNeverReturnsPlaintext()
    {
        const string plaintext = "hemligt CV-innehåll med personnummer";
        var cipher = _sut.Encrypt(plaintext, Dek());

        var wrongDek = new byte[32];
        wrongDek[0] = 0xFF; // skiljer sig från Dek()

        Exception? caught = null;
        string? leaked = null;
        try
        {
            leaked = _sut.Decrypt(cipher, wrongDek);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Fail-closed: GCM auth-tag-fel måste kasta, aldrig returnera klartext.
        caught.ShouldNotBeNull();
        leaked.ShouldBeNull();

        // §5.4 — klartext-PII får inte läcka i exception-message.
        (caught!.Message ?? string.Empty)
            .ShouldNotContain("personnummer");
        (caught.Message ?? string.Empty)
            .ShouldNotContain(plaintext);
    }

    [Fact]
    public void IsEncrypted_LegacyPlaintext_ReturnsFalse()
    {
        // Klartext-legacy (pre-migrering) har inget sentinel-prefix.
        _sut.IsEncrypted("vanlig klartext utan prefix").ShouldBeFalse();
        _sut.IsEncrypted(string.Empty).ShouldBeFalse();
        _sut.IsEncrypted("v1 utan kolon är inte sentinel").ShouldBeFalse();
    }

    [Fact]
    public void IsEncrypted_SentinelValue_ReturnsTrue()
    {
        var cipher = _sut.Encrypt("krypterat", Dek());

        _sut.IsEncrypted(cipher).ShouldBeTrue();
    }

    [Fact]
    public void Decrypt_FrozenPreRefactorCiphertext_StillRoundTrips()
    {
        // Fas 4b PR-9a — wire-format back-compat pin for the AesGcmEnvelope refactor
        // (ADR 0100): this literal was produced by the PRE-refactor KmsEnvelopeEncryptor
        // (2026-07-10) under Dek(). All Form A/B ciphertext at rest has this exact layout —
        // if this test ever fails, the wire format drifted and existing at-rest data bricks.
        const string frozen =
            "v1:SwWuHhLFqM+kt25NiVOjwpq3RfhWsxcVskEYxZtOtJ8fpE8BozefPadktoA6mrlkSGZgrNuXY45y"
            + "C9ADS5oonY/H6a5lPFgKx00iSfiS18M2pbsJss7n6yunBf2FBHxCNbIpZR1yTO+kmKiKuQWA";

        _sut.Decrypt(frozen, Dek()).ShouldBe(
            "Fryst pre-refaktor-pin: Åsa Öberg söker tjänsten som utvecklare på Ängsö AB.");
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentCiphertext()
    {
        const string plaintext = "identisk indata";
        var dek = Dek();

        var first = _sut.Encrypt(plaintext, dek);
        var second = _sut.Encrypt(plaintext, dek);

        // Slumpmässig nonce per Encrypt → ciphertext får ALDRIG vara
        // deterministisk (annars läcker likhet mellan PII-fält).
        first.ShouldNotBe(second);

        // Båda ska dock dekryptera tillbaka till samma klartext.
        _sut.Decrypt(first, dek).ShouldBe(plaintext);
        _sut.Decrypt(second, dek).ShouldBe(plaintext);
    }

    [Theory]
    [InlineData(16)] // AES-128 — för svag mot ADR 0049 Beslut 1-kontraktet
    [InlineData(24)] // AES-192
    [InlineData(31)] // off-by-one (trunkerad DEK)
    [InlineData(33)]
    public void Encrypt_WithNon256BitDek_Throws(int dekLength)
    {
        var badDek = new byte[dekLength];

        // Major 1 (security-auditor 2026-05-18): AES-256 enforce:as vid gränsen.
        Should.Throw<System.Security.Cryptography.CryptographicException>(
            () => _sut.Encrypt("data", badDek));
    }

    [Fact]
    public void Decrypt_WithNon256BitDek_Throws()
    {
        var cipher = _sut.Encrypt("data", Dek());
        var badDek = new byte[16];

        Should.Throw<System.Security.Cryptography.CryptographicException>(
            () => _sut.Decrypt(cipher, badDek));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        const string plaintext = "data som manipuleras";
        var dek = Dek();
        var cipher = _sut.Encrypt(plaintext, dek);

        // Flippa en byte i base64-payloaden → GCM auth-tag måste underkänna.
        var payload = Convert.FromBase64String(cipher["v1:".Length..]);
        payload[^1] ^= 0xFF; // sista byten ligger i tag(16)
        var tampered = "v1:" + Convert.ToBase64String(payload);

        Exception? caught = null;
        string? leaked = null;
        try
        {
            leaked = _sut.Decrypt(tampered, dek);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Fail-closed mot manipulering — integritetsbrott kastar, läcker ej.
        caught.ShouldNotBeNull();
        leaked.ShouldBeNull();
        (caught!.Message ?? string.Empty).ShouldNotContain(plaintext);
    }
}
