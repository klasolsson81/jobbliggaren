using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Infrastructure.Time;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Reads a JobTech date as the instant it names: an offsetless value is Swedish wall-clock time
/// (<see cref="SwedishCalendar.FromSwedishWallClock"/>), an explicit <c>Z</c> or offset is honoured,
/// and the result is at <c>Offset == Zero</c>. A value it cannot turn into an instant throws
/// <see cref="JsonException"/>. The wire convention: ADR 0032 Amendment 2026-10-08.
/// </summary>
internal sealed class JobTechSwedishDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTime(out var parsed))
            throw new JsonException("JobTech-datumet är inte ett giltigt ISO 8601-värde.");

        if (parsed.Kind != DateTimeKind.Unspecified)
        {
            return reader.TryGetDateTimeOffset(out var withOffset)
                ? withOffset.ToUniversalTime()
                : throw new JsonException("JobTech-datumet är inte ett giltigt ISO 8601-värde.");
        }

        try
        {
            return SwedishCalendar.FromSwedishWallClock(parsed);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException("JobTech-datumet ligger utanför giltigt intervall.", ex);
        }
    }

    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
