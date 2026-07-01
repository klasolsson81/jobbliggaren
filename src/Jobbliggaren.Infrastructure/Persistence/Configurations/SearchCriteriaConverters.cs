using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistens-yta för <see cref="SearchCriteria"/> (CTO Yta A3, ADR 0042
/// Beslut B 2026-05-16). `OwnsOne(...).ToJson()` mappar inte
/// <c>IReadOnlyList&lt;string&gt;</c> stabilt i Npgsql (issue #3129) →
/// property-level <see cref="ValueConverter"/> mot en <c>jsonb</c>-kolumn
/// istället, med en tolerant <see cref="JsonConverter{T}"/> som läser BÅDE
/// skalär-form och array-form per list-nyckel. Saknad nyckel → tom lista
/// (ADR 0042 invariant 4 — gammal rad utan nya dimensioner passerar Create).
/// Default-deny: allt som inte är sträng-eller-strängarray avvisas
/// (Saltzer/Schroeder 1975). Bor i Infrastructure — Domain förblir
/// serialiserings-/EF-fritt (CLAUDE.md §2.1).
///
/// <para><b>Fas C2 (ADR 0067, CTO-dom (f) 2026-06-09) — legacy-"Ssyk"-kedjan:</b>
/// nycklarna är OccupationGroup/Municipality/Region/Q/SortBy. Legacy-nyckeln
/// <c>"Ssyk"</c> (occupation-name) FAIL-LOUD:ar (aldrig tyst Skip — tyst
/// droppning kunde amputera en bevaknings yrke-dimension eller lämna raden i
/// tom-invariant-brott). Garantikedjan som gör fallet ≈ omöjligt: (1) C2-
/// reverse-lookup-migrationen transformerar/strippar nyckeln på ALLA rader
/// (predikat = nyckel-existens, inkl. <c>"Ssyk":[]</c>) och abortar vid
/// omappbart id; (2) skrivvägen stängdes i samma batch (Write emitterar aldrig
/// nyckeln; commands bär inte fältet); (3) saved_searches hade 0 rader vid
/// migrationen. Deploy-ordning: migrationen appliceras FÖRE ny binär startas.</para>
/// </summary>
internal sealed class SearchCriteriaJsonConverter : JsonConverter<SearchCriteria>
{
    public override SearchCriteria Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("SearchCriteria-jsonb måste vara ett objekt.");

        List<string> occupationGroup = [];
        List<string> municipality = [];
        List<string> region = [];
        List<string> employmentType = [];
        List<string> worktimeExtent = [];
        List<string> employer = [];
        string? q = null;
        JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Oväntad token i SearchCriteria-jsonb.");

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "OccupationGroup":
                    occupationGroup = ReadStringOrStringArray(ref reader, "OccupationGroup");
                    break;
                case "Municipality":
                    municipality = ReadStringOrStringArray(ref reader, "Municipality");
                    break;
                case "Region":
                    region = ReadStringOrStringArray(ref reader, "Region");
                    break;
                // ADR 0067 Beslut 6 (Fas B2): Klass 2-nycklar. Saknad nyckel
                // (gammal rad sparad före B2) → tom lista via switch-default →
                // Create passerar tom-invarianten additivt (samma bakåtkompat-
                // mönster som C1/C2-dimensionerna). Property-namn = jsonb-kontrakt.
                case "EmploymentType":
                    employmentType = ReadStringOrStringArray(ref reader, "EmploymentType");
                    break;
                case "WorktimeExtent":
                    worktimeExtent = ReadStringOrStringArray(ref reader, "WorktimeExtent");
                    break;
                // #311 PR-2b C1 (ADR 0087 D6): Employer-nyckeln (org.nr). Saknad nyckel (rad
                // sparad före C1) → tom lista via switch-default → Create passerar tom-invarianten
                // additivt (samma bakåtkompat-mönster som Klass 2 — bevisat av back-compat-testet).
                // Property-namn = jsonb-kontrakt (PascalCase).
                case "Employer":
                    employer = ReadStringOrStringArray(ref reader, "Employer");
                    break;
                case "Q":
                    q = reader.TokenType switch
                    {
                        JsonTokenType.Null => null,
                        JsonTokenType.String => reader.GetString(),
                        _ => throw new JsonException("SearchCriteria.Q måste vara sträng eller null."),
                    };
                    break;
                case "SortBy":
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var sb))
                        throw new JsonException("SearchCriteria.SortBy måste vara ett heltal.");
                    sortBy = (JobAdSortBy)sb;
                    break;
                case "Ssyk":
                    // CTO-dom (f) 2026-06-09: fail-loud, ALDRIG tyst Skip().
                    // Se garantikedjan i klass-XML-doc:en.
                    throw new JsonException(
                        "Lagrad SearchCriteria-jsonb bär legacy-nyckeln \"Ssyk\" (occupation-name) "
                        + "som skulle ha transformerats till \"OccupationGroup\" av C2-reverse-lookup-"
                        + "migrationen. Raden är omigrerad — applicera migrationen i stället för att "
                        + "tyst droppa sökningens yrke-dimension (fail-safe default, ADR 0067).");
                default:
                    reader.Skip();
                    break;
            }
        }

        // Re-validera via Domain-faktorn (single source of invariants +
        // sorterad+distinct-normalisering). Lagrad data var giltig vid
        // skrivning; saknad nyckel → tom lista passerar. Fail-safe
        // default-deny vid korrupt data.
        var result = SearchCriteria.Create(
            occupationGroup: occupationGroup,
            municipality: municipality,
            region: region,
            employmentType: employmentType,
            worktimeExtent: worktimeExtent,
            employer: employer,
            q: q,
            sortBy: sortBy);
        if (result.IsFailure)
            throw new JsonException(
                $"Lagrad SearchCriteria-jsonb bröt domän-invariant: {result.Error.Code}.");
        return result.Value;
    }

    public override void Write(
        Utf8JsonWriter writer, SearchCriteria value, JsonSerializerOptions options)
    {
        // Skriver alltid array-form + PascalCase (matchar VO-property-namnen =
        // jsonb-nyckel-kontraktet). Nyckelordning = kanonisk dimensionsordning
        // (architect F1). SortBy som heltal (oförändrad form). "Ssyk" skrivs
        // ALDRIG — skrivvägen kan per konstruktion inte trigga fail-loud-casen.
        writer.WriteStartObject();

        writer.WritePropertyName("OccupationGroup");
        writer.WriteStartArray();
        foreach (var g in value.OccupationGroup)
            writer.WriteStringValue(g);
        writer.WriteEndArray();

        writer.WritePropertyName("Municipality");
        writer.WriteStartArray();
        foreach (var m in value.Municipality)
            writer.WriteStringValue(m);
        writer.WriteEndArray();

        writer.WritePropertyName("Region");
        writer.WriteStartArray();
        foreach (var r in value.Region)
            writer.WriteStringValue(r);
        writer.WriteEndArray();

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2-dimensioner (alltid array-form).
        writer.WritePropertyName("EmploymentType");
        writer.WriteStartArray();
        foreach (var e in value.EmploymentType)
            writer.WriteStringValue(e);
        writer.WriteEndArray();

        writer.WritePropertyName("WorktimeExtent");
        writer.WriteStartArray();
        foreach (var w in value.WorktimeExtent)
            writer.WriteStringValue(w);
        writer.WriteEndArray();

        // #311 PR-2b C1 (ADR 0087 D6) — Employer (org.nr) i kanonisk ordning efter WorktimeExtent
        // (matchar VO:ts Equals/GetHashCode-ordning + FilterHash). Alltid array-form.
        writer.WritePropertyName("Employer");
        writer.WriteStartArray();
        foreach (var emp in value.Employer)
            writer.WriteStringValue(emp);
        writer.WriteEndArray();

        if (value.Q is null)
            writer.WriteNull("Q");
        else
            writer.WriteString("Q", value.Q);

        writer.WriteNumber("SortBy", (int)value.SortBy);

        writer.WriteEndObject();
    }

    // Tolerant + default-deny: sträng → [s]; array-av-strängar → lista;
    // null → []. Nummer/objekt/bool/array-med-icke-sträng/null-element →
    // hård avvisning (ingen tyst koercering).
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
                            $"SearchCriteria.{field}-arrayen får bara innehålla strängar.");
                    list.Add(reader.GetString()!);
                }
                throw new JsonException($"Oavslutad SearchCriteria.{field}-array.");
            default:
                throw new JsonException(
                    $"SearchCriteria.{field} måste vara sträng, strängarray eller null.");
        }
    }
}

/// <summary>
/// EF Core value-conversion + value-comparison för <see cref="SearchCriteria"/>
/// mappad mot en <c>jsonb</c>-kolumn (CTO Yta A3). Comparern använder VO:ts
/// strukturella record-equality (samma som SavedSearch jsonb-dedupe vilar på).
/// </summary>
internal static class SearchCriteriaConversion
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new SearchCriteriaJsonConverter());
        return o;
    }

    public static readonly ValueConverter<SearchCriteria, string> Converter =
        new(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<SearchCriteria>(s, Options)!);

    // SearchCriteria är en immutabel record-VO → snapshot = samma instans
    // (listorna är normaliserade arrays skapade i Create, muteras aldrig).
    public static readonly ValueComparer<SearchCriteria> Comparer =
        new(
            (a, b) => a == null ? b == null : a.Equals(b),
            v => v.GetHashCode(),
            v => v);
}
