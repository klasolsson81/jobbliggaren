using Jobbliggaren.Application.Admin.Queries.GetAuditLogEntries;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Auditing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Queries;

/// <summary>
/// Filter-permutationer + paginering för admin-audit-vyn. Integration-tester
/// täcker HTTP-pipeline; dessa unit-tester verifierar handler-logiken isolerat
/// (snabbare, dokumenterar invarianter). CTO 2026-05-11 M4.
/// </summary>
public class GetAuditLogEntriesQueryHandlerTests
{
    private static async Task SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        params (DateTimeOffset OccurredAt, string EventType, string AggregateType, Guid? UserId)[] entries)
    {
        foreach (var (occurredAt, eventType, aggregateType, userId) in entries)
        {
            var entry = AuditLogEntry.Create(
                occurredAt: occurredAt,
                correlationId: Guid.NewGuid(),
                userId: userId,
                eventType: eventType,
                aggregateType: aggregateType,
                aggregateId: Guid.NewGuid(),
                ipAddress: null,
                userAgent: null);
            db.AuditLogEntries.Add(entry);
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static DateTimeOffset Day(int day) =>
        new(2026, 5, day, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_NoFilter_ReturnsAllEntriesOrderedByOccurredAtDesc()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db,
            (Day(1), "Application.Created", "Application", Guid.NewGuid()),
            (Day(5), "Resume.Created", "Resume", Guid.NewGuid()),
            (Day(3), "Application.NoteAdded", "Application", Guid.NewGuid()));

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var result = await handler.Handle(
            new GetAuditLogEntriesQuery(Page: 1, PageSize: 50),
            CancellationToken.None);

        result.TotalCount.ShouldBe(3);
        result.Items.Count.ShouldBe(3);
        result.Items[0].OccurredAt.ShouldBe(Day(5));
        result.Items[1].OccurredAt.ShouldBe(Day(3));
        result.Items[2].OccurredAt.ShouldBe(Day(1));
    }

    [Fact]
    public async Task Handle_EventTypeFilter_OnlyReturnsMatchingEntries()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db,
            (Day(1), "Application.Created", "Application", null),
            (Day(2), "Resume.Created", "Resume", null),
            (Day(3), "Application.Created", "Application", null));

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var result = await handler.Handle(
            new GetAuditLogEntriesQuery(EventType: "Application.Created"),
            CancellationToken.None);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.EventType == "Application.Created");
    }

    [Fact]
    public async Task Handle_WhitespaceEventTypeFilter_TreatedAsNoFilter()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db,
            (Day(1), "Application.Created", "Application", null),
            (Day(2), "Resume.Created", "Resume", null));

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var result = await handler.Handle(
            new GetAuditLogEntriesQuery(EventType: "   "),
            CancellationToken.None);

        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_UserIdFilter_OnlyReturnsEntriesForThatUser()
    {
        var db = TestAppDbContextFactory.Create();
        var keepUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedAsync(db,
            (Day(1), "Application.Created", "Application", keepUserId),
            (Day(2), "Application.Created", "Application", otherUserId),
            (Day(3), "Resume.Created", "Resume", keepUserId));

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var result = await handler.Handle(
            new GetAuditLogEntriesQuery(UserId: keepUserId),
            CancellationToken.None);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.UserId == keepUserId);
    }

    [Fact]
    public async Task Handle_DateRangeFilter_RespectsFromAndToInclusiveBoundaries()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db,
            (Day(1), "X", "X", null),
            (Day(5), "X", "X", null),
            (Day(10), "X", "X", null));

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var result = await handler.Handle(
            new GetAuditLogEntriesQuery(From: Day(3), To: Day(8)),
            CancellationToken.None);

        result.TotalCount.ShouldBe(1);
        result.Items[0].OccurredAt.ShouldBe(Day(5));
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPageWithTotalCount()
    {
        var db = TestAppDbContextFactory.Create();
        var seedEntries = Enumerable.Range(1, 15)
            .Select(i => (Day(i), "X", "X", (Guid?)null))
            .ToArray();
        await SeedAsync(db, seedEntries);

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var page2 = await handler.Handle(
            new GetAuditLogEntriesQuery(Page: 2, PageSize: 10),
            CancellationToken.None);

        page2.TotalCount.ShouldBe(15);
        page2.Items.Count.ShouldBe(5); // sista 5 av 15
        page2.Page.ShouldBe(2);
        page2.PageSize.ShouldBe(10);
        page2.TotalPages.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_AggregateTypeFilter_OnlyReturnsMatchingEntries()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db,
            (Day(1), "Application.Created", "Application", null),
            (Day(2), "Resume.Created", "Resume", null),
            (Day(3), "Application.NoteAdded", "Application", null));

        var handler = new GetAuditLogEntriesQueryHandler(db);
        var result = await handler.Handle(
            new GetAuditLogEntriesQuery(AggregateType: "Application"),
            CancellationToken.None);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.AggregateType == "Application");
    }

    [Fact]
    public async Task Handle_NoEntries_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new GetAuditLogEntriesQueryHandler(db);

        var result = await handler.Handle(new GetAuditLogEntriesQuery(), CancellationToken.None);

        result.TotalCount.ShouldBe(0);
        result.Items.ShouldBeEmpty();
        result.TotalPages.ShouldBe(0);
    }
}
