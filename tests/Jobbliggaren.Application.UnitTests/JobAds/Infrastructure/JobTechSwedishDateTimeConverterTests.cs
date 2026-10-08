using System.Text.Json;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Infrastructure;

/// <summary>
/// JobTech sends Swedish wall-clock time without an offset (ADR 0032 Amendment 2026-10-08). The
/// expected values are hard-coded UTC instants and every case asserts <c>Offset == Zero</c>, so a
/// property that loses the converter fails on any host.
/// </summary>
public class JobTechSwedishDateTimeConverterTests
{
    private static JobTechHit Deserialize(string json) =>
        JsonSerializer.Deserialize<JobTechHit>(json).ShouldNotBeNull();

    [Fact]
    public void Deserialize_ReadsAnOffsetlessSummerValue_AsSwedishTime()
    {
        // The live shape, measured against jobsearch.api.jobtechdev.se at 17:10Z on 2026-10-07.
        var hit = Deserialize("""{ "id": "1", "publication_date": "2026-10-07T19:09:40" }""");

        hit.PublicationDate.ShouldBe(new DateTimeOffset(2026, 10, 7, 17, 9, 40, TimeSpan.Zero));
        hit.PublicationDate!.Value.Offset.ShouldBe(TimeSpan.Zero, "Npgsql writes timestamptz only at offset zero");
    }

    [Fact]
    public void Deserialize_ReadsAnOffsetlessWinterValue_AsSwedishTime()
    {
        // CET is +01:00 — a hard-coded two-hour shift would fail here and pass the summer case.
        var hit = Deserialize("""{ "id": "1", "publication_date": "2026-01-15T10:00:00" }""");

        hit.PublicationDate.ShouldBe(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
        hit.PublicationDate!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Deserialize_KeepsALastApplicationDayOnItsSwedishDate()
    {
        // 23:59:59 Swedish on the 6th.
        var hit = Deserialize("""{ "id": "1", "last_publication_date": "2026-11-06T23:59:59" }""");

        hit.LastPublicationDate.ShouldBe(new DateTimeOffset(2026, 11, 6, 22, 59, 59, TimeSpan.Zero));
        hit.LastPublicationDate!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Deserialize_ReadsRemovedDate_AsSwedishTime()
    {
        var hit = Deserialize("""{ "id": "1", "removed": true, "removed_date": "2026-10-07T19:09:40" }""");

        hit.RemovedDate.ShouldBe(new DateTimeOffset(2026, 10, 7, 17, 9, 40, TimeSpan.Zero));
        hit.RemovedDate!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-03-29T02:30:00")] // the spring gap: this wall-clock time does not exist
    [InlineData("2026-10-25T02:30:00")] // the repeated autumn hour
    public void Deserialize_ResolvesTheDstTransitionHours_ToStandardTime(string wire)
    {
        var hit = Deserialize($$"""{ "id": "1", "publication_date": "{{wire}}" }""");

        hit.PublicationDate!.Value.TimeOfDay.ShouldBe(new TimeSpan(1, 30, 0));
        hit.PublicationDate!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-05-12T10:00:00Z")]
    [InlineData("2026-05-12T12:00:00+02:00")]
    [InlineData("2026-05-12T05:00:00-05:00")]
    public void Deserialize_HonoursAnExplicitOffset(string wire)
    {
        var hit = Deserialize($$"""{ "id": "1", "publication_date": "{{wire}}" }""");

        hit.PublicationDate.ShouldBe(new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero));
        hit.PublicationDate!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Deserialize_LeavesNullAndMissingDatesNull()
    {
        var hit = Deserialize("""{ "id": "1", "publication_date": null }""");

        hit.PublicationDate.ShouldBeNull();
        hit.LastPublicationDate.ShouldBeNull();
        hit.RemovedDate.ShouldBeNull();
    }

    [Theory]
    [InlineData("\"inte-ett-datum\"")]
    [InlineData("\"07/10/2026 19:09\"")]
    [InlineData("1759856980")]
    [InlineData("\"0001-01-01T00:00:00\"")] // valid ISO, but Swedish midnight is before DateTimeOffset.MinValue
    public void Deserialize_ThrowsJsonException_OnADateItCannotRead(string wire)
    {
        // JsonException is the class JobTechStreamClient skips ONE element on.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<JobTechHit>($$"""{ "id": "1", "publication_date": {{wire}} }"""));
    }

    [Fact]
    public void RawPayload_StoresTheCorrectedInstant()
    {
        // The raw_payload path: JsonSerializer.Serialize(hit) → JobTechPayloadSanitizer.
        var hit = Deserialize("""{ "id": "1", "publication_date": "2026-10-07T19:09:40" }""");

        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(JsonSerializer.Serialize(hit));

        using var stored = JsonDocument.Parse(sanitized);
        var publicationDate = stored.RootElement.GetProperty("publication_date").GetDateTimeOffset();
        publicationDate.ShouldBe(new DateTimeOffset(2026, 10, 7, 17, 9, 40, TimeSpan.Zero));
        publicationDate.Offset.ShouldBe(TimeSpan.Zero);
    }
}
