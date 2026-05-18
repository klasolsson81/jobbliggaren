namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049) — konfiguration för KMS-envelope-fält-kryptering.
/// CMK-ARN/-id och region bundna via <c>IOptions</c> + env-var (samma
/// precedens som <c>JobTechOptions</c>/<c>EmailOptions</c> och
/// Migrate-mönstret, ADR 0049 Kontext). Fail-closed: tom
/// <see cref="CmkKeyId"/> ska validera bort vid startup (.ValidateOnStart()).
/// </summary>
public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    /// <summary>
    /// KMS CMK-ARN eller key-id som wrappar per-användar-DEK:erna
    /// (<c>GenerateDataKey</c>/<c>Decrypt</c>). Obligatorisk i miljöer som
    /// kör mot riktig KMS — får inte vara tom (valideras vid startup).
    /// </summary>
    public string CmkKeyId { get; init; } = string.Empty;

    /// <summary>
    /// AWS-region för KMS-klienten. Default eu-north-1 (Stockholm) —
    /// GDPR EU-routing, samma som RDS/Secrets Manager.
    /// </summary>
    public string AwsRegion { get; init; } = "eu-north-1";
}
