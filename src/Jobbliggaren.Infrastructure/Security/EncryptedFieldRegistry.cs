using System.Text.Json;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049, CTO-triage lucka 3) — EN sanning (SPOT) för vilka
/// (entitet, property/shadow) som krypteras. Statisk allowlist i Infrastructure;
/// Domain bär INGA krypto-attribut (ADR 0009, Clean Arch). Delas av
/// <see cref="FieldEncryptionSaveChangesInterceptor"/> (write) +
/// <see cref="FieldDecryptionMaterializationInterceptor"/> (read).
///
/// <para><b>Form A</b> (C3 — de tre TEXT-kolumnerna): domän-string-property
/// krypteras in-place (samma property bär klartext-eller-ciphertext).</para>
///
/// <para><b>Form B</b> (C4 #1c — ADR 0049 Mekanik-not 6): domän-VO
/// (<c>ResumeVersion.Content</c>) är EF-<c>Ignore</c>:ad; JSON-serialiseras →
/// krypteras → skrivs till krypterad text-shadow <c>content_enc</c>. Read:
/// shadow → decrypt → JSON → VO. Backfill-fönstret (Beslut 5 steg 2): legacy
/// klartext-JSON i <c>content</c>-jsonb (rå-shadow <c>ContentLegacyJson</c>)
/// läses om <c>content_enc</c> saknar sentinel — ingen decrypt, ingen DEK.</para>
/// </summary>
internal static class EncryptedFieldRegistry
{
    /// <summary>
    /// SPOT (dotnet-architect 2026-05-19): delad System.Text.Json-policy för
    /// <c>ResumeContent</c>↔JSON. Konsumeras av <see cref="JsonMap"/>-
    /// delegaterna, write-/read-interceptorerna OCH
    /// <c>ResumeVersionConfiguration</c> (legacy-fallback). EN instans —
    /// <see cref="JsonSerializerOptions"/> är trådsäker efter första bruk.
    /// Flyttad hit från <c>ResumeVersionConfiguration</c> (krypto-relaterad
    /// serialiseringspolicy hör hemma i Security, paritet med registry).
    /// </summary>
    internal static readonly JsonSerializerOptions ContentJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // Form A — C3 TEXT-kolumner + F4-8 ParsedResume.RawText (greenfield, no legacy).
    private static readonly Dictionary<Type, string[]> Map = new()
    {
        [typeof(DomainApplication)] = ["CoverLetter"],
        [typeof(ApplicationNote)] = ["Content"],
        [typeof(FollowUp)] = ["Note"],
        // F4-8 (ADR 0074 Invariant 3): the raw normalized CV text, retained for F4-9
        // span citation, is a plain string → Form A (encrypted in-place into raw_text).
        [typeof(ParsedResume)] = ["RawText"],
    };

    // Form B — C4 #1c: domän-VO JSON-serialiserad till krypterad text-shadow,
    // med legacy jsonb-rå-shadow som backfill-fallback (Beslut 5 steg 2).
    private static readonly Dictionary<Type, JsonSerializedVoField[]> JsonMap = new()
    {
        [typeof(ResumeVersion)] =
        [
            new JsonSerializedVoField(
                DomainProperty: nameof(ResumeVersion.Content),
                ShadowProperty: "ContentEnc",
                LegacyShadowProperty: "ContentLegacyJson",
                ToJson: vo => JsonSerializer.Serialize(vo, ContentJsonOptions),
                FromJson: json =>
                    JsonSerializer.Deserialize<ResumeContent>(json, ContentJsonOptions)!),
        ],
        // F4-8 (ADR 0074 Invariant 3): the structured parsed content is an EF-Ignore'd
        // VO → JSON → encrypted shadow (parsed_content_enc). Greenfield table — no
        // backfill window, so NO legacy plaintext shadow (LegacyShadowProperty = null).
        [typeof(ParsedResume)] =
        [
            new JsonSerializedVoField(
                DomainProperty: nameof(ParsedResume.Content),
                ShadowProperty: "ParsedContentEnc",
                LegacyShadowProperty: null,
                ToJson: vo => JsonSerializer.Serialize(vo, ContentJsonOptions),
                FromJson: json =>
                    JsonSerializer.Deserialize<ParsedResumeContent>(json, ContentJsonOptions)!),
        ],
    };

    public static bool TryGetEncryptedProperties(Type entityType, out string[] properties) =>
        Map.TryGetValue(entityType, out properties!);

    public static bool TryGetJsonSerializedFields(
        Type entityType, out JsonSerializedVoField[] fields) =>
        JsonMap.TryGetValue(entityType, out fields!);
}

/// <summary>
/// TD-13 C4 #1c (ADR 0049 Mekanik-not 6) — Form B-mappning: en EF-<c>Ignore</c>:ad
/// domän-VO som JSON-serialiseras runt fält-krypteringen och skrivs till en
/// krypterad text-shadow, med en legacy klartext-jsonb-rå-shadow som
/// backfill-fönster-fallback. Delegaterna kapslar den delade
/// <see cref="EncryptedFieldRegistry.ContentJsonOptions"/> (SPOT).
/// </summary>
internal sealed record JsonSerializedVoField(
    string DomainProperty,
    string ShadowProperty,
    string? LegacyShadowProperty,
    Func<object, string> ToJson,
    Func<string, object> FromJson);
