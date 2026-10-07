using System.Text.Json;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Infrastructure;

/// <summary>
/// JobTech sends Swedish wall-clock time without an offset; the built-in converter read it as the
/// host's local time, so on the UTC box every ad was stamped one to two hours late (/jobb showed
/// "idag, kl. 20:53" at 19:04). The expected values below are hard-coded UTC instants, so they hold
/// on any host — and they discriminate on a UTC host (CI), where the old reading kept the Swedish
/// digits as UTC. On a Swedish dev machine the old reading happened to agree.
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
    }

    [Fact]
    public void Deserialize_KeepsALastApplicationDayOnItsSwedishDate()
    {
        // 23:59:59 Swedish on the 6th. Read as UTC it was 01:59:59 on the 7th in Sweden, and the
        // card printed "Sista ansökningsdag 7 nov." for an ad that closes on the 6th.
        var hit = Deserialize("""{ "id": "1", "last_publication_date": "2026-11-06T23:59:59" }""");

        hit.LastPublicationDate.ShouldBe(new DateTimeOffset(2026, 11, 6, 22, 59, 59, TimeSpan.Zero));
    }

    [Fact]
    public void Deserialize_ReadsRemovedDate_AsSwedishTime()
    {
        var hit = Deserialize("""{ "id": "1", "removed": true, "removed_date": "2026-10-07T19:09:40" }""");

        hit.RemovedDate.ShouldBe(new DateTimeOffset(2026, 10, 7, 17, 9, 40, TimeSpan.Zero));
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
    public void Deserialize_ThrowsJsonException_OnAMalformedDate(string wire)
    {
        // JsonException is the class JobTechStreamClient skips ONE element on; anything else would be
        // read as transport truncation and end the whole stream.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<JobTechHit>($$"""{ "id": "1", "publication_date": {{wire}} }"""));
    }

    [Fact]
    public void RoundTrip_ThroughTheRawPayloadSerialisation_PreservesTheInstant()
    {
        // PlatsbankenJobSource writes raw_payload with JsonSerializer.Serialize(hit); the written
        // value carries its offset, so reading it back must not shift it a second time.
        var hit = Deserialize("""{ "id": "1", "publication_date": "2026-10-07T19:09:40" }""");

        var json = JsonSerializer.Serialize(hit);
        var roundTripped = Deserialize(json);

        roundTripped.PublicationDate.ShouldBe(hit.PublicationDate);
    }
}
