using Hangfire;
using Hangfire.Logging;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.Server;
using Jobbliggaren.Api.BackgroundJobs;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1982 (ADR 0159) — the Worker's sign of life, read from REAL Hangfire storage on a real Postgres under a
/// <c>Europe/Berlin</c> session. The actor that produces a server record is a running <see cref="BackgroundJobServer"/>
/// (its announce and its heartbeat), and the actor that removes one is that server stopping gracefully; no record is
/// seeded by hand. A killed Worker leaves its record behind with an ageing heartbeat, so "silent" is produced here by
/// the clock moving on, which is the actor that does it in production (AGENTS.md §5, Tests).
///
/// <para>
/// The superuser connection proves nothing about the Api's Hangfire role; that the <c>hangfire</c> schema is closed to
/// every other role is <c>docs/runbooks/hangfire-schema.md</c> §4, and the reason the adapter goes through
/// <c>JobStorage</c> at all.
/// </para>
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class HangfireWorkerHeartbeatSourceTests : IAsyncLifetime
{
    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;
    private JobStorage _storage = default!;
    private readonly List<BackgroundJobServer> _servers = [];

    public HangfireWorkerHeartbeatSourceTests(SharedPostgresFixture postgres) => _postgres = postgres;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        QuietHangfireLogs();
        _connectionString = new NpgsqlConnectionStringBuilder(await _postgres.CreateDatabaseAsync())
        {
            Timezone = "Europe/Berlin",
        }.ConnectionString;
        var options = new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true };
        _storage = new PostgreSqlStorage(new NpgsqlConnectionFactory(_connectionString, options), options);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        (_storage as IDisposable)?.Dispose();
        await _postgres.DropDatabaseAsync(_connectionString);
    }

    private HangfireWorkerHeartbeatSource Sut() => new(_storage);

    // Hangfire keeps ONE process-wide log provider, and every ApiFactory host in this run sets it to its own
    // LoggerFactory, which is disposed with the host. A server or storage built after such a host would log through a
    // disposed factory and throw in its constructor. This class owns no host, so it logs nowhere.
    private static void QuietHangfireLogs() => LogProvider.SetCurrentLogProvider(new SilentLogProvider());

    private BackgroundJobServer StartServer()
    {
        QuietHangfireLogs();
        var server = new BackgroundJobServer(
            new BackgroundJobServerOptions { WorkerCount = 1, ServerName = $"test-{Guid.NewGuid():N}" }, _storage);
        _servers.Add(server);
        return server;
    }

    private async Task<WorkerHeartbeatReading> EventuallyAsync(Func<WorkerHeartbeatReading, bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var reading = Sut().Read();
            if (condition(reading))
            {
                return reading;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public void Read_WithNoServerEverStarted_ReportsNoRecordsAndNoHeartbeat()
    {
        var reading = Sut().Read();

        reading.ShouldBe(new WorkerHeartbeatReading(HasRecords: false, NewestHeartbeat: null));
    }

    [Fact]
    public async Task Read_ARunningServer_ReportsARecordWhoseHeartbeatIsTheStorageClockInUtc()
    {
        StartServer();

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var reading = await EventuallyAsync(r => r.HasRecords, "the server never announced itself");

        // Under a Berlin session an hour-or-two error would put this far outside the bound: the reading is UTC.
        var heartbeat = reading.NewestHeartbeat.ShouldNotBeNull();
        heartbeat.Offset.ShouldBe(TimeSpan.Zero);
        heartbeat.ShouldBeGreaterThan(before);
        heartbeat.ShouldBeLessThan(DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Read_TwoServers_ReportOneFlagAndTheNewestHeartbeat_NeverACount()
    {
        StartServer();
        StartServer();
        await EventuallyAsync(r => r.HasRecords, "no server announced itself");
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);

        var reading = Sut().Read();

        reading.HasRecords.ShouldBeTrue();
        // The port has no field that could hold a count: the shape is the guard.
        typeof(WorkerHeartbeatReading).GetProperties().Select(p => p.Name).ShouldBe(["HasRecords", "NewestHeartbeat"]);
    }

    [Fact]
    public async Task Read_AfterAGracefulStop_TheRecordIsGone()
    {
        var server = StartServer();
        await EventuallyAsync(r => r.HasRecords, "the server never announced itself");

        server.Dispose();

        var reading = await EventuallyAsync(r => !r.HasRecords, "a gracefully stopped server leaves its record behind");
        reading.NewestHeartbeat.ShouldBeNull();
    }

    [Fact]
    public async Task Verdict_ALiveWorkerIsAlive_AndTheClockMovingOnTurnsItSilent()
    {
        StartServer();
        var reading = await EventuallyAsync(r => r.HasRecords, "the server never announced itself");
        var sampledAt = DateTimeOffset.UtcNow;
        var snapshot = new ServiceObservationSnapshot(
            sampledAt.AddHours(-1),
            SourceSample.Of(sampledAt, new WorkerObservation(reading.HasRecords, reading.NewestHeartbeat)),
            SourceSample.Missing<bool>(),
            SourceSample.Missing<ImportObservation>());
        var options = new ServiceObservationOptions();

        // Now: a sign of life seconds old.
        ServiceStatusEvaluator.Evaluate(snapshot, sampledAt, options).BackgroundJobs.State.ShouldBe(WorkerProcessState.Alive);

        // Ten minutes on, with the process killed and the sampler still running: the record stays, its heartbeat does not move.
        var killedSampledAt = sampledAt.AddMinutes(10);
        var later = new ServiceObservationSnapshot(
            snapshot.ObservedSince,
            SourceSample.Of(killedSampledAt, new WorkerObservation(reading.HasRecords, reading.NewestHeartbeat)),
            SourceSample.Missing<bool>(),
            SourceSample.Missing<ImportObservation>());
        var verdict = ServiceStatusEvaluator.Evaluate(later, killedSampledAt, options).BackgroundJobs;
        verdict.State.ShouldBe(WorkerProcessState.Silent);
        verdict.HeartbeatAt.ShouldBe(reading.NewestHeartbeat);
    }

    // The two defaults the 120 s alive window is derived from. If a Hangfire upgrade moves them, the window is wrong.
    [Fact]
    public void HangfiresDefaultsTheAliveWindowIsDerivedFrom_AreUnchanged()
    {
        var defaults = new BackgroundJobServerOptions();

        defaults.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(30));
        defaults.ServerTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        ServiceObservationLimits.WorkerAliveWithin.ShouldBeGreaterThan(2 * defaults.HeartbeatInterval);
        ServiceObservationLimits.WorkerAliveWithin.ShouldBeLessThan(defaults.ServerTimeout);
    }

    private sealed class SilentLogProvider : ILogProvider
    {
        public ILog GetLogger(string name) => new SilentLog();
    }

    private sealed class SilentLog : ILog
    {
        public bool Log(Hangfire.Logging.LogLevel logLevel, Func<string>? messageFunc, Exception? exception = null) => true;
    }
}
