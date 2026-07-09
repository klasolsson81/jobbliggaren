using Jobbliggaren.Application.Applications.Commands.BatchTransition;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

/// <summary>
/// #630 PR 9 — all-or-nothing two-phase bulk transition (CTO bind 2026-07-09).
/// Phase 1 resolves every id owner-scoped and throws before ANY mutation on a
/// missing/foreign id; phase 2 loops Application.TransitionTo, so the ADR 0092
/// D3 invariants and the D4 timeline apply per item exactly as on the single
/// path.
/// </summary>
public class BatchTransitionApplicationsCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccessLogger = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public BatchTransitionApplicationsCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, List<DomainApplication> applications)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        int applicationCount)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var applications = new List<DomainApplication>(applicationCount);
        for (var i = 0; i < applicationCount; i++)
        {
            var app = DomainApplication.Create(
                seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
            db.Applications.Add(app);
            applications.Add(app);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, applications);
    }

    private BatchTransitionApplicationsCommandHandler CreateHandler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _failedAccessLogger);

    private static BatchTransitionApplicationsCommand Command(
        params (DomainApplication App, string Target)[] items) =>
        new([.. items.Select(i => new BatchTransitionItem(i.App.Id.Value, i.Target))]);

    // ---------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_MultipleOwnApplications_TransitionsAll()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 3);
        var handler = CreateHandler(db);

        var result = await handler.Handle(
            Command((apps[0], "Submitted"), (apps[1], "Submitted"), (apps[2], "Submitted")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        foreach (var app in apps)
        {
            var updated = await db.Applications.FindAsync(
                [app.Id], TestContext.Current.CancellationToken);
            updated!.Status.ShouldBe(ApplicationStatus.Submitted);
        }
    }

    [Fact]
    public async Task Handle_PerItemTargets_TransitionsEachToItsOwnTarget()
    {
        // The group-undo shape (CTO bind Q7): every item carries its OWN
        // target, so one batch call restores mixed previous statuses.
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 2);
        var handler = CreateHandler(db);

        var result = await handler.Handle(
            Command((apps[0], "Submitted"), (apps[1], "Rejected")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (await db.Applications.FindAsync([apps[0].Id], TestContext.Current.CancellationToken))!
            .Status.ShouldBe(ApplicationStatus.Submitted);
        (await db.Applications.FindAsync([apps[1].Id], TestContext.Current.CancellationToken))!
            .Status.ShouldBe(ApplicationStatus.Rejected);
    }

    [Fact]
    public async Task Handle_MultipleOwnApplications_AppendsOneStatusChangePerApplication()
    {
        // ADR 0092 D4: the timeline row is appended inside TransitionTo per
        // item — the batch inherits it without any extra plumbing.
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 2);
        var handler = CreateHandler(db);

        await handler.Handle(
            Command((apps[0], "Submitted"), (apps[1], "Submitted")),
            CancellationToken.None);

        foreach (var app in apps)
        {
            var updated = await db.Applications.FindAsync(
                [app.Id], TestContext.Current.CancellationToken);
            var change = updated!.StatusChanges.ShouldHaveSingleItem();
            change.From.ShouldBe(ApplicationStatus.Draft);
            change.To.ShouldBe(ApplicationStatus.Submitted);
            change.ChangedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
        }
    }

    [Fact]
    public async Task Handle_ToSubmitted_StampsAppliedAtPerApplication()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 2);
        var handler = CreateHandler(db);

        await handler.Handle(
            Command((apps[0], "Submitted"), (apps[1], "Submitted")),
            CancellationToken.None);

        foreach (var app in apps)
        {
            var updated = await db.Applications.FindAsync(
                [app.Id], TestContext.Current.CancellationToken);
            updated!.AppliedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
        }
    }

    [Fact]
    public async Task Handle_DuplicateIdenticalItems_TransitionsOnce()
    {
        // CTO bind Q6: identical (id, target) duplicates are silently deduped
        // — a resent double-click yields ONE transition and ONE timeline row.
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 1);
        var handler = CreateHandler(db);

        var result = await handler.Handle(
            Command((apps[0], "Submitted"), (apps[0], "Submitted")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var updated = await db.Applications.FindAsync(
            [apps[0].Id], TestContext.Current.CancellationToken);
        updated!.Status.ShouldBe(ApplicationStatus.Submitted);
        updated.StatusChanges.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ItemAlreadyAtTargetStatus_NoOpsWithoutTimelineRow()
    {
        // Self-transition parity with the single path: a no-op success without
        // a From==To timeline row, while the rest of the batch transitions.
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 2);
        var handler = CreateHandler(db);

        var result = await handler.Handle(
            Command((apps[0], "Draft"), (apps[1], "Submitted")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var noOp = await db.Applications.FindAsync(
            [apps[0].Id], TestContext.Current.CancellationToken);
        noOp!.Status.ShouldBe(ApplicationStatus.Draft);
        noOp.StatusChanges.ShouldBeEmpty();

        var transitioned = await db.Applications.FindAsync(
            [apps[1].Id], TestContext.Current.CancellationToken);
        transitioned!.Status.ShouldBe(ApplicationStatus.Submitted);
        transitioned.StatusChanges.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new BatchTransitionApplicationsCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, _failedAccessLogger);

        var command = new BatchTransitionApplicationsCommand(
            [new BatchTransitionItem(Guid.NewGuid(), "Submitted")]);

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    // ---------------------------------------------------------------
    // All-or-nothing: missing/foreign ids abort BEFORE any mutation
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenAnyIdUnknown_ThrowsNotFoundWithoutMutatingAnyAggregate()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, apps) = await SeedAsync(db, _userId, 2);
        var handler = CreateHandler(db);

        var command = new BatchTransitionApplicationsCommand(
        [
            new BatchTransitionItem(apps[0].Id.Value, "Submitted"),
            new BatchTransitionItem(apps[1].Id.Value, "Submitted"),
            new BatchTransitionItem(Guid.NewGuid(), "Submitted"),
        ]);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        // Phase 1 threw before phase 2 — the caller's own aggregates in the
        // same batch are untouched (all-or-nothing).
        foreach (var app in apps)
        {
            var untouched = await db.Applications.FindAsync(
                [app.Id], TestContext.Current.CancellationToken);
            untouched!.Status.ShouldBe(ApplicationStatus.Draft);
            untouched.StatusChanges.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Handle_WhenAnyIdBelongsToOtherUser_LogsCrossUserAttemptAndThrowsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, foreignApps) = await SeedAsync(db, Guid.NewGuid(), 1);
        var (_, ownApps) = await SeedAsync(db, _userId, 1);
        var handler = CreateHandler(db);

        var command = new BatchTransitionApplicationsCommand(
        [
            new BatchTransitionItem(ownApps[0].Id.Value, "Submitted"),
            new BatchTransitionItem(foreignApps[0].Id.Value, "Submitted"),
        ]);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        _failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application",
            foreignApps[0].Id.Value,
            _userId,
            "BatchTransitionApplications");

        // The victim's aggregate AND the caller's own aggregate are untouched.
        (await db.Applications.FindAsync([foreignApps[0].Id], TestContext.Current.CancellationToken))!
            .Status.ShouldBe(ApplicationStatus.Draft);
        (await db.Applications.FindAsync([ownApps[0].Id], TestContext.Current.CancellationToken))!
            .Status.ShouldBe(ApplicationStatus.Draft);
    }

    [Fact]
    public async Task Handle_MixedUnknownAndForeignIds_LogsOnlyForeignIds()
    {
        // Probe discrimination parity with the single path: an unknown id is
        // NOT a cross-user attempt and must not pollute the ops signal; the
        // response is one uniform 404 either way (no enumeration oracle).
        var db = TestAppDbContextFactory.Create();
        var (_, foreignApps) = await SeedAsync(db, Guid.NewGuid(), 1);
        var ownSeeker = JobSeeker.Register(_userId, "Current User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);
        var handler = CreateHandler(db);
        var unknownId = Guid.NewGuid();

        var command = new BatchTransitionApplicationsCommand(
        [
            new BatchTransitionItem(foreignApps[0].Id.Value, "Submitted"),
            new BatchTransitionItem(unknownId, "Submitted"),
        ]);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        _failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application", foreignApps[0].Id.Value, _userId, "BatchTransitionApplications");
        _failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), unknownId, Arg.Any<Guid>(), Arg.Any<string>());
    }
}
