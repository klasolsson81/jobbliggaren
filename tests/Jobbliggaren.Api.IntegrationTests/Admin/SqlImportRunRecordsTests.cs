using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Admin.Services;
using Jobbliggaren.Infrastructure.Auditing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1982 (ADR 0159) — the newest recorded stream run, read from a real audit log.
///
/// <para>
/// Every row goes through the REAL <see cref="SystemEventAuditor"/> with the real <see cref="JobAdsSynced"/>: the reader
/// depends on the key names and casing the serialiser emits (<c>Source</c>, <c>JobType</c>, <c>Errors</c>) and on
/// <c>occurred_at</c> being the run's completion, and a rename in the record or the query is caught here (AGENTS.md §5,
/// Tests). The session runs under <c>Europe/Berlin</c>, so a reader that mixed up local and UTC time would be an hour or
/// two off. The out-of-window, other-source, other-job-type and snapshot rows are the counterfactuals: each one is the
/// row a mutant of the corresponding filter would return.
/// </para>
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class SqlImportRunRecordsTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    public SqlImportRunRecordsTests(SharedPostgresFixture postgres) => _postgres = postgres;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = new NpgsqlConnectionStringBuilder(await _postgres.CreateDatabaseAsync())
        {
            Timezone = "Europe/Berlin",
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DropDatabaseAsync(_connectionString);
    }

    private SqlImportRunRecords Sut() => new(_provider.GetRequiredService<IServiceScopeFactory>());

    private async Task RecordAsync(JobAdsSynced run)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new SystemEventAuditor(db, new FixedCorrelation(), NullLogger<SystemEventAuditor>.Instance)
            .RecordAsync(run, Ct);
    }

    private static JobAdsSynced Run(
        DateTimeOffset completedAt, string jobType = JobAdsSynced.StreamJobType, string? source = null, int errors = 0) =>
        new(
            AggregateId: Guid.NewGuid(),
            OccurredAt: completedAt,
            Source: source ?? JobSource.Platsbanken.Value,
            JobType: jobType,
            Fetched: 120, Added: 3, Updated: 4, Archived: 1, Skipped: 2, Errors: errors,
            StartedAt: completedAt.AddSeconds(-9), CompletedAt: completedAt);

    [Fact]
    public async Task Latest_ReturnsTheNewestStreamRunOfTheSourceInsideTheWindow_WithItsEndAndItsFailedEvents()
    {
        await RecordAsync(Run(Now.AddHours(-1), errors: 9));
        await RecordAsync(Run(Now.AddMinutes(-3), errors: 4));
        await RecordAsync(Run(Now.AddMinutes(-13), errors: 0));

        var run = await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct);

        run.ShouldNotBeNull();
        (run.CompletedAt - Now.AddMinutes(-3)).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(1));
        run.CompletedAt.Offset.ShouldBe(TimeSpan.Zero);
        run.Errors.ShouldBe(4);
    }

    [Fact]
    public async Task Latest_ARunWithNoFailedEvents_IsReturnedWithZero_NotAsNothing()
    {
        await RecordAsync(Run(Now.AddMinutes(-3), errors: 0));

        var run = await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct);

        run.ShouldNotBeNull().Errors.ShouldBe(0);
    }

    [Fact]
    public async Task Latest_NewerRowsThatAreNotAStreamRunOfThisSource_AreNotReturned()
    {
        // The older stream run is the answer; every newer row below is one a mutant of a filter would return.
        await RecordAsync(Run(Now.AddMinutes(-20), errors: 1));
        await RecordAsync(Run(Now.AddMinutes(-2), JobAdsSynced.SnapshotJobType, errors: 7));
        await RecordAsync(Run(Now.AddMinutes(-3), "backfill-extracted-terms", errors: 6));
        await RecordAsync(Run(Now.AddMinutes(-4), source: "LinkedIn", errors: 5));

        var run = await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct);

        run.ShouldNotBeNull();
        run.Errors.ShouldBe(1);
        (run.CompletedAt - Now.AddMinutes(-20)).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Latest_ARunOlderThanTheWindow_IsNotReturned_SoNothingWasRecordedForADay()
    {
        await RecordAsync(Run(Now - ServiceObservationLimits.ImportWindow - TimeSpan.FromMinutes(1), errors: 2));

        var run = await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct);

        run.ShouldBeNull();
    }

    [Fact]
    public async Task Latest_TheBoundComesFromTheCallersClock_NotTheDatabases()
    {
        // The run is 3 minutes before the injected "now" and a year before the database's real one: a query
        // that used now() would find nothing.
        await RecordAsync(Run(Now.AddMinutes(-3)));

        var run = await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct);

        run.ShouldNotBeNull();
    }

    [Fact]
    public async Task Latest_AnEmptyAuditLog_IsNull()
    {
        var run = await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct);

        run.ShouldBeNull();
    }

    // Declared unreachable: every writer of System.JobAdsSynced emits Errors. This asserts only that the read side
    // fails loudly (the sampler turns it into an unknown row) instead of inventing a zero for an unreadable count.
    [Fact]
    public async Task Latest_AStreamRowWithNoReadableErrorCount_FailsInsteadOfBecomingZero()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogEntries.Add(AuditLogEntry.CreateSystemEvent(
                Now.AddMinutes(-3), Guid.NewGuid(), JobAdsSynced.EventTypeName, "System.JobAdSync", Guid.NewGuid(),
                payload: $$"""{"Source":"Platsbanken","JobType":"stream"}"""));
            await db.SaveChangesAsync(Ct);
        }

        await Should.ThrowAsync<InvalidDataException>(async () =>
            await Sut().LatestStreamRunAsync(JobSource.Platsbanken, Now - ServiceObservationLimits.ImportWindow, Ct));
    }

    [Fact]
    public void TheEventTypeAndJobTypeTokens_AreTheOnesThePriorReaderAndBothJobsAlreadyUse()
    {
        // One constant per token, shared by the writers and the readers; the strings themselves are persisted history.
        JobAdsSynced.EventTypeName.ShouldBe("System.JobAdsSynced");
        JobAdsSynced.StreamJobType.ShouldBe("stream");
        JobAdsSynced.SnapshotJobType.ShouldBe("snapshot");
    }

    private sealed class FixedCorrelation : ICorrelationIdProvider
    {
        public Guid Current { get; } = Guid.NewGuid();
    }
}
