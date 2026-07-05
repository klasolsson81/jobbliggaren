using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// System.Text.Json converter for the <see cref="LanguageProficiency"/> SmartEnum inside the
/// Form B encrypted <c>content_enc</c> blob (ADR 0049 Form B / ADR 0094 D-C). SmartEnums have a
/// private constructor and do not round-trip through STJ by default; this writes the stable Name
/// token (e.g. "Native") and reads it back. The read is <b>tolerant</b> — an unknown, absent, or
/// null token materialises <see cref="LanguageProficiency.NotStated"/> and never throws (parity
/// with ADR 0049 Beslut 5 read-tolerance, so a legacy or forward-incompatible token can never
/// fail a decrypt-read of a whole CV).
/// </summary>
internal sealed class LanguageProficiencyJsonConverter : JsonConverter<LanguageProficiency>
{
    public override LanguageProficiency Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var token = reader.GetString();
                return token is not null
                    && LanguageProficiency.TryFromName(token, ignoreCase: true, out var value)
                        ? value
                        : LanguageProficiency.NotStated;
            case JsonTokenType.Null:
                return LanguageProficiency.NotStated;
            default:
                // A non-string, non-null token (a legacy object/array form, or a number) is not a
                // token we ever write; skip the whole value and fall back to NotStated.
                reader.Skip();
                return LanguageProficiency.NotStated;
        }
    }

    public override void Write(
        Utf8JsonWriter writer, LanguageProficiency value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);
}
