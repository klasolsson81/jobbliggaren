namespace JobbPilot.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4) — SSOT för fält-krypteringens wire-format-
/// versionssentinel. Sentinel-prefixet (<c>v1:</c>) disambiguerar ciphertext
/// från klartext-legacy och bär DEK-version för key-rotation (crypto-agility,
/// OWASP). Tidigare duplicerat i <c>KmsEnvelopeEncryptor</c> +
/// <c>FieldEncryptionBackfiller</c> (DRY-brott på knowledge-nivå, Hunt/Thomas
/// 1999; senior-cto-advisor 2026-05-19 — code-reviewer Finding 1 in-block).
///
/// <para>
/// <b>Runtime-auktoritet oförändrad:</b> den kanoniska "är detta krypterat"-
/// checken är <see cref="IFieldEncryptor.IsEncrypted"/> via regexen
/// <c>^v\d+:</c> (vidare än literalen — matchar alla versioner). Denna SSOT
/// bär (a) den literal <see cref="IFieldEncryptor.Encrypt"/> emitterar och
/// (b) den SQL-<c>LIKE</c>-översättbara syskonformen för legacy-detektering i
/// EF-/raw-SQL-queries (regexen är ej LINQ/SQL-översättbar; LIKE-formen är en
/// architect-sanktionerad approximation, 2026-05-19, EJ ersättare för
/// runtime-regexen).
/// </para>
/// </summary>
public static class FieldEncryptionSentinel
{
    /// <summary>
    /// Aktuellt versionssentinel-prefix. Emitteras av
    /// <see cref="IFieldEncryptor.Encrypt"/>. Bump:as vid DEK-version-rotation
    /// (då blir detta v2: etc.; runtime-regexen <c>^v\d+:</c> tål redan det).
    /// </summary>
    public const string VersionPrefix = "v1:";

    /// <summary>
    /// SQL-<c>LIKE</c>-mönster för legacy-detektering (rad ÄR krypterad om
    /// kolumnen <c>LIKE</c> detta). Legacy = <c>NOT LIKE</c>. SQL-översättbar
    /// syskonform till <see cref="VersionPrefix"/> (ej till runtime-regexen —
    /// architect-sanktionerad approximation 2026-05-19).
    /// </summary>
    public const string SqlLikePattern = "v1:%";
}
