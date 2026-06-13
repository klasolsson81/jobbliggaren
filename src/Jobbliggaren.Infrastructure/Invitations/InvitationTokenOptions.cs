namespace Jobbliggaren.Infrastructure.Invitations;

public sealed class InvitationTokenOptions
{
    public const string SectionName = "InvitationToken";

    /// <summary>
    /// HMAC-SHA256 key för token-hashing, base64-encoded. Genereras automatiskt
    /// vid uppstart om saknas (dev-flow). I prod måste värdet komma från
    /// AWS Secrets Manager via konfigurationsoverlay (BUILD.md §13.2).
    /// </summary>
    public string? HmacKeyBase64 { get; init; }

    /// <summary>
    /// Antal slumpmässiga bytes i plaintext-token. 32 bytes = 256 bits entropi,
    /// vilket är OWASP-rekommenderad nivå för out-of-band-tokens (ASVS V3.7).
    /// </summary>
    public int PlaintextByteLength { get; init; } = 32;
}
