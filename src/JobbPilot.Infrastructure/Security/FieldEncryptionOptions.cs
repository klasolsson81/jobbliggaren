namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049) — konfiguration för KMS-envelope-fält-kryptering.
/// CMK-ARN/-id och region bundna via <c>IOptions</c> + env-var (samma
/// precedens som <c>JobTechOptions</c>/<c>EmailOptions</c> och
/// Migrate-mönstret, ADR 0049 Kontext). Fail-closed: tom
/// <see cref="CmkKeyId"/> validerar bort vid startup i Production/Staging
/// (hård fail); i Development/Test loggas warning och boot tillåts —
/// runtime-guarden i <c>KmsDataKeyProvider</c> kvarstår fail-closed i alla
/// miljöer (ADR 0049 Beslut 4 mekanik-not 2026-05-18,
/// <see cref="FieldEncryptionOptionsValidator"/>).
/// </summary>
public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    /// <summary>
    /// DEK-provider-val: <c>"Kms"</c> (AWS KMS envelope, <see cref="KmsDataKeyProvider"/>)
    /// eller <c>"Local"</c> (lokal AES-256-GCM-wrappad envelope,
    /// <see cref="LocalDataKeyProvider"/>). Default <c>"Kms"</c> — bevarar
    /// befintligt beteende i alla miljöer som inte explicit väljer Local
    /// (integ-test-fixturer, prod). Dev sätter <c>"Local"</c> i
    /// <c>appsettings.Development.json</c> (ADR 0066 — AWS avvecklat lokalt).
    /// Okänt värde → hård fail i DI (paritet med <c>EmailOptions.Provider</c>).
    /// <see cref="IFieldEncryptor"/> (AES-256-GCM-primitiv) är AWS-fri och
    /// delas av båda providers — bara DEK-wrap/unwrap skiljer sig.
    /// </summary>
    public string Provider { get; init; } = "Kms";

    /// <summary>
    /// KMS CMK-ARN eller key-id som wrappar per-användar-DEK:erna
    /// (<c>GenerateDataKey</c>/<c>Decrypt</c>). Obligatorisk i miljöer som
    /// kör mot riktig KMS — hård startup-validering i Production/Staging,
    /// warning i Development/Test (se <see cref="FieldEncryptionOptionsValidator"/>).
    /// Endast relevant när <see cref="Provider"/> = <c>"Kms"</c>.
    /// </summary>
    public string CmkKeyId { get; init; } = string.Empty;

    /// <summary>
    /// AWS-region för KMS-klienten. Default eu-north-1 (Stockholm) —
    /// GDPR EU-routing, samma som RDS/Secrets Manager. Endast relevant när
    /// <see cref="Provider"/> = <c>"Kms"</c>.
    /// </summary>
    public string AwsRegion { get; init; } = "eu-north-1";

    /// <summary>
    /// Base64 av en 32-byte (AES-256) lokal master-nyckel som wrappar
    /// per-användar-DEK:erna när <see cref="Provider"/> = <c>"Local"</c>.
    /// PII-skyddande hemlighet: läses ENBART ur <c>appsettings.Local.json</c>
    /// (gitignored), aldrig committad config. Loggas/exponeras aldrig
    /// (CLAUDE.md §5.4). Tom/fel-längd → hård startup-fail i ALLA miljöer
    /// (se <see cref="FieldEncryptionOptionsValidator"/>) — en trasig lokal
    /// master-nyckel får aldrig tyst degradera krypteringen.
    /// </summary>
    public string LocalMasterKeyBase64 { get; init; } = string.Empty;
}
