using System.Security.Cryptography;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5 / ADR 0100) — verifies the Form C binary cipher
/// (<see cref="BinaryFieldEncryptor"/>, <c>[version(1)] || nonce(12) || ciphertext || tag(16)</c>
/// over the shared <see cref="AesGcmEnvelope"/> core) against the
/// <c>IBinaryFieldEncryptor</c> contract: round-trip, version guard (crypto-agility —
/// an unknown layout fails loud, not as an auth-tag mismatch), nonce uniqueness, and
/// fail-closed tamper/wrong-DEK behaviour with no plaintext in the exception (§5).
/// Mirrors <see cref="FieldEncryptorTests"/> (the Form A/B string sibling).
/// </summary>
public class BinaryFieldEncryptorTests
{
    // Same fixed 32-byte DEK recipe as FieldEncryptorTests — deterministic in test;
    // the real DEK comes from IDataKeyProvider in production.
    private static byte[] Dek()
    {
        var dek = new byte[32];
        for (var i = 0; i < dek.Length; i++)
        {
            dek[i] = (byte)(i * 7 + 3);
        }

        return dek;
    }

    private static byte[] SamplePdfishBytes()
    {
        // Non-trivial, non-text bytes (magic prefix + varied payload) — the cipher is
        // byte-in/byte-out and must not care what the bytes mean.
        var bytes = new byte[256];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46; // "%PDF"
        for (var i = 4; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i * 13 + 7);
        }

        return bytes;
    }

    private readonly BinaryFieldEncryptor _sut = new();

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsBytes()
    {
        var plaintext = SamplePdfishBytes();

        var sealedContent = _sut.Encrypt(plaintext, Dek());
        var opened = _sut.Decrypt(sealedContent, Dek());

        opened.ShouldBe(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesVersionPrefixedEnvelope_WithExpectedLength()
    {
        var plaintext = SamplePdfishBytes();

        var sealedContent = _sut.Encrypt(plaintext, Dek());

        // [version(1)] || nonce(12) || ciphertext(len) || tag(16) — raw bytes, no base64.
        sealedContent[0].ShouldBe((byte)0x01);
        sealedContent.Length.ShouldBe(1 + 12 + plaintext.Length + 16);
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentCiphertext()
    {
        var plaintext = SamplePdfishBytes();
        var dek = Dek();

        var first = _sut.Encrypt(plaintext, dek);
        var second = _sut.Encrypt(plaintext, dek);

        // Random nonce per Encrypt — equal originals must never yield equal ciphertext.
        first.ShouldNotBe(second);
        _sut.Decrypt(first, dek).ShouldBe(plaintext);
        _sut.Decrypt(second, dek).ShouldBe(plaintext);
    }

    [Fact]
    public void Decrypt_UnknownVersionByte_FailsLoud_NotAsAuthTagMismatch()
    {
        var sealedContent = _sut.Encrypt(SamplePdfishBytes(), Dek());
        sealedContent[0] = 0x02; // a future/unknown layout version

        var ex = Should.Throw<CryptographicException>(
            () => _sut.Decrypt(sealedContent, Dek()));

        // Crypto-agility: the version guard must name the version problem — a v2 envelope
        // read by a v1 decryptor is a CLEAR failure, not a generic auth-tag mismatch.
        ex.Message.ShouldContain("Form C-version");
    }

    [Fact]
    public void Decrypt_EmptyPayload_Throws()
    {
        Should.Throw<CryptographicException>(
            () => _sut.Decrypt(ReadOnlySpan<byte>.Empty, Dek()));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsAndNeverReturnsPlaintext()
    {
        var plaintext = SamplePdfishBytes();
        var dek = Dek();
        var sealedContent = _sut.Encrypt(plaintext, dek);
        sealedContent[^1] ^= 0xFF; // flip a tag byte — GCM must reject

        byte[]? leaked = null;
        Exception? caught = null;
        try
        {
            leaked = _sut.Decrypt(sealedContent, dek);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.ShouldNotBeNull();
        caught.ShouldBeOfType<CryptographicException>();
        leaked.ShouldBeNull();
    }

    [Fact]
    public void Decrypt_WithWrongDek_ThrowsAndNeverReturnsPlaintext()
    {
        var sealedContent = _sut.Encrypt(SamplePdfishBytes(), Dek());
        var wrongDek = new byte[32];
        wrongDek[0] = 0xFF;

        byte[]? leaked = null;
        Exception? caught = null;
        try
        {
            leaked = _sut.Decrypt(sealedContent, wrongDek);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.ShouldNotBeNull();
        caught.ShouldBeOfType<CryptographicException>();
        leaked.ShouldBeNull();
    }

    [Theory]
    [InlineData(16)] // AES-128 — weaker than the ADR 0049 Beslut 1 contract
    [InlineData(24)] // AES-192
    [InlineData(31)] // truncated DEK (off-by-one)
    [InlineData(33)]
    public void Encrypt_WithNon256BitDek_Throws(int dekLength)
    {
        var badDek = new byte[dekLength];

        Should.Throw<CryptographicException>(
            () => _sut.Encrypt(SamplePdfishBytes(), badDek));
    }

    [Fact]
    public void Decrypt_WithNon256BitDek_Throws()
    {
        var sealedContent = _sut.Encrypt(SamplePdfishBytes(), Dek());
        var badDek = new byte[16];

        Should.Throw<CryptographicException>(
            () => _sut.Decrypt(sealedContent, badDek));
    }

    [Fact]
    public void Encrypt_EmptyPlaintext_RoundTripsToEmpty()
    {
        // AES-GCM handles empty plaintext (envelope = version + nonce + tag only). The
        // AGGREGATE rejects empty sealed-content ≤ 29 bytes never happens through capture
        // (CaptureOriginal requires byteSize > 0), but the cipher itself must be total.
        var dek = Dek();

        var sealedContent = _sut.Encrypt(ReadOnlySpan<byte>.Empty, dek);

        sealedContent.Length.ShouldBe(1 + 12 + 16);
        _sut.Decrypt(sealedContent, dek).ShouldBeEmpty();
    }
}
