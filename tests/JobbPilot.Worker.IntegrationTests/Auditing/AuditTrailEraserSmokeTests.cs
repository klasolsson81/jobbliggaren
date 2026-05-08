using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Auditing;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Worker.IntegrationTests.Auditing;

/// <summary>
/// End-to-end smoke-test för <see cref="IAuditTrailEraser"/> mot riktig
/// Postgres (Testcontainers). Verifierar GDPR Art. 17-anonymiseringspolicyn
/// per ADR 0022 + ADR 0024 D3.
///
/// Märkt <c>[Trait("Category", "SmokeTest")]</c> — körs INTE i default
/// <c>dotnet test</c>. Kör explicit: <c>dotnet test --filter "Category=SmokeTest"</c>.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class AuditTrailEraserSmokeTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    [Fact]
    public async Task AnonymizeUserAuditTrail_SetsPiiToNull_PreservesAccountabilityFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        await SeedAuditEntryAsync(
            userId, aggregateId, correlationId, occurredAt,
            eventType: "Application.Created",
            aggregateType: "Application",
            ipAddress: "192.0.2.0",
            userAgent: "TestAgent/1.0",
            ct);

        // Akt
        int affected;
        using (var scope = _fixture.Services.CreateScope())
        {
            var eraser = scope.ServiceProvider.GetRequiredService<IAuditTrailEraser>();
            affected = await eraser.AnonymizeUserAuditTrailAsync(userId, ct);
        }
        affected.ShouldBe(1);

        // Verifiera: PII NULL, accountability bevarat
        var entry = await ReadByAggregateIdAsync(aggregateId, ct);
        entry.ShouldNotBeNull();
        entry.UserId.ShouldBeNull("user_id ska anonymiseras");
        entry.IpAddress.ShouldBeNull("ip_address ska anonymiseras");
        entry.UserAgent.ShouldBeNull("user_agent ska anonymiseras");
        entry.CorrelationId.ShouldBe(correlationId, "correlation_id bevaras 90 dagar för accountability");
        entry.EventType.ShouldBe("Application.Created", "event_type bevaras");
        entry.AggregateType.ShouldBe("Application", "aggregate_type bevaras");
        entry.AggregateId.ShouldBe(aggregateId, "aggregate_id bevaras");
    }

    [Fact]
    public async Task AnonymizeUserAuditTrail_OnlyAffectsMatchingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var aggregateA = Guid.NewGuid();
        var aggregateB = Guid.NewGuid();

        await SeedAuditEntryAsync(userA, aggregateA, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "Test.A", "Test", "10.0.0.1", "AgentA", ct);
        await SeedAuditEntryAsync(userB, aggregateB, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "Test.B", "Test", "10.0.0.2", "AgentB", ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var eraser = scope.ServiceProvider.GetRequiredService<IAuditTrailEraser>();
            var affected = await eraser.AnonymizeUserAuditTrailAsync(userA, ct);
            affected.ShouldBe(1, "endast användare A:s rader ska anonymiseras");
        }

        var entryA = await ReadByAggregateIdAsync(aggregateA, ct);
        var entryB = await ReadByAggregateIdAsync(aggregateB, ct);

        entryA.ShouldNotBeNull();
        entryA.UserId.ShouldBeNull();

        entryB.ShouldNotBeNull();
        entryB.UserId.ShouldBe(userB, "userB ska INTE påverkas");
        entryB.IpAddress.ShouldBe("10.0.0.2");
        entryB.UserAgent.ShouldBe("AgentB");
    }

    [Fact]
    public async Task AnonymizeUserAuditTrail_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();

        await SeedAuditEntryAsync(userId, aggregateId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "Test.Idem", "Test", "10.0.0.99", "Agent", ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var eraser = scope.ServiceProvider.GetRequiredService<IAuditTrailEraser>();

            var first = await eraser.AnonymizeUserAuditTrailAsync(userId, ct);
            var second = await eraser.AnonymizeUserAuditTrailAsync(userId, ct);

            first.ShouldBe(1);
            second.ShouldBe(0, "andra körningen ska inte hitta rader med matching user_id (redan NULL)");
        }

        var entry = await ReadByAggregateIdAsync(aggregateId, ct);
        entry.ShouldNotBeNull();
        entry.UserId.ShouldBeNull();
    }

    // ─── Helpers ───

    private async Task SeedAuditEntryAsync(
        Guid userId, Guid aggregateId, Guid correlationId, DateTimeOffset occurredAt,
        string eventType, string aggregateType, string? ipAddress, string? userAgent,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = AuditLogEntry.Create(
            occurredAt, correlationId, userId,
            eventType, aggregateType, aggregateId,
            ipAddress, userAgent);

        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuditLogEntry?> ReadByAggregateIdAsync(Guid aggregateId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AggregateId == aggregateId, ct);
    }
}
