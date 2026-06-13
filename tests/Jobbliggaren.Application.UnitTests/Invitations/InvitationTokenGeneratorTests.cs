using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Invitations;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Invitations;

public class InvitationTokenGeneratorTests
{
    private static InvitationTokenGenerator CreateWithFixedKey(string base64Key = "YWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYQ==")
    {
        var opts = Options.Create(new InvitationTokenOptions { HmacKeyBase64 = base64Key });
        return new InvitationTokenGenerator(opts);
    }

    [Fact]
    public void Generate_ReturnsPlaintextAndHash()
    {
        var gen = CreateWithFixedKey();

        var token = gen.Generate();

        token.Plaintext.ShouldNotBeNullOrWhiteSpace();
        token.Hash.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Generate_PlaintextIsUrlSafeBase64()
    {
        var gen = CreateWithFixedKey();

        var token = gen.Generate();

        token.Plaintext.ShouldNotContain('+');
        token.Plaintext.ShouldNotContain('/');
        token.Plaintext.ShouldNotContain('=');
        token.Plaintext.Length.ShouldBe(43); // 32 bytes → 43 chars URL-safe base64
    }

    [Fact]
    public void Generate_HashIsHmacSha256Hex()
    {
        var gen = CreateWithFixedKey();

        var token = gen.Generate();

        token.Hash.Length.ShouldBe(64); // SHA-256 = 32 bytes = 64 hex chars
        foreach (var c in token.Hash)
        {
            (char.IsDigit(c) || (c >= 'a' && c <= 'f')).ShouldBeTrue(
                $"Hash innehåller icke-hex tecken: '{c}'");
        }
    }

    [Fact]
    public void Generate_ProducesDifferentTokensOnEachCall()
    {
        var gen = CreateWithFixedKey();

        var first = gen.Generate();
        var second = gen.Generate();

        first.Plaintext.ShouldNotBe(second.Plaintext);
        first.Hash.ShouldNotBe(second.Hash);
    }

    [Fact]
    public void Hash_IsDeterministicForSamePlaintext()
    {
        var gen = CreateWithFixedKey();

        var hash1 = gen.Hash("same-plaintext");
        var hash2 = gen.Hash("same-plaintext");

        hash1.ShouldBe(hash2);
    }

    [Fact]
    public void Hash_MatchesGeneratorPlaintextHash()
    {
        var gen = CreateWithFixedKey();
        var token = gen.Generate();

        var recomputed = gen.Hash(token.Plaintext);

        recomputed.ShouldBe(token.Hash);
    }

    [Fact]
    public void Hash_DiffersWithDifferentKey()
    {
        var gen1 = CreateWithFixedKey("YWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYQ==");
        var gen2 = CreateWithFixedKey("YmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYg==");

        var hash1 = gen1.Hash("same-plaintext");
        var hash2 = gen2.Hash("same-plaintext");

        hash1.ShouldNotBe(hash2);
    }

    [Fact]
    public void Generate_WithMissingKey_StillWorksWithEphemeralKey()
    {
        // Dev-fallback: ingen HmacKeyBase64 → random per-process-key genereras.
        var opts = Options.Create(new InvitationTokenOptions { HmacKeyBase64 = null });
        var gen = new InvitationTokenGenerator(opts);

        var token = gen.Generate();

        token.Plaintext.ShouldNotBeNullOrWhiteSpace();
        token.Hash.Length.ShouldBe(64);
    }
}
