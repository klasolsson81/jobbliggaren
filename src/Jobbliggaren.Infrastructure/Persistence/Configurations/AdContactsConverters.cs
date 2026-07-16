using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence surface for <see cref="AdContacts"/> (#842 Tier A). Mirrors
/// <see cref="ExtractedTermsJsonConverter"/>: a property-level <see cref="ValueConverter"/> to a
/// <c>jsonb</c> column with a tolerant hand-written <see cref="JsonConverter{T}"/> — deliberately
/// NOT <c>OwnsMany().ToJson()</c> (architect Q3: the owned-collection identity traps that already
/// bit <c>Company.Erased</c>, and ToJson's enum mapping gotchas). The jsonb is an array of contact
/// objects with PascalCase keys; <c>Origin</c> is persisted BY NAME, never by ordinal, so an enum
/// reordering cannot silently re-label a stored contact's provenance (Declared vs
/// ExtractedFromBody is a truth claim, not a display hint). Re-validates through
/// <see cref="AdContacts.FromPersisted"/> on read (single normalization point; fail-loud on
/// corrupt jsonb). Lives in Infrastructure — Domain stays serialization-/EF-free (CLAUDE.md §2.1).
/// </summary>
internal sealed class AdContactsJsonConverter : JsonConverter<AdContacts>
{
    public override AdContacts Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("AdContacts-jsonb måste vara en array.");

        var contacts = new List<AdContact?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("AdContacts-arrayen får bara innehålla objekt.");
            contacts.Add(ReadContact(ref reader));
        }

        return AdContacts.FromPersisted(contacts);
    }

    private static AdContact? ReadContact(ref Utf8JsonReader reader)
    {
        string? name = null, role = null, email = null, phone = null;
        AdContactOrigin? origin = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Oväntad token i AdContact-objektet.");

            var prop = reader.GetString();
            reader.Read();
            switch (prop)
            {
                case "Name": name = ReadNullableString(ref reader); break;
                case "Role": role = ReadNullableString(ref reader); break;
                case "Email": email = ReadNullableString(ref reader); break;
                case "Phone": phone = ReadNullableString(ref reader); break;
                case "Origin":
                    var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    if (raw is null
                        || !Enum.TryParse<AdContactOrigin>(raw, ignoreCase: false, out var parsed)
                        || !Enum.IsDefined(parsed))
                    {
                        throw new JsonException($"AdContact.Origin har ett okänt värde: '{raw}'.");
                    }
                    origin = parsed;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (origin is null)
            throw new JsonException("AdContact-objektet saknar Origin.");

        // TryCreate drops an all-null entry — corrupt-but-empty stored rows degrade to absence
        // rather than throwing, which matches the write-side semantics (blank is absence).
        return AdContact.TryCreate(name, role, email, phone, origin.Value);
    }

    private static string? ReadNullableString(ref Utf8JsonReader reader)
        => reader.TokenType == JsonTokenType.Null ? null : reader.GetString();

    public override void Write(
        Utf8JsonWriter writer, AdContacts value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var contact in value.Contacts)
        {
            writer.WriteStartObject();
            WriteNullable(writer, "Name", contact.Name);
            WriteNullable(writer, "Role", contact.Role);
            WriteNullable(writer, "Email", contact.Email);
            WriteNullable(writer, "Phone", contact.Phone);
            writer.WriteString("Origin", contact.Origin.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}

/// <summary>
/// EF Core value-conversion + value-comparison for <see cref="AdContacts"/> mapped to a
/// <c>jsonb</c> column (#842 Tier A). The comparer uses the VO's structural equality
/// (sequence-equal over the immutable, canonically-ordered contact list — see the AdContacts
/// single-canonical-order remark: write path and read path sort identically, so a reload never
/// looks like a phantom change).
/// </summary>
internal static class AdContactsConversion
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new AdContactsJsonConverter());
        return o;
    }

    public static readonly ValueConverter<AdContacts, string> Converter =
        new(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<AdContacts>(s, Options)!);

    public static readonly ValueComparer<AdContacts> Comparer =
        new(
            (a, b) => a == null ? b == null : a.Equals(b),
            v => v.GetHashCode(),
            v => v);
}
