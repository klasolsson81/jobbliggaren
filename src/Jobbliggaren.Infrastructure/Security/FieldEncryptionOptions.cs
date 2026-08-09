namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049) / ADR 0066 — konfiguration för lokal envelope-fält-
/// kryptering. Efter AWS-exiten (ADR 0050/0066) är
/// <see cref="LocalDataKeyProvider"/> den enda DEK-wrap-mekanismen; den
/// AWS-KMS-baserade providern och dess <c>CmkKeyId</c>/<c>AwsRegion</c>-options
/// är borttagna (#802). Master-nyckeln binds via <c>IOptions</c> + env-var
/// (samma precedens som <c>JobTechOptions</c>/<c>EmailOptions</c>). Fail-closed:
/// en tom/ogiltig master-nyckel validerar bort vid startup i ALLA miljöer
/// (<see cref="FieldEncryptionOptionsValidator"/>) — en trasig lokal nyckel får
/// aldrig tyst degradera krypteringen.
/// </summary>
public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    /// <summary>
    /// DEK-provider-val. Enda giltiga värdet efter AWS-exiten (#802/ADR 0066) är
    /// <c>"Local"</c> (lokal AES-256-GCM-wrappad envelope,
    /// <see cref="LocalDataKeyProvider"/>). Default <c>"Local"</c> — en utelämnad
    /// nyckel ger lokal envelope i alla miljöer. Ett explicit icke-Local-värde
    /// (t.ex. en kvarlämnad <c>"Kms"</c> i stale config) fail-fastar hårt i DI
    /// (<c>AddPersistence</c>) — aldrig en tyst fallback (paritet med
    /// <c>EmailOptions.Provider</c>; #802-footgunklassen). <see cref="IFieldEncryptor"/>
    /// (AES-256-GCM-primitiv, <see cref="AesGcmFieldEncryptor"/>) är AWS-fri och
    /// oberoende av provider-valet — bara DEK-wrap/unwrap är provider-specifikt.
    /// </summary>
    public string Provider { get; init; } = "Local";

    /// <summary>
    /// Base64 av en 32-byte (AES-256) lokal master-nyckel som wrappar
    /// per-användar-DEK:erna (<see cref="LocalDataKeyProvider"/>).
    /// PII-skyddande hemlighet: läses ENBART ur <c>appsettings.Local.json</c>
    /// (gitignored) lokalt / managed secret i drift, aldrig committad config.
    /// Loggas/exponeras aldrig (CLAUDE.md §5.4). Tom/fel-längd → hård startup-fail
    /// i ALLA miljöer (se <see cref="FieldEncryptionOptionsValidator"/>) — en
    /// trasig lokal master-nyckel får aldrig tyst degradera krypteringen.
    /// </summary>
    public string LocalMasterKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// #198 (M-3) — identity of the master key currently in force. NOT a secret: it is stamped
    /// into <c>user_data_keys.cmk_key_id</c> on every new wrapped DEK and is the idempotency
    /// marker the offline re-wrap operation reads ("local-v1" → "local-v2").
    ///
    /// <para>
    /// It must be configurable, and that is load-bearing rather than cosmetic: with the value
    /// hardcoded, every row created AFTER a rotation would be stamped with the retired key's
    /// identity while actually wrapped under the new key, so the next rotation's marker would
    /// select rows it cannot unwrap. Default <c>"local-v1"</c> preserves every pre-rotation row
    /// and every dev environment unchanged.
    /// </para>
    ///
    /// <para>
    /// Key identity lives HERE, never in the wrapped-DEK wire header. That header's version byte
    /// is format agility (layout), a separate axis — see <see cref="LocalDataKeyProvider"/>.
    /// </para>
    /// </summary>
    public string LocalMasterKeyId { get; init; } = "local-v1";
}
