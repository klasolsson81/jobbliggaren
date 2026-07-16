using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Drops PII-bearing <b>keys</b> from the JobTech payload before it is persisted to
/// <c>job_ads.raw_payload</c>. Allowlist-based per Saltzer/Schroeder 1975 (default-deny)
/// — unknown keys are dropped. Pure static helper; no DI registration (no instance data).
/// </summary>
/// <remarks>
/// <para>
/// ADR 0032 §8-amendment 2026-05-12 + TD-73. The allowlist keys are derived from the
/// JobTech jobsearch/jobstream schema (web-verified 2026-05-12) and cover the fields
/// needed to debug/replay match logic.
/// </para>
///
/// <para>
/// <b>TRUTH-SYNC 2026-07-13 (#842) — read this before trusting the class name.</b>
/// This class's doc used to claim it "strips PII (recruiter name, email, phone,
/// signatory)". <b>That was false, and the false claim was load-bearing:</b> ADR 0032 §8
/// registered this class as one of two GDPR mitigations for inbound recruiter PII, and
/// ADR 0049 cited it to decline field encryption.
/// </para>
///
/// <para>
/// <b>It strips the FIELD, not the ADDRESS.</b> This is a <i>key-name</i> filter. It never
/// examines a value. Every free-text key is <b>deliberately retained</b> —
/// <c>description</c>, <c>description_text</c>, <c>text</c>, <c>company_information</c>,
/// <c>needs</c>, <c>requirements</c>, <c>salary_description</c> — and values are
/// <c>DeepClone()</c>d unexamined. A recruiter's address written into the ad body
/// ("Skicka CV till anna@acme.se") passes through untouched. Measured on the real corpus:
/// <b>27 077 of 93 469 ads (29 %) carry an email address in the ad body.</b>
/// </para>
///
/// <para>
/// <b>PII in free text was never a gap in this design. It IS this design.</b> The control
/// that will actually remove it is <c>RecruiterContactRedactor</c>, applied as a
/// <c>JobAd</c> aggregate invariant at ingest (ADR 0106 Tier A, PR2 — not shipped as of
/// this PR). This allowlist survives as defense-in-depth against structured contact keys,
/// which is all it ever did. It is not, and never was, a free-text control.
/// </para>
/// </remarks>
public static class JobTechPayloadSanitizer
{
    /// <summary>
    /// Top-level allowlist. Kontaktfält (employer.contact_email/name,
    /// application_details.email/url med PII) är medvetet uteslutna. Underliggande
    /// objekt-strukturer (workplace_address, occupation, employment_type) sanit-
    /// eras rekursivt genom samma allowlist eftersom hela trädet projiceras
    /// genom Top-level + Nested-listan.
    /// </summary>
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        // Identifierare + status (v1 + v2)
        "id", "external_id", "original_id", "removed", "removed_date",
        "source_type", "timestamp", "identified_language",

        // Annons-innehåll (description är object med text-keys, conditions också nested)
        "headline", "description", "description_html", "description_text",
        "text", "text_formatted",
        "company_information", "needs", "requirements",
        "publication_date", "last_publication_date", "experience_required",
        "conditions", "abilities", "number_of_vacancies", "access",
        "access_to_own_car", "driving_license_required", "driving_license",
        "logo_url",

        // Klassifikation
        "occupation", "occupation_group", "occupation_field", "occupation_address",
        "ssyk", "ssyk_level_1", "ssyk_level_2", "ssyk_level_3", "ssyk_level_4",
        "label", "legacy_ams_taxonomy_id", "concept_id",

        // Arbetsplats (geografi OK, inte rekryterar-info)
        "workplace_address", "country", "country_code", "country_concept_id",
        "region", "region_code", "region_concept_id",
        "municipality", "municipality_code", "municipality_concept_id",
        "street_address", "postcode", "city", "coordinates",

        // Anställningsform + ansökan (application_details är PII-tung — droppas
        // som top-level key; specifikt email/phone/information droppas defense-in-depth)
        "employment_type", "duration", "working_hours_type", "scope_of_work",
        "min", "max", "salary", "salary_type", "salary_description",
        "application_deadline",

        // Krav
        "must_have", "nice_to_have", "skills", "languages", "work_experiences",
        "education", "education_level", "education_field", "weight",

        // Företag (publika namn + org-nummer OK; phone_number, email, contact_email
        // är PII och INTE i listan → droppas av default-deny)
        "employer", "name", "organization_number", "workplace",

        // URLer till själva annonsen (publika)
        "webpage_url", "source_links", "url",
    };

    /// <summary>
    /// Sanerar payload. Returnerar JSON-sträng som innehåller endast allowlist-
    /// keys (rekursivt). Vid parse-fel returneras en tom JSON-object "{}" så
    /// nedströms-konsumenter alltid får giltigt jsonb-värde.
    /// </summary>
    // #842 Tier A — output encoding, NOT a filter change (the allowlist above is untouched,
    // re-bind R1(e)). ToJsonString()'s default encoder \uXXXX-escapes every non-ASCII character,
    // so the C# STRING the aggregate scrubs carried "Håkan" and "070<NBSP>123…" where the
    // detector's regex sees letters and separators — an NBSP-separated phone or an åäö email
    // survived the scrub in the payload copy while jsonb stored it DECODED and fully readable.
    // (Postgres decodes escapes on the text→jsonb cast, so the DB semantics are identical either
    // way; only the pre-storage string the invariant runs over changes.) Relaxed escaping makes
    // the string the scrub sees equal the text the database stores. "Unsafe" = do not embed in
    // HTML; this value goes into a jsonb column.
    private static readonly JsonSerializerOptions RelaxedEscaping = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SanitizeForStorage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return "{}";

        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is null)
                return "{}";

            var sanitized = Sanitize(node);
            return sanitized?.ToJsonString(RelaxedEscaping) ?? "{}";
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static JsonNode? Sanitize(JsonNode? node) => node switch
    {
        JsonObject obj => SanitizeObject(obj),
        JsonArray arr => SanitizeArray(arr),
        _ => node?.DeepClone(),
    };

    private static JsonObject SanitizeObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var kvp in obj)
        {
            if (!AllowedKeys.Contains(kvp.Key))
                continue;

            result[kvp.Key] = Sanitize(kvp.Value);
        }
        return result;
    }

    private static JsonArray SanitizeArray(JsonArray arr)
    {
        var result = new JsonArray();
        foreach (var item in arr)
            result.Add(Sanitize(item));
        return result;
    }
}
