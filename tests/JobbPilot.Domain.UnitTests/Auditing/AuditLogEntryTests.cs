using JobbPilot.Domain.Auditing;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Auditing;

/// <summary>
/// Domain-tester för flat entity AuditLogEntry per ADR 0022.
/// Verifierar invarianter i Create-factoryn samt att ID är unikt per anrop.
/// </summary>
public class AuditLogEntryTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AggregateId = Guid.NewGuid();

    // ---------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidArgs_SetsAllProperties()
    {
        var entry = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: "127.0.0.1",
            userAgent: "Mozilla/5.0",
            impersonatedBy: null);

        entry.OccurredAt.ShouldBe(OccurredAt);
        entry.CorrelationId.ShouldBe(CorrelationId);
        entry.UserId.ShouldBe(UserId);
        entry.EventType.ShouldBe("Application.Created");
        entry.AggregateType.ShouldBe("Application");
        entry.AggregateId.ShouldBe(AggregateId);
        entry.IpAddress.ShouldBe("127.0.0.1");
        entry.UserAgent.ShouldBe("Mozilla/5.0");
        entry.ImpersonatedBy.ShouldBeNull();
        entry.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WithImpersonatedBy_SetsImpersonatedByValue()
    {
        var impersonator = Guid.NewGuid();

        var entry = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null,
            impersonatedBy: impersonator);

        entry.ImpersonatedBy.ShouldBe(impersonator);
    }

    // ---------------------------------------------------------------
    // Invariant-validering
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEventTypeIsNullOrWhitespace_Throws(string? eventType)
    {
        var act = () => AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: eventType!,
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenAggregateTypeIsNullOrWhitespace_Throws(string? aggregateType)
    {
        var act = () => AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: aggregateType!,
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Create_WhenAggregateIdIsEmpty_Throws()
    {
        var act = () => AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: Guid.Empty,
            ipAddress: null,
            userAgent: null);

        var ex = act.ShouldThrow<ArgumentException>();
        ex.ParamName.ShouldBe("aggregateId");
    }

    // ---------------------------------------------------------------
    // Nullable-fält tillåts
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithNullUserId_AllowsNull()
    {
        // System-jobb-fall: Worker-jobb (t.ex. MarkGhosted) har ingen inloggad user.
        var entry = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: null,
            eventType: "Application.MarkedGhosted",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        entry.UserId.ShouldBeNull();
    }

    [Fact]
    public void Create_WithNullIpAndUserAgent_AllowsNull()
    {
        // Worker-fall (no HTTP-context): IP och User-Agent saknas.
        var entry = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        entry.IpAddress.ShouldBeNull();
        entry.UserAgent.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // ID-uniqueness
    // ---------------------------------------------------------------

    [Fact]
    public void Create_GeneratesNewId_PerCall()
    {
        var first = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        var second = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        first.Id.ShouldNotBe(second.Id);
        first.Id.Value.ShouldNotBe(Guid.Empty);
        second.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void AuditLogEntryId_New_GeneratesNonEmptyGuid()
    {
        var id = AuditLogEntryId.New();

        id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void AuditLogEntryId_TwoNewIds_AreDistinct()
    {
        var a = AuditLogEntryId.New();
        var b = AuditLogEntryId.New();

        a.ShouldNotBe(b);
    }

    // ---------------------------------------------------------------
    // ADR 0035 — CreateSystemEvent-factory
    // ---------------------------------------------------------------

    [Fact]
    public void CreateSystemEvent_WithValidArgs_SetsAllSystemDefaults()
    {
        var entry = AuditLogEntry.CreateSystemEvent(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            eventType: "System.JobAdsSynced",
            aggregateType: "System.JobAdSync",
            aggregateId: AggregateId,
            payload: "{\"fetched\":42}");

        entry.OccurredAt.ShouldBe(OccurredAt);
        entry.CorrelationId.ShouldBe(CorrelationId);
        entry.EventType.ShouldBe("System.JobAdsSynced");
        entry.AggregateType.ShouldBe("System.JobAdSync");
        entry.AggregateId.ShouldBe(AggregateId);
        entry.Payload.ShouldBe("{\"fetched\":42}");
        // System har ingen request-context per design — alla user-fält är null.
        entry.UserId.ShouldBeNull();
        entry.IpAddress.ShouldBeNull();
        entry.UserAgent.ShouldBeNull();
        entry.ImpersonatedBy.ShouldBeNull();
    }

    [Fact]
    public void CreateSystemEvent_WithNullPayload_AllowsNull()
    {
        var entry = AuditLogEntry.CreateSystemEvent(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            eventType: "System.JobAdsSynced",
            aggregateType: "System.JobAdSync",
            aggregateId: AggregateId,
            payload: null);

        entry.Payload.ShouldBeNull();
    }

    [Fact]
    public void CreateSystemEvent_WhenAggregateIdIsEmpty_ThrowsBevarsInvariant()
    {
        var act = () => AuditLogEntry.CreateSystemEvent(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            eventType: "System.JobAdsSynced",
            aggregateType: "System.JobAdSync",
            aggregateId: Guid.Empty,
            payload: null);

        var ex = act.ShouldThrow<ArgumentException>();
        ex.ParamName.ShouldBe("aggregateId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSystemEvent_WhenEventTypeIsNullOrWhitespace_Throws(string? eventType)
    {
        var act = () => AuditLogEntry.CreateSystemEvent(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            eventType: eventType!,
            aggregateType: "System.JobAdSync",
            aggregateId: AggregateId,
            payload: null);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Create_CommandAudit_LeavesPayloadNullForFas2()
    {
        // ADR 0022: command-audit-payload är reserverat för Fas 4 (PII-saner-krav).
        // Create-factoryn ska därför inte sätta Payload — bara CreateSystemEvent gör det.
        var entry = AuditLogEntry.Create(
            occurredAt: OccurredAt,
            correlationId: CorrelationId,
            userId: UserId,
            eventType: "Application.Created",
            aggregateType: "Application",
            aggregateId: AggregateId,
            ipAddress: null,
            userAgent: null);

        entry.Payload.ShouldBeNull();
    }
}
