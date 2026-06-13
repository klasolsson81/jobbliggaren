using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auditing;

/// <summary>
/// TD-73 prod-gating — SystemEventAuditor (ADR 0035).
/// Verifierar:
/// <list type="bullet">
/// <item>Payload-serialisering körs på runtime-typ (concrete record, inte abstract)</item>
/// <item>Audit-rad skapas via CreateSystemEvent och persisteras</item>
/// <item>Idempotens vid retry: andra anrop med samma (EventType, AggregateId) skip:as</item>
/// </list>
///
/// Använder InMemory EF-provider via TestAppDbContextFactory eftersom Postgres-
/// specifik partitionering inte spelar roll för dessa unit-tester.
/// </summary>
public class SystemEventAuditorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static SystemEventAuditor CreateAuditor(IAppDbContext db)
    {
        var corr = new StubCorrelationIdProvider(Guid.NewGuid());
        return new SystemEventAuditor(db, corr, NullLogger<SystemEventAuditor>.Instance);
    }

    [Fact]
    public async Task RecordAsync_WritesAuditRowWithSerializedPayload()
    {
        var db = Common.TestAppDbContextFactory.Create();
        var auditor = CreateAuditor(db);
        var evt = new JobAdsSynced(
            AggregateId: Guid.NewGuid(),
            OccurredAt: Now,
            Source: "platsbanken",
            JobType: "stream",
            Fetched: 565,
            Added: 417,
            Updated: 0,
            Archived: 0,
            Skipped: 148,
            Errors: 0,
            StartedAt: Now,
            CompletedAt: Now.AddSeconds(42));

        await auditor.RecordAsync(evt, TestContext.Current.CancellationToken);

        var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
        entry.EventType.ShouldBe("System.JobAdsSynced");
        entry.AggregateType.ShouldBe("System.JobAdSync");
        entry.AggregateId.ShouldBe(evt.AggregateId);
        entry.UserId.ShouldBeNull();
        entry.IpAddress.ShouldBeNull();
        entry.UserAgent.ShouldBeNull();
        entry.Payload.ShouldNotBeNullOrEmpty();

        // Payload-shape — verifiera runtime-type-dispatch
        using var doc = JsonDocument.Parse(entry.Payload!);
        doc.RootElement.GetProperty("Source").GetString().ShouldBe("platsbanken");
        doc.RootElement.GetProperty("Fetched").GetInt32().ShouldBe(565);
        doc.RootElement.GetProperty("Added").GetInt32().ShouldBe(417);
    }

    [Fact]
    public async Task RecordAsync_RawPayloadPurged_SerializesAllFields()
    {
        var db = Common.TestAppDbContextFactory.Create();
        var auditor = CreateAuditor(db);
        var cutoff = Now.AddDays(-30);
        var evt = new RawPayloadPurged(
            AggregateId: Guid.NewGuid(),
            OccurredAt: Now,
            RowsAffected: 42,
            Cutoff: cutoff,
            RetentionDays: 30);

        await auditor.RecordAsync(evt, TestContext.Current.CancellationToken);

        var entry = await db.AuditLogEntries.SingleAsync(TestContext.Current.CancellationToken);
        entry.EventType.ShouldBe("System.RawPayloadPurged");
        entry.AggregateType.ShouldBe("System.RawPayloadPurge");
        entry.AggregateId.ShouldBe(evt.AggregateId);

        using var doc = JsonDocument.Parse(entry.Payload!);
        doc.RootElement.GetProperty("RowsAffected").GetInt32().ShouldBe(42);
        doc.RootElement.GetProperty("RetentionDays").GetInt32().ShouldBe(30);
    }

    [Fact]
    public async Task RecordAsync_IsIdempotent_OnSameAggregateIdAndEventType()
    {
        // Hangfire-retry-scenario: andra anrop med samma (EventType, AggregateId)
        // ska skip:as — inga duplicerade audit-rader.
        var db = Common.TestAppDbContextFactory.Create();
        var auditor = CreateAuditor(db);
        var aggregateId = Guid.NewGuid();
        var evt = new JobAdsSynced(
            AggregateId: aggregateId,
            OccurredAt: Now,
            Source: "platsbanken",
            JobType: "stream",
            Fetched: 0, Added: 0, Updated: 0, Archived: 0, Skipped: 0, Errors: 0,
            StartedAt: Now,
            CompletedAt: Now);

        await auditor.RecordAsync(evt, TestContext.Current.CancellationToken);
        await auditor.RecordAsync(evt, TestContext.Current.CancellationToken);

        var rows = await db.AuditLogEntries.CountAsync(TestContext.Current.CancellationToken);
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task RecordAsync_DifferentAggregateId_WritesSecondRow()
    {
        // Två sync-runs (olika runId) ska ge två rader.
        var db = Common.TestAppDbContextFactory.Create();
        var auditor = CreateAuditor(db);
        var evt1 = new JobAdsSynced(
            AggregateId: Guid.NewGuid(),
            OccurredAt: Now,
            Source: "platsbanken", JobType: "stream",
            Fetched: 0, Added: 0, Updated: 0, Archived: 0, Skipped: 0, Errors: 0,
            StartedAt: Now, CompletedAt: Now);
        var evt2 = evt1 with { AggregateId = Guid.NewGuid() };

        await auditor.RecordAsync(evt1, TestContext.Current.CancellationToken);
        await auditor.RecordAsync(evt2, TestContext.Current.CancellationToken);

        var rows = await db.AuditLogEntries.CountAsync(TestContext.Current.CancellationToken);
        rows.ShouldBe(2);
    }

    private sealed class StubCorrelationIdProvider(Guid id) : ICorrelationIdProvider
    {
        public Guid Current { get; } = id;
    }
}
