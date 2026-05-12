using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobbPilot.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Strippar PII (rekryterar-namn, email, telefon, firmatecknare) från JobTech-
/// payload innan persistering i <c>job_ads.raw_payload</c>. Allowlist-baserad
/// per Saltzer/Schroeder 1975 (default-deny) — okända keys droppas. Pure
/// static helper; ingen DI-registrering eftersom inga instance-data.
/// </summary>
/// <remarks>
/// ADR 0032 §8-amendment 2026-05-12 + TD-73. Allowlist-keys är härledda från
/// JobTech jobsearch-/jobstream-schemat (web-verifierat 2026-05-12) och täcker
/// fält som behövs för debug/replay av match-logik utan att exponera PII.
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
        // Identifierare + status
        "id", "external_id", "removed", "removed_date",

        // Annons-innehåll (description är object med "text"-key, conditions också nested)
        "headline", "description", "description_html", "description_text", "text",
        "publication_date", "last_publication_date", "experience_required",
        "conditions", "abilities",

        // Klassifikation
        "occupation", "occupation_group", "occupation_field", "occupation_address",
        "ssyk", "ssyk_level_1", "ssyk_level_2", "ssyk_level_3", "ssyk_level_4",
        "label", "legacy_ams_taxonomy_id", "concept_id",

        // Arbetsplats (geografi OK, inte rekryterar-info)
        "workplace_address", "country", "country_code", "country_concept_id",
        "region", "region_code", "region_concept_id",
        "municipality", "municipality_code", "municipality_concept_id",
        "street_address", "postcode", "city",

        // Anställningsform + ansökan
        "employment_type", "duration", "working_hours_type", "scope_of_work",
        "min", "max", "salary", "salary_type", "salary_description",
        "application_deadline",

        // Krav
        "must_have", "nice_to_have", "skills", "languages", "work_experiences",
        "education", "education_level", "education_field",

        // Företag (publika namn OK, kontakt-PII INTE OK)
        "employer", "name", "organization_number", "workplace",

        // URL till själva annonsen (publik)
        "source_links", "url",
    };

    /// <summary>
    /// Sanerar payload. Returnerar JSON-sträng som innehåller endast allowlist-
    /// keys (rekursivt). Vid parse-fel returneras en tom JSON-object "{}" så
    /// nedströms-konsumenter alltid får giltigt jsonb-värde.
    /// </summary>
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
            return sanitized?.ToJsonString() ?? "{}";
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
