using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Auditing.Jobs.AuditLogRetention;
using JobbPilot.Domain.Common;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace JobbPilot.Worker.IntegrationTests.Auditing;

/// <summary>
/// End-to-end smoke-test för <see cref="AuditLogRetentionJob"/> mot riktig
/// Postgres (Testcontainers). Verifierar:
/// <list type="bullet">
/// <item>Idempotent partition-skapande (CREATE IF NOT EXISTS)</item>
/// <item>Lexikografisk filtrering av gamla partitions (audit_log_YYYYMMDD-mönster)</item>
/// <item>Default-partitionen skyddas alltid (regex-filtret)</item>
/// <item>End-to-end RunAsync gör båda operationerna i en körning</item>
/// </list>
///
/// Märkt <c>[Trait("Category", "SmokeTest")]</c> — körs INTE i default <c>dotnet test</c>.
/// Kör explicit: <c>dotnet test --filter "Category=SmokeTest"</c>.
///
/// Tester använder unika partition-datum (1900-tal eller 2030-tal) för att inte
/// kollidera med bootstrap-partitions från migrationen (real-now ± 7 dagar).
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class AuditLogRetentionJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    [Fact]
    public async Task EnsureNextDayPartition_CreatesPartitionWhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        // Far-future datum så vi vet partitionen inte finns från bootstrap
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        const string expectedName = "audit_log_20300102";

        await DropPartitionIfExistsAsync(expectedName, ct);
        (await PartitionExistsAsync(expectedName, ct)).ShouldBeFalse();

        using var scope = _fixture.Services.CreateScope();
        var maintainer = scope.ServiceProvider.GetRequiredService<IAuditPartitionMaintainer>();

        var result = await maintainer.EnsureNextDayPartitionAsync(now, ct);

        result.ShouldBe(expectedName);
        (await PartitionExistsAsync(expectedName, ct)).ShouldBeTrue();

        // Cleanup
        await DropPartitionIfExistsAsync(expectedName, ct);
    }

    [Fact]
    public async Task EnsureNextDayPartition_IsIdempotentWhenAlreadyExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
        const string expectedName = "audit_log_20300602";

        await DropPartitionIfExistsAsync(expectedName, ct);

        using var scope = _fixture.Services.CreateScope();
        var maintainer = scope.ServiceProvider.GetRequiredService<IAuditPartitionMaintainer>();

        // Första körning skapar
        var first = await maintainer.EnsureNextDayPartitionAsync(now, ct);
        // Andra körning ska inte kasta
        var second = await maintainer.EnsureNextDayPartitionAsync(now, ct);

        first.ShouldBe(expectedName);
        second.ShouldBe(expectedName);
        (await PartitionExistsAsync(expectedName, ct)).ShouldBeTrue();

        await DropPartitionIfExistsAsync(expectedName, ct);
    }

    [Fact]
    public async Task DropPartitionsOlderThan_DropsOldPartitionsSkipsDefaultAndRecent()
    {
        var ct = TestContext.Current.CancellationToken;
        // Skapa två fake gamla partitions (1900-tal — långt före retention-fönster)
        const string oldPartitionA = "audit_log_19990101";
        const string oldPartitionB = "audit_log_19990501";
        await CreatePartitionIfMissingAsync(oldPartitionA, "1999-01-01", "1999-01-02", ct);
        await CreatePartitionIfMissingAsync(oldPartitionB, "1999-05-01", "1999-05-02", ct);

        // Bootstrap-partitions från migrationen finns redan kring real-now
        // Default-partitionen finns alltid

        var cutoff = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using var scope = _fixture.Services.CreateScope();
        var maintainer = scope.ServiceProvider.GetRequiredService<IAuditPartitionMaintainer>();

        var dropped = await maintainer.DropPartitionsOlderThanAsync(cutoff, ct);

        dropped.ShouldContain(oldPartitionA);
        dropped.ShouldContain(oldPartitionB);
        (await PartitionExistsAsync(oldPartitionA, ct)).ShouldBeFalse();
        (await PartitionExistsAsync(oldPartitionB, ct)).ShouldBeFalse();

        // Default-partitionen ska aldrig droppas (skyddad av regex-filtret
        // ^audit_log_[0-9]{8}$ som inte matchar audit_log_default)
        (await PartitionExistsAsync("audit_log_default", ct)).ShouldBeTrue(
            "default-partitionen får aldrig droppas av retention-jobbet");

        // Bootstrap-partitions kring real-now ska inte heller droppas (alla nyare än 2000-01-01)
        // Vi stickprovar morgondagens namn
        var tomorrow = $"audit_log_{DateTime.UtcNow.AddDays(1):yyyyMMdd}";
        (await PartitionExistsAsync(tomorrow, ct)).ShouldBeTrue(
            "bootstrap-partitions kring real-now får inte droppas");
    }

    [Fact]
    public async Task RunAsync_EndToEnd_EnsuresNextDayAndDropsOld()
    {
        var ct = TestContext.Current.CancellationToken;

        // Setup: en fake mycket gammal partition (1900-tal) som ska droppas
        const string ancient = "audit_log_19000101";
        await CreatePartitionIfMissingAsync(ancient, "1900-01-01", "1900-01-02", ct);

        // Setup: säkerställ att morgondagens-partition (relativt en far-future "now")
        // inte finns i förväg
        var now = new DateTimeOffset(2030, 3, 15, 12, 0, 0, TimeSpan.Zero);
        const string expectedNewPartition = "audit_log_20300316";
        await DropPartitionIfExistsAsync(expectedNewPartition, ct);

        using var scope = _fixture.Services.CreateScope();
        var maintainer = scope.ServiceProvider.GetRequiredService<IAuditPartitionMaintainer>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<AuditLogRetentionJob>();
        var job = new AuditLogRetentionJob(maintainer, new FixedClock(now), logger);

        await job.RunAsync(ct);

        // Båda effekterna har skett:
        (await PartitionExistsAsync(expectedNewPartition, ct)).ShouldBeTrue(
            "RunAsync ska ha skapat morgondagens partition (relativt fixed clock)");
        (await PartitionExistsAsync(ancient, ct)).ShouldBeFalse(
            "RunAsync ska ha droppat partition äldre än 90 dagar bakåt från fixed clock");

        // Cleanup
        await DropPartitionIfExistsAsync(expectedNewPartition, ct);
    }

    // ========== Helpers ==========

    private async Task<bool> PartitionExistsAsync(string partitionName, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Database
            .SqlQueryRaw<int>(
                "SELECT 1 AS \"Value\" FROM pg_class WHERE relname = {0}",
                partitionName)
            .ToListAsync(ct);

        return rows.Count > 0;
    }

    private async Task DropPartitionIfExistsAsync(string partitionName, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Identifierare kan inte parameteriseras — partitionName från test-konstanter,
        // ingen user input.
#pragma warning disable EF1002 // Interpolated string in SQL — verifierat säkert (test-konstanter, ingen user input)
        await db.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS {partitionName};",
            ct);
#pragma warning restore EF1002
    }

    private async Task CreatePartitionIfMissingAsync(
        string partitionName, string fromDate, string toDate, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Identifierare och datum-bounds från test-konstanter (inga user inputs).
#pragma warning disable EF1002 // Interpolated string in SQL — verifierat säkert
#pragma warning disable EF1003 // Concatenated string in SQL — verifierat säkert
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS {partitionName} PARTITION OF audit_log " +
            $"FOR VALUES FROM ('{fromDate} 00:00:00+00') TO ('{toDate} 00:00:00+00');",
            ct);
#pragma warning restore EF1003
#pragma warning restore EF1002
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
