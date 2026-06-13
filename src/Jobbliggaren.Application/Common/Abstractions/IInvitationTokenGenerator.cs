namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Generator för opaque invitation-tokens. Plaintext skickas bara till
/// inbjuden via email; hash lagras i Invitation-aggregaten och används vid
/// redemption-lookup. Per ADR 0005 amendment 2026-05-12: HMAC-SHA256 i
/// SesInvitationTokenGenerator (Infrastructure). Pre-auth-mekanism — INTE
/// JWT (deprecated per ADR 0017/0018) eller session-tokens.
/// </summary>
public interface IInvitationTokenGenerator
{
    /// <summary>
    /// Genererar nytt slumpmässigt token + dess hash. Plaintext är 32 bytes
    /// URL-safe base64 (43 chars), hash är HMAC-SHA256 hex (64 chars).
    /// </summary>
    InvitationToken Generate();

    /// <summary>
    /// Hashar inkommande plaintext för redemption-lookup. Samma hash-algoritm
    /// som <see cref="Generate"/> så att jämförelse mot lagrad hash matchar.
    /// </summary>
    string Hash(string plaintext);
}

public sealed record InvitationToken(string Plaintext, string Hash);
