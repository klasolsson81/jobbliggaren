using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Auditing;
using Jobbliggaren.Infrastructure.JobAds.SnapshotMisses;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #510 — the 7-day snapshot-size baseline must read the metric the relative
/// floor COMPARES (<c>SnapshotOutcome.ParsedTotal</c>), not <c>Fetched</c>.
/// <c>Fetched</c> counts yields across ALL retry attempts (a
/// truncate-then-succeed run yields the parsed prefix twice), so a single such
/// run used to inflate <c>MAX(Fetched)</c> and suppress miss-tracking — and
/// therefore stale-ad archiving — for up to 7 days.
/// <para>
/// The seed goes through the REAL <see cref="SystemEventAuditor"/> (the shape
/// production emits — a <c>ParsedTotal</c> property-name drift in the record or
/// the SQL is caught here), asymmetrically, with three counterfactuals: an
/// inflated <c>Fetched</c> on every snapshot row (query-reads-Fetched mutant → RED),
/// a stream row with a huge <c>ParsedTotal</c> (JobType-filter mutant → RED) and an
/// out-of-window row with a huge <c>ParsedTotal</c> (window mutant → RED).
/// </para>
/// </summary>
public sealed class SnapshotBaselineMetricTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private ServiceProvider _provider = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetMaxObservedSnapshotSize_ReadsMaxParsedTotal_NotInflatedFetched()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditor = new SystemEventAuditor(
            db, new FixedCorrelationIdProvider(), NullLogger<SystemEventAuditor>.Instance);

        // R1+R2: healthy-window snapshot rows. Fetched is INFLATED above every
        // ParsedTotal (the truncate-then-succeed shape) — if the query still read
        // Fetched, MAX would be 95_000 and the assertion below goes red.
        await auditor.RecordAsync(SnapshotRow(now.AddHours(-1), fetched: 91_000, parsedTotal: 51_000), ct);
        await auditor.RecordAsync(SnapshotRow(now.AddHours(-26), fetched: 95_000, parsedTotal: 40_000), ct);

        // R3: a STREAM row carrying an absurd ParsedTotal — must be excluded by the
        // JobType='snapshot' filter.
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: Guid.NewGuid(), OccurredAt: now.AddHours(-1),
            Source: JobSource.Platsbanken.Value, JobType: "stream",
            Fetched: 999_999, Added: 0, Updated: 0, Archived: 0, Skipped: 0, Errors: 0,
            StartedAt: now.AddHours(-1), CompletedAt: now.AddHours(-1),
            ParsedTotal: 888_888), ct);

        // R4: a legacy-shaped snapshot row (no ParsedTotal — pre-#510 rows): its
        // NULL is excluded from MAX, and its inflated Fetched must stay unread.
        await auditor.RecordAsync(SnapshotRow(now.AddHours(-3), fetched: 91_000, parsedTotal: null), ct);

        // R5: an out-of-window snapshot row with a huge ParsedTotal — must be
        // excluded by the 7-day occurred_at filter.
        await auditor.RecordAsync(SnapshotRow(now.AddDays(-10), fetched: 10_000, parsedTotal: 999_999), ct);

        // R6: an in-window snapshot row for ANOTHER source — must be excluded by
        // the payload Source filter (fourth counterfactual, architect review).
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: Guid.NewGuid(), OccurredAt: now.AddHours(-1),
            Source: "other-source", JobType: "snapshot",
            Fetched: 0, Added: 0, Updated: 0, Archived: 0, Skipped: 0, Errors: 0,
            StartedAt: now.AddHours(-1), CompletedAt: now.AddHours(-1),
            ParsedTotal: 777_777), ct);

        var tracker = new JobAdSnapshotMissTracker(db, NullLogger<JobAdSnapshotMissTracker>.Instance);
        var max7d = await tracker.GetMaxObservedSnapshotSizeAsync(JobSource.Platsbanken, days: 7, ct);

        max7d.ShouldBe(51_000);
    }

    [Fact]
    public async Task GetMaxObservedSnapshotSize_WhenNoRowCarriesParsedTotal_ReturnsNull()
    {
        // Warm-up/back-compat: right after #510 deploys, the whole 7-day window is
        // legacy rows without ParsedTotal → baseline NULL → the relative floor is
        // INACTIVE (the deliberate cold-start semantics, CTO 2026-05-23 Q5; the
        // absolute floor backstops) — NOT read-Fetched, NOT zero.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditor = new SystemEventAuditor(
            db, new FixedCorrelationIdProvider(), NullLogger<SystemEventAuditor>.Instance);

        await auditor.RecordAsync(SnapshotRow(now.AddHours(-2), fetched: 91_000, parsedTotal: null), ct);

        var tracker = new JobAdSnapshotMissTracker(db, NullLogger<JobAdSnapshotMissTracker>.Instance);
        var max7d = await tracker.GetMaxObservedSnapshotSizeAsync(JobSource.Platsbanken, days: 7, ct);

        max7d.ShouldBeNull();
    }

    private static JobAdsSynced SnapshotRow(DateTimeOffset occurredAt, int fetched, int? parsedTotal) =>
        new(
            AggregateId: Guid.NewGuid(),
            OccurredAt: occurredAt,
            Source: JobSource.Platsbanken.Value,
            JobType: "snapshot",
            Fetched: fetched,
            Added: 0, Updated: 0, Archived: 0, Skipped: 0, Errors: 0,
            StartedAt: occurredAt, CompletedAt: occurredAt,
            ParsedTotal: parsedTotal);

    private sealed class FixedCorrelationIdProvider : ICorrelationIdProvider
    {
        public Guid Current { get; } = Guid.NewGuid();
    }
}
