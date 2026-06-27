using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Jobbliggaren.Api.Endpoints;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// Ren projektions-/sanerings-test (ingen ApiFactory-fixtur, ingen Hangfire-
/// server) av <see cref="AdminBackgroundJobsProjection"/>. Bevisar säkerhets-
/// invarianten security-auditor satte som must-clear för #204: en misslyckad-jobb-
/// vy får ALDRIG bära rå exception-message, stack trace eller job-arguments —
/// bara den PII-fria undantags-typen ytsätts som felkategori (GDPR Art. 5,
/// CLAUDE.md §5 personnummer-guard). Lever i integrationstest-projektet eftersom
/// det är det enda testprojektet som refererar Api-lagret + Hangfire-typerna;
/// testet självt rör varken DB eller HTTP.
/// </summary>
public class AdminBackgroundJobsProjectionTests
{
    private const string Personnummer = "19900101-1234";

    [Fact]
    public void ToFailedDto_surfaces_only_the_exception_type_name_never_message_or_trace()
    {
        var failed = new FailedJobDto
        {
            Job = Job.FromExpression(() => SampleFailingJob.Run()),
            ExceptionType = "Microsoft.EntityFrameworkCore.DbUpdateException",
            // De två fälten nedan är den exakta PII-vektorn invarianten skyddar mot.
            ExceptionMessage = $"Kunde inte spara: personnummer {Personnummer} kolliderade",
            ExceptionDetails = $"   at Foo.Bar() med personnummer {Personnummer}\n   at Baz.Qux()",
            Reason = $"personnummer {Personnummer}",
            FailedAt = new DateTime(2026, 6, 27, 3, 20, 0, DateTimeKind.Utc),
        };

        var dto = AdminBackgroundJobsProjection.ToFailedDto("job-42", failed);

        dto.JobId.ShouldBe("job-42");
        dto.JobType.ShouldBe(nameof(SampleFailingJob));
        dto.ErrorCategory.ShouldBe("DbUpdateException");
        dto.FailedAt.ShouldBe(new DateTimeOffset(2026, 6, 27, 3, 20, 0, TimeSpan.Zero));

        // Hela DTO:n serialiserad får inte innehålla något PII-spår. Detta är
        // det bärande säkerhets-assertet — strukturellt har DTO:n inga
        // message-/details-fält, men vi bevisar att inget värde smiter igenom.
        var serialized = System.Text.Json.JsonSerializer.Serialize(dto);
        serialized.ShouldNotContain(Personnummer);
        serialized.ShouldNotContain("at Foo.Bar");
        serialized.ShouldNotContain("Kunde inte spara");
    }

    [Fact]
    public void ToFailedDto_falls_back_to_generic_label_when_exception_type_missing()
    {
        var failed = new FailedJobDto
        {
            Job = Job.FromExpression(() => SampleFailingJob.Run()),
            ExceptionType = null,
            ExceptionMessage = $"personnummer {Personnummer}",
            FailedAt = null,
        };

        var dto = AdminBackgroundJobsProjection.ToFailedDto("job-7", failed);

        dto.ErrorCategory.ShouldBe("(okänt fel)");
        dto.FailedAt.ShouldBeNull();
        dto.ErrorCategory.ShouldNotContain(Personnummer);
    }

    [Fact]
    public void ToRecurringDto_maps_status_fields_and_stamps_utc()
    {
        var recurring = new RecurringJobDto
        {
            Id = "background-matching",
            Cron = "20 3 * * *",
            LastExecution = new DateTime(2026, 6, 27, 3, 20, 0, DateTimeKind.Unspecified),
            LastJobState = "Succeeded",
            NextExecution = new DateTime(2026, 6, 28, 3, 20, 0, DateTimeKind.Unspecified),
        };

        var dto = AdminBackgroundJobsProjection.ToRecurringDto(recurring);

        dto.Id.ShouldBe("background-matching");
        dto.Cron.ShouldBe("20 3 * * *");
        dto.LastJobState.ShouldBe("Succeeded");
        // Unspecified-kind stämplas till UTC → entydig offset 0.
        dto.LastExecution.ShouldBe(new DateTimeOffset(2026, 6, 27, 3, 20, 0, TimeSpan.Zero));
        dto.NextExecution.ShouldBe(new DateTimeOffset(2026, 6, 28, 3, 20, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToRecurringDto_tolerates_a_never_run_job()
    {
        var recurring = new RecurringJobDto
        {
            Id = "digest-dispatch-weekly",
            Cron = "0 6 * * 1",
            LastExecution = null,
            LastJobState = null,
            NextExecution = new DateTime(2026, 6, 29, 6, 0, 0, DateTimeKind.Utc),
        };

        var dto = AdminBackgroundJobsProjection.ToRecurringDto(recurring);

        dto.LastExecution.ShouldBeNull();
        dto.LastJobState.ShouldBeNull();
        dto.NextExecution.ShouldNotBeNull();
    }

    // Minsta möjliga jobb-yta för Job.FromExpression — bara så att Job.Type.Name
    // har ett deterministiskt värde att asserta mot. Körs aldrig.
    public static class SampleFailingJob
    {
        public static void Run()
        {
        }
    }
}
