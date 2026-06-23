using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence surface for <see cref="MatchPreferences"/> (F4-12, ADR 0076),
/// mirroring <c>SearchCriteriaConverters</c>. <c>OwnsOne(...).ToJson()</c> does not
/// map <c>IReadOnlyList&lt;string&gt;</c> stably in Npgsql (issue #3129) → a
/// property-level <see cref="ValueConverter"/> against a <c>jsonb</c> column, with a
/// tolerant <see cref="JsonConverter{T}"/> that reads BOTH scalar and array form per
/// list key. Missing key → empty list (an old job_seekers row, or the column's
/// <c>'{}'</c> default, deserializes to <see cref="MatchPreferences.Empty"/>).
/// Default-deny: anything not a string-or-string-array is rejected (Saltzer/Schroeder
/// 1975). Lives in Infrastructure — Domain stays serialization/EF-free (CLAUDE.md §2.1).
/// Property names are the jsonb-key contract (PascalCase).
/// </summary>
internal sealed class MatchPreferencesJsonConverter : JsonConverter<MatchPreferences>
{
    public override MatchPreferences Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("MatchPreferences-jsonb måste vara ett objekt.");

        List<string> occupationGroups = [];
        List<string> regions = [];
        List<string> employmentTypes = [];
        List<string> municipalities = [];
        List<string> skills = [];
        int? experienceYears = null;
        List<OccupationExperience> occupationExperience = [];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Oväntad token i MatchPreferences-jsonb.");

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "PreferredOccupationGroups":
                    occupationGroups = ReadStringOrStringArray(ref reader, "PreferredOccupationGroups");
                    break;
                case "PreferredRegions":
                    regions = ReadStringOrStringArray(ref reader, "PreferredRegions");
                    break;
                case "PreferredEmploymentTypes":
                    employmentTypes = ReadStringOrStringArray(ref reader, "PreferredEmploymentTypes");
                    break;
                case "PreferredMunicipalities":
                    municipalities = ReadStringOrStringArray(ref reader, "PreferredMunicipalities");
                    break;
                // STEG 3 (ADR 0079) — skills are a 5th string-or-array list (same
                // tolerant reader); experience is the one NUMERIC key (its own branch
                // — ReadStringOrStringArray is default-deny on numbers).
                case "PreferredSkills":
                    skills = ReadStringOrStringArray(ref reader, "PreferredSkills");
                    break;
                case "ExperienceYears":
                    experienceYears = ReadNullableInt(ref reader, "ExperienceYears");
                    break;
                // ADR 0079-amendment — the per-occupation experience overlay: an array of
                // {ConceptId, Years} objects (default-deny on any other element shape). Domain
                // re-validation (subset/range/format/distinct) happens in Create on the read path.
                case "PreferredOccupationExperience":
                    occupationExperience = ReadOccupationExperienceArray(
                        ref reader, "PreferredOccupationExperience");
                    break;
                // Missing/unknown key → empty list / null via the defaults above (an old
                // row written before Spår 3 / STEG 3, or the '{}' default, has no
                // "PreferredMunicipalities" / "PreferredSkills" / "ExperienceYears" key →
                // empty / null, never a crash). Forward-compatible.
                default:
                    reader.Skip();
                    break;
            }
        }

        // Re-validate via the Domain factory (single source of invariants +
        // sorted+distinct normalization). Stored data was valid at write time;
        // missing key → empty list / null passes (empty is allowed). Fail-safe on corruption.
        var result = MatchPreferences.Create(
            preferredOccupationGroups: occupationGroups,
            preferredRegions: regions,
            preferredEmploymentTypes: employmentTypes,
            preferredMunicipalities: municipalities,
            preferredSkills: skills,
            experienceYears: experienceYears,
            preferredOccupationExperience: occupationExperience);
        if (result.IsFailure)
            throw new JsonException(
                $"Lagrad MatchPreferences-jsonb bröt domän-invariant: {result.Error.Code}.");
        return result.Value;
    }

    public override void Write(
        Utf8JsonWriter writer, MatchPreferences value, JsonSerializerOptions options)
    {
        // Always array form + PascalCase (= VO property names = jsonb-key contract).
        // Canonical write order (each appended last as an additive jsonb extension):
        // OccupationGroups, Regions, EmploymentTypes, Municipalities, Skills, the
        // ExperienceYears scalar, then the PreferredOccupationExperience overlay.
        writer.WriteStartObject();

        writer.WritePropertyName("PreferredOccupationGroups");
        writer.WriteStartArray();
        foreach (var g in value.PreferredOccupationGroups)
            writer.WriteStringValue(g);
        writer.WriteEndArray();

        writer.WritePropertyName("PreferredRegions");
        writer.WriteStartArray();
        foreach (var r in value.PreferredRegions)
            writer.WriteStringValue(r);
        writer.WriteEndArray();

        writer.WritePropertyName("PreferredEmploymentTypes");
        writer.WriteStartArray();
        foreach (var e in value.PreferredEmploymentTypes)
            writer.WriteStringValue(e);
        writer.WriteEndArray();

        writer.WritePropertyName("PreferredMunicipalities");
        writer.WriteStartArray();
        foreach (var m in value.PreferredMunicipalities)
            writer.WriteStringValue(m);
        writer.WriteEndArray();

        // STEG 3 (ADR 0079) — skills array (appended last among the lists) + the
        // experience scalar. ExperienceYears is always written explicitly (number when
        // stated, JSON null when "not stated") for a deterministic canonical form.
        writer.WritePropertyName("PreferredSkills");
        writer.WriteStartArray();
        foreach (var s in value.PreferredSkills)
            writer.WriteStringValue(s);
        writer.WriteEndArray();

        if (value.ExperienceYears is { } years)
            writer.WriteNumber("ExperienceYears", years);
        else
            writer.WriteNull("ExperienceYears");

        // ADR 0079-amendment — the per-occupation experience overlay, appended last (additive
        // jsonb extension). An array of {ConceptId, Years} objects; Years is written explicitly
        // (number when stated, JSON null when "not stated") for a deterministic canonical form.
        writer.WritePropertyName("PreferredOccupationExperience");
        writer.WriteStartArray();
        foreach (var oe in value.PreferredOccupationExperience)
        {
            writer.WriteStartObject();
            writer.WriteString("ConceptId", oe.ConceptId);
            if (oe.Years is { } oeYears)
                writer.WriteNumber("Years", oeYears);
            else
                writer.WriteNull("Years");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    // Tolerant + default-deny: string → [s]; string array → list; null → [].
    // Number/object/bool/array-with-non-string → hard reject (no silent coercion).
    private static List<string> ReadStringOrStringArray(
        ref Utf8JsonReader reader, string field)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return [];
            case JsonTokenType.String:
                return [reader.GetString()!];
            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        return list;
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException(
                            $"MatchPreferences.{field}-arrayen får bara innehålla strängar.");
                    list.Add(reader.GetString()!);
                }
                throw new JsonException($"Oavslutad MatchPreferences.{field}-array.");
            default:
                throw new JsonException(
                    $"MatchPreferences.{field} måste vara sträng, strängarray eller null.");
        }
    }

    // ADR 0079-amendment — the per-occupation overlay reader. Default-deny: an array of
    // objects, each with a required string ConceptId and an optional integer/null Years
    // (anything else is rejected, no silent coercion — parity with the other readers). The
    // domain invariants (subset/range/format/distinct) are re-checked in Create on read.
    private static List<OccupationExperience> ReadOccupationExperienceArray(
        ref Utf8JsonReader reader, string field)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"MatchPreferences.{field} måste vara en array eller null.");

        var list = new List<OccupationExperience>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list;
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException(
                    $"MatchPreferences.{field}-arrayen får bara innehålla objekt.");

            string? conceptId = null;
            int? years = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException($"Oväntad token i MatchPreferences.{field}-objekt.");

                var prop = reader.GetString();
                reader.Read();
                switch (prop)
                {
                    case "ConceptId":
                        if (reader.TokenType != JsonTokenType.String)
                            throw new JsonException(
                                $"MatchPreferences.{field}.ConceptId måste vara en sträng.");
                        conceptId = reader.GetString();
                        break;
                    case "Years":
                        years = ReadNullableInt(ref reader, $"{field}.Years");
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(conceptId))
                throw new JsonException(
                    $"MatchPreferences.{field}-objekt saknar en giltig ConceptId.");
            list.Add(new OccupationExperience(conceptId, years));
        }

        throw new JsonException($"Oavslutad MatchPreferences.{field}-array.");
    }

    // STEG 3 (ADR 0079) — the one NUMERIC key. Default-deny: only an integer Number or
    // null is accepted; a fractional number, string, object, bool or array is rejected
    // (no silent coercion — parity with ReadStringOrStringArray). Domain re-validation
    // (0..MaxExperienceYears) happens in MatchPreferences.Create on the read path.
    private static int? ReadNullableInt(ref Utf8JsonReader reader, string field)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var value))
                    return value;
                throw new JsonException($"MatchPreferences.{field} måste vara ett heltal.");
            default:
                throw new JsonException(
                    $"MatchPreferences.{field} måste vara ett heltal eller null.");
        }
    }
}

/// <summary>
/// EF Core value-conversion + value-comparison for <see cref="MatchPreferences"/>
/// mapped against a <c>jsonb</c> column (mirrors <c>SearchCriteriaConversion</c>).
/// The comparer uses the VO's structural record-equality.
/// </summary>
internal static class MatchPreferencesConversion
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new MatchPreferencesJsonConverter());
        return o;
    }

    public static readonly ValueConverter<MatchPreferences, string> Converter =
        new(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<MatchPreferences>(s, Options)!);

    // Immutable record-VO → snapshot = same instance (lists are normalized arrays
    // created in Create, never mutated).
    public static readonly ValueComparer<MatchPreferences> Comparer =
        new(
            (a, b) => a == null ? b == null : a.Equals(b),
            v => v.GetHashCode(),
            v => v);
}
