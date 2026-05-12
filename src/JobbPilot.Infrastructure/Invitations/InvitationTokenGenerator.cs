using System.Security.Cryptography;
using System.Text;
using JobbPilot.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Invitations;

/// <summary>
/// HMAC-SHA256-baserad token-generator per ADR 0005 amendment 2026-05-12.
/// Plaintext är 32 bytes random URL-safe base64 (256 bits entropi); hash är
/// HMAC-SHA256 hex (64 chars). HMAC mot server-secret ger defense-in-depth
/// ifall DB läcker — angripare kan inte återanvända TokenHash mot framtida
/// token-guesses utan key.
/// </summary>
public sealed class InvitationTokenGenerator(IOptions<InvitationTokenOptions> options)
    : IInvitationTokenGenerator
{
    private readonly byte[] _hmacKey = ResolveHmacKey(options.Value);
    private readonly int _plaintextByteLength = options.Value.PlaintextByteLength;

    public InvitationToken Generate()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(_plaintextByteLength);
        var plaintext = Base64UrlEncode(randomBytes);
        var hash = ComputeHash(plaintext);
        return new InvitationToken(plaintext, hash);
    }

    public string Hash(string plaintext) => ComputeHash(plaintext);

    private string ComputeHash(string plaintext)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var hashBytes = hmac.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] ResolveHmacKey(InvitationTokenOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.HmacKeyBase64))
            return Convert.FromBase64String(opts.HmacKeyBase64);

        // Dev-fallback: generera per-process-key. ALDRIG i prod — då bryts hash
        // mellan deploys och utfärdade invitations blir o-redeemable. Prod måste
        // sätta HmacKeyBase64 via Secrets Manager.
        return RandomNumberGenerator.GetBytes(32);
    }
}
