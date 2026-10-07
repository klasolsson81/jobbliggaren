using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Infrastructure.Time;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Reads a JobTech date as the instant it names. JobTech sends Swedish wall-clock time with NO
/// offset — <c>"publication_date": "2026-10-07T19:09:40"</c> came back from
/// <c>jobsearch.api.jobtechdev.se/search</c> at 17:10Z on 2026-10-07 — and System.Text.Json reads
/// an offsetless string as the HOST's local time. On the production box (UTC) the Swedish digits
/// therefore became the same digits in UTC: two hours late in summer, one in winter. /jobb printed
/// "idag, kl. 20:53" at 19:04, and a <c>last_publication_date</c> of 23:59:59 on the 6th showed as
/// the 7th.
///
/// <para>
/// An offsetless value is read as <see cref="SwedishCalendar.ZoneId"/>; an explicit <c>Z</c> or
/// offset is honoured as written. The result is normalised to <c>Offset == Zero</c>, which Npgsql
/// requires for <c>timestamptz</c>. In the repeated autumn hour <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
/// picks standard time, so a value there can be an hour early — the wire carries nothing that
/// would tell the two apart.
/// </para>
///
/// <para>
/// A malformed value throws <see cref="JsonException"/>, exactly as the built-in converter did:
/// <c>JobTechStreamClient</c> skips that one element on it, and nothing else.
/// </para>
/// </summary>
internal sealed class JobTechSwedishDateTimeConverter : JsonConverter<DateTimeOffset>
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(SwedishCalendar.ZoneId);

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // TryGetDateTime is the same strict ISO 8601 profile the built-in converter parses with, and
        // it keeps what that converter throws away: whether the string carried an offset at all.
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTime(out var parsed))
            throw new JsonException("JobTech-datumet är inte ett giltigt ISO 8601-värde.");

        return parsed.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(parsed, Zone.GetUtcOffset(parsed)).ToUniversalTime()
            : new DateTimeOffset(parsed.ToUniversalTime(), TimeSpan.Zero);
    }

    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
