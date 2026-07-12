using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence surface for <see cref="WatchFilterSpec"/> (bevaknings-reconcile RF-2, 2026-07-12),
/// mirroring <c>MatchPreferencesConverters</c>. <c>OwnsOne(...).ToJson()</c> does not map
/// <c>IReadOnlyList&lt;string&gt;</c> stably in Npgsql (issue #3129) → a property-level
/// <see cref="ValueConverter"/> against a NULLABLE <c>jsonb</c> column, with a tolerant
/// <see cref="JsonConverter{T}"/> that reads BOTH scalar and array form for the list key.
/// EF never invokes the converter for <c>null</c> — SQL <c>NULL</c> round-trips as CLR
/// <c>null</c> (= no filter, the canonical representation; existing rows are back-compatible
/// for free). Default-deny: anything not a string-or-string-array / boolean is rejected
/// (Saltzer/Schroeder 1975). Lives in Infrastructure — Domain stays serialization/EF-free
/// (CLAUDE.md §2.1). Property names are the jsonb-key contract (PascalCase).
/// </summary>
internal sealed class WatchFilterSpecJsonConverter : JsonConverter<WatchFilterSpec>
{
    public override WatchFilterSpec Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("WatchFilterSpec-jsonb måste vara ett objekt.");

        List<string> municipalities = [];
        var onlyMatched = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Oväntad token i WatchFilterSpec-jsonb.");

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "Municipalities":
                    municipalities = ReadStringOrStringArray(ref reader, "Municipalities");
                    break;
                case "OnlyMatched":
                    onlyMatched = reader.TokenType switch
                    {
                        JsonTokenType.True => true,
                        JsonTokenType.False => false,
                        _ => throw new JsonException(
                            "WatchFilterSpec.OnlyMatched måste vara en boolean."),
                    };
                    break;
                // Unknown key → skip the VALUE (an additive future axis alongside an
                // existing narrowing still reads). NOTE: a future-axis-ONLY spec (no
                // municipalities, OnlyMatched=false) still fails loud via Create's
                // empty-spec invariant below — correct default-deny, not full
                // forward-compat (code-review 2026-07-12 Minor 2).
                default:
                    reader.Skip();
                    break;
            }
        }

        // Re-validate via the Domain factory (single source of invariants + normalization).
        // Stored data was valid at write time; a stored spec that no longer satisfies the
        // invariants (e.g. an empty spec — which Create forbids) fails loud, never silently.
        var result = WatchFilterSpec.Create(municipalities, onlyMatched);
        if (result.IsFailure)
            throw new JsonException(
                $"Lagrad WatchFilterSpec-jsonb bröt domän-invariant: {result.Error.Code}.");
        return result.Value;
    }

    public override void Write(
        Utf8JsonWriter writer, WatchFilterSpec value, JsonSerializerOptions options)
    {
        // Always array form + PascalCase (= VO property names = jsonb-key contract).
        // OnlyMatched is always written explicitly for a deterministic canonical form.
        writer.WriteStartObject();

        writer.WritePropertyName("Municipalities");
        writer.WriteStartArray();
        foreach (var m in value.Municipalities)
            writer.WriteStringValue(m);
        writer.WriteEndArray();

        writer.WriteBoolean("OnlyMatched", value.OnlyMatched);

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
                            $"WatchFilterSpec.{field}-arrayen får bara innehålla strängar.");
                    list.Add(reader.GetString()!);
                }
                throw new JsonException($"Oavslutad WatchFilterSpec.{field}-array.");
            default:
                throw new JsonException(
                    $"WatchFilterSpec.{field} måste vara sträng, strängarray eller null.");
        }
    }
}

/// <summary>
/// EF Core value-conversion + value-comparison for <see cref="WatchFilterSpec"/> mapped
/// against a nullable <c>jsonb</c> column (mirrors <c>MatchPreferencesConversion</c>).
/// The comparer uses the VO's structural equality and is null-guarded (the column is
/// nullable — unlike <c>MatchPreferences</c>' non-null <c>'{}'</c>-default mapping).
/// </summary>
internal static class WatchFilterSpecConversion
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new WatchFilterSpecJsonConverter());
        return o;
    }

    public static readonly ValueConverter<WatchFilterSpec, string> Converter =
        new(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<WatchFilterSpec>(s, Options)!);

    // Immutable record-VO → snapshot = same instance (the list is a normalized array
    // created in Create, never mutated). hashCode is null-guarded too — the column is
    // NULLABLE (parity the ParsedResumeConfiguration nullable-jsonb comparers), unlike
    // the non-null MatchPreferences sibling.
    public static readonly ValueComparer<WatchFilterSpec> Comparer =
        new(
            (a, b) => a == null ? b == null : a.Equals(b),
            v => v == null ? 0 : v.GetHashCode(),
            v => v);
}
