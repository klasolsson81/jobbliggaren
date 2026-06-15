using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence surface for <see cref="ExtractedTerms"/> (F4-4, dotnet-architect
/// Variant A / 3a). Mirrors <see cref="SearchCriteriaJsonConverter"/>: a
/// property-level <see cref="ValueConverter"/> to a <c>jsonb</c> column, with a
/// tolerant default-deny <see cref="JsonConverter{T}"/>. The jsonb is an <b>array
/// of term objects</b> (PascalCase keys = the jsonb contract) so the STORED
/// generated <c>extracted_lexemes text[]</c> column can project
/// <c>jsonb_path_query_array(extracted_terms, '$[*].Lexeme')</c> for the GIN
/// overlap the matching engine (F4-6) uses. Re-validates through
/// <see cref="ExtractedTerms.From"/> on read (single normalization point;
/// fail-loud on corrupt jsonb). Lives in Infrastructure — Domain stays
/// serialization-/EF-free (CLAUDE.md §2.1).
/// </summary>
internal sealed class ExtractedTermsJsonConverter : JsonConverter<ExtractedTerms>
{
    public override ExtractedTerms Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("ExtractedTerms-jsonb måste vara en array.");

        var terms = new List<ExtractedTerm>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("ExtractedTerms-arrayen får bara innehålla objekt.");
            terms.Add(ReadTerm(ref reader));
        }

        // Single normalization point (parity SearchCriteria.Create): re-dedupe,
        // re-sort, re-cap + invariant-validate. Corrupt stored data fail-louds.
        return ExtractedTerms.From(terms);
    }

    private static ExtractedTerm ReadTerm(ref Utf8JsonReader reader)
    {
        string? lexeme = null, display = null, matchedOn = null, conceptId = null;
        ExtractedTermKind? kind = null;
        ExtractedTermSource? source = null;
        double weight = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Oväntad token i ExtractedTerm-objektet.");

            var prop = reader.GetString();
            reader.Read();
            switch (prop)
            {
                case "Lexeme": lexeme = reader.GetString(); break;
                case "Display": display = reader.GetString(); break;
                case "MatchedOn": matchedOn = reader.GetString(); break;
                case "ConceptId":
                    conceptId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "Kind": kind = ParseEnum<ExtractedTermKind>(ref reader, "Kind"); break;
                case "Source": source = ParseEnum<ExtractedTermSource>(ref reader, "Source"); break;
                case "Weight":
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out weight))
                        throw new JsonException("ExtractedTerm.Weight måste vara ett tal.");
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (lexeme is null || display is null || matchedOn is null || kind is null || source is null)
            throw new JsonException("ExtractedTerm-objektet saknar obligatoriska fält.");

        return new ExtractedTerm(lexeme, display, kind.Value, source.Value, matchedOn, conceptId, weight);
    }

    private static TEnum ParseEnum<TEnum>(ref Utf8JsonReader reader, string field)
        where TEnum : struct, Enum
    {
        var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (raw is null || !Enum.TryParse<TEnum>(raw, ignoreCase: false, out var value)
            || !Enum.IsDefined(value))
        {
            throw new JsonException($"ExtractedTerm.{field} har ett okänt värde: '{raw}'.");
        }
        return value;
    }

    public override void Write(
        Utf8JsonWriter writer, ExtractedTerms value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var term in value.Terms)
        {
            writer.WriteStartObject();
            writer.WriteString("Lexeme", term.Lexeme);
            writer.WriteString("Display", term.Display);
            writer.WriteString("Kind", term.Kind.ToString());
            writer.WriteString("Source", term.Source.ToString());
            writer.WriteString("MatchedOn", term.MatchedOn);
            if (term.ConceptId is null)
                writer.WriteNull("ConceptId");
            else
                writer.WriteString("ConceptId", term.ConceptId);
            writer.WriteNumber("Weight", term.Weight);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

/// <summary>
/// EF Core value-conversion + value-comparison for <see cref="ExtractedTerms"/>
/// mapped to a <c>jsonb</c> column (F4-4). The comparer uses the VO's structural
/// equality (sequence-equal over the immutable, normalized term list).
/// </summary>
internal static class ExtractedTermsConversion
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new ExtractedTermsJsonConverter());
        return o;
    }

    public static readonly ValueConverter<ExtractedTerms, string> Converter =
        new(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<ExtractedTerms>(s, Options)!);

    public static readonly ValueComparer<ExtractedTerms> Comparer =
        new(
            (a, b) => a == null ? b == null : a.Equals(b),
            v => v.GetHashCode(),
            v => v);
}
