using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Auditing;

/// <summary>
/// Tester för AuditBehavior per ADR 0022. Använder InMemory-DbContext för att
/// kunna verifiera att .Add(entry) körs (NSubstitute mot DbSet är onödigt
/// rörigt). Övriga ports mockas via NSubstitute.
/// </summary>
public class AuditBehaviorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AggregateGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Infrastructure.Persistence.AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICorrelationIdProvider _correlationIdProvider =
        Substitute.For<ICorrelationIdProvider>();
    private readonly IRequestContextProvider _requestContextProvider =
        Substitute.For<IRequestContextProvider>();

    public AuditBehaviorTests()
    {
        _currentUser.UserId.Returns(UserId);
        _clock.UtcNow.Returns(Now);
        _correlationIdProvider.Current.Returns(CorrelationId);
        _requestContextProvider.IpAddress.Returns("203.0.113.1");
        _requestContextProvider.UserAgent.Returns("Mozilla/5.0");
    }

    private AuditBehavior<TMessage, TResponse> CreateBehavior<TMessage, TResponse>()
        where TMessage : IMessage =>
        new(
            _db,
            _currentUser,
            _clock,
            _correlationIdProvider,
            _requestContextProvider);

    /// <summary>
    /// AuditBehavior anropar bara <c>db.AuditLogEntries.Add(entry)</c> utan
    /// SaveChanges (per ADR 0022 görs persistens av UnitOfWorkBehavior). För
    /// att verifiera Add-anropet utan att trigga SaveChanges läses entiteter
    /// direkt från ChangeTracker:n istället för via DbSet-query (InMemory-
    /// providerns query-engine ser bara persisterade entiteter).
    /// </summary>
    private List<AuditLogEntry> TrackedAuditEntries() =>
        _db.ChangeTracker
            .Entries<AuditLogEntry>()
            .Select(e => e.Entity)
            .ToList();

    // ---------------------------------------------------------------
    // Auditable commands — happy path
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCommandIsAuditableAndResponseIsResultSuccess_AddsAuditLogEntry()
    {
        var behavior = CreateBehavior<AuditableMutationCommand, Result>();
        var command = new AuditableMutationCommand(AggregateGuid);
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        var response = await behavior.Handle(command, next, CancellationToken.None);

        response.IsSuccess.ShouldBeTrue();
        var entries = TrackedAuditEntries();
        entries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenCommandIsAuditableAndSuccess_PopulatesAllFieldsCorrectly()
    {
        var behavior = CreateBehavior<AuditableMutationCommand, Result>();
        var command = new AuditableMutationCommand(AggregateGuid);
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        var entry = TrackedAuditEntries().Single();
        entry.EventType.ShouldBe("Test.Mutation");
        entry.AggregateType.ShouldBe("TestAggregate");
        entry.AggregateId.ShouldBe(AggregateGuid);
        entry.UserId.ShouldBe(UserId);
        entry.OccurredAt.ShouldBe(Now);
        entry.CorrelationId.ShouldBe(CorrelationId);
        entry.IpAddress.ShouldBe("203.0.113.1");
        entry.UserAgent.ShouldBe("Mozilla/5.0");
        entry.ImpersonatedBy.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ExtractsAggregateIdFromResponse_ForCreateCase()
    {
        // Create-fall: ID genereras i handler och returneras via Result<Guid>.Value.
        var behavior = CreateBehavior<AuditableCreateCommand, Result<Guid>>();
        var command = new AuditableCreateCommand();
        MessageHandlerDelegate<AuditableCreateCommand, Result<Guid>> next =
            (_, _) => ValueTask.FromResult(Result.Success(AggregateGuid));

        await behavior.Handle(command, next, CancellationToken.None);

        var entry = TrackedAuditEntries().Single();
        entry.AggregateId.ShouldBe(AggregateGuid);
        entry.EventType.ShouldBe("Test.Created");
    }

    [Fact]
    public async Task Handle_ExtractsAggregateIdFromCommandField_ForMutationCase()
    {
        // Mutation-fall: aggregate-ID kommer från command-fältet (Id finns redan).
        var behavior = CreateBehavior<AuditableMutationCommand, Result>();
        var command = new AuditableMutationCommand(AggregateGuid);
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        var entry = TrackedAuditEntries().Single();
        entry.AggregateId.ShouldBe(AggregateGuid);
    }

    // ---------------------------------------------------------------
    // Failure / skip-paths
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCommandIsAuditableButResponseIsFailure_DoesNotAddAuditLogEntry()
    {
        var behavior = CreateBehavior<AuditableMutationCommand, Result>();
        var command = new AuditableMutationCommand(AggregateGuid);
        var error = new DomainError("Test.Failed", "test-fel");
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Failure(error));

        var response = await behavior.Handle(command, next, CancellationToken.None);

        response.IsFailure.ShouldBeTrue();
        TrackedAuditEntries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenCommandIsAuditableButResponseIsResultTFailure_DoesNotAddAuditLogEntry()
    {
        var behavior = CreateBehavior<AuditableCreateCommand, Result<Guid>>();
        var command = new AuditableCreateCommand();
        var error = new DomainError("Test.Failed", "test-fel");
        MessageHandlerDelegate<AuditableCreateCommand, Result<Guid>> next =
            (_, _) => ValueTask.FromResult(Result.Failure<Guid>(error));

        await behavior.Handle(command, next, CancellationToken.None);

        TrackedAuditEntries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenCommandIsNotAuditable_DoesNotAddAuditLogEntry()
    {
        // Plain ICommand utan IAuditableCommand-marker — AuditBehavior skipparr.
        var behavior = CreateBehavior<NonAuditableCommand, Result>();
        var command = new NonAuditableCommand();
        MessageHandlerDelegate<NonAuditableCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        TrackedAuditEntries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AlwaysReturnsSameResponseAsNext()
    {
        // Behavior är pass-through: response från handler ska aldrig modifieras.
        var behavior = CreateBehavior<AuditableMutationCommand, Result>();
        var command = new AuditableMutationCommand(AggregateGuid);
        var expected = Result.Success();
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(expected);

        var actual = await behavior.Handle(command, next, CancellationToken.None);

        actual.ShouldBeSameAs(expected);
    }

    // ---------------------------------------------------------------
    // Batch marker (#630 PR 9) — one row per aggregate id
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCommandIsBatchAuditableAndSuccess_AddsOneEntryPerAggregateId()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var behavior = CreateBehavior<BatchAuditableMutationCommand, Result>();
        var command = new BatchAuditableMutationCommand(ids);
        MessageHandlerDelegate<BatchAuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        var entries = TrackedAuditEntries();
        entries.Count.ShouldBe(3);
        entries.Select(e => e.AggregateId).ShouldBe(ids, ignoreOrder: true);
    }

    [Fact]
    public async Task Handle_WhenCommandIsBatchAuditable_AllEntriesShareCorrelationIdAndInstant()
    {
        // The shared correlation id is what groups the rows as ONE batch, and
        // one command is one moment — every row carries the same OccurredAt.
        var behavior = CreateBehavior<BatchAuditableMutationCommand, Result>();
        var command = new BatchAuditableMutationCommand([Guid.NewGuid(), Guid.NewGuid()]);
        MessageHandlerDelegate<BatchAuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        var entries = TrackedAuditEntries();
        entries.ShouldAllBe(e => e.CorrelationId == CorrelationId);
        entries.ShouldAllBe(e => e.OccurredAt == Now);
        entries.ShouldAllBe(e => e.EventType == "Test.BatchMutation");
        entries.ShouldAllBe(e => e.UserId == UserId);
    }

    [Fact]
    public async Task Handle_WhenCommandIsBatchAuditableButResponseIsFailure_DoesNotAddAuditLogEntries()
    {
        var behavior = CreateBehavior<BatchAuditableMutationCommand, Result>();
        var command = new BatchAuditableMutationCommand([Guid.NewGuid()]);
        var error = new DomainError("Test.Failed", "test-fel");
        MessageHandlerDelegate<BatchAuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Failure(error));

        await behavior.Handle(command, next, CancellationToken.None);

        TrackedAuditEntries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenBatchCommandReturnsNoIds_AddsNoEntries()
    {
        var behavior = CreateBehavior<BatchAuditableMutationCommand, Result>();
        var command = new BatchAuditableMutationCommand([]);
        MessageHandlerDelegate<BatchAuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        TrackedAuditEntries().ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // System-jobb / Worker-fall (null user, null context)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCurrentUserHasNoUserId_WritesUserIdAsNull()
    {
        // System-jobb-fall: t.ex. HardDeleteAccountsJob från Worker — ingen inloggad user.
        var workerCurrentUser = Substitute.For<ICurrentUser>();
        workerCurrentUser.UserId.Returns((Guid?)null);

        var behavior = new AuditBehavior<AuditableMutationCommand, Result>(
            _db,
            workerCurrentUser,
            _clock,
            _correlationIdProvider,
            _requestContextProvider);

        var command = new AuditableMutationCommand(AggregateGuid);
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        var entry = TrackedAuditEntries().Single();
        entry.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenRequestContextProviderReturnsNullIp_AuditEntryHasNullIp()
    {
        var nullContext = Substitute.For<IRequestContextProvider>();
        nullContext.IpAddress.Returns((string?)null);
        nullContext.UserAgent.Returns((string?)null);

        var behavior = new AuditBehavior<AuditableMutationCommand, Result>(
            _db,
            _currentUser,
            _clock,
            _correlationIdProvider,
            nullContext);

        var command = new AuditableMutationCommand(AggregateGuid);
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        var entry = TrackedAuditEntries().Single();
        entry.IpAddress.ShouldBeNull();
        entry.UserAgent.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // SaveChanges-paritet — AuditBehavior anropar inte SaveChanges
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_DoesNotCallSaveChanges()
    {
        // Per ADR 0022 sker SaveChanges i UnitOfWorkBehavior:s post-action,
        // inte i AuditBehavior. Audit-entryt persisteras tillsammans med
        // handler-mutationen i samma transaction.
        // Använder InMemory-DbContext via TrackedAuditEntries-pattern och
        // verifierar att audit-rad finns i ChangeTracker (Added) men inte
        // i underlying store (skulle krävt SaveChanges).
        var behavior = CreateBehavior<AuditableMutationCommand, Result>();
        var command = new AuditableMutationCommand(AggregateGuid);
        MessageHandlerDelegate<AuditableMutationCommand, Result> next =
            (_, _) => ValueTask.FromResult(Result.Success());

        await behavior.Handle(command, next, CancellationToken.None);

        // Entityt är i Added-state — bevis att Add() kördes utan SaveChanges.
        var trackedStates = _db.ChangeTracker
            .Entries<AuditLogEntry>()
            .Select(e => e.State)
            .ToList();
        trackedStates.Count.ShouldBe(1);
        trackedStates[0].ShouldBe(EntityState.Added);
    }
}

// ---------------------------------------------------------------
// Test-doubles — fake commands för behavior-test
// ---------------------------------------------------------------

internal sealed record AuditableCreateCommand
    : ICommand<Result<Guid>>, IAuditableCommand<Result<Guid>>
{
    public string EventType => "Test.Created";
    public string AggregateType => "TestAggregate";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}

internal sealed record AuditableMutationCommand(Guid AggregateId)
    : ICommand<Result>, IAuditableCommand<Result>
{
    public string EventType => "Test.Mutation";
    public string AggregateType => "TestAggregate";
    public Guid ExtractAggregateId(Result response) => AggregateId;
}

internal sealed record NonAuditableCommand : ICommand<Result>;

internal sealed record BatchAuditableMutationCommand(IReadOnlyList<Guid> AggregateIds)
    : ICommand<Result>, IBatchAuditableCommand<Result>
{
    public string EventType => "Test.BatchMutation";
    public string AggregateType => "TestAggregate";
    public IReadOnlyList<Guid> ExtractAggregateIds(Result response) => AggregateIds;
}
