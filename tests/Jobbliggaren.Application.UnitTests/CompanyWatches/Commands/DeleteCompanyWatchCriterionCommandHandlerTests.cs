using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Commands.DeleteCompanyWatchCriterion;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// #560 PR-3 — HARD delete (C-D8/G1 verdict, the #782 template). The load-bearing assertion is
/// PHYSICAL absence: the row must be GONE, not hidden. A handler that stamped a row instead of
/// removing it would leave the user's whole job-hunt predicate resident forever (Art. 5(1)(e), the
/// exact failure the verdict closes) — and this read has nothing to hide behind, because the
/// aggregate has no lifecycle state left. The soft-delete apparatus this assertion once had to
/// switch off with <c>IgnoreQueryFilters()</c> was demolished with the <c>deleted_at</c> column.
/// </summary>
public class DeleteCompanyWatchCriterionCommandHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));

    private static DeleteCompanyWatchCriterionCommandHandler HandlerFor(
        AppDbContext db, Guid? userId, IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new DeleteCompanyWatchCriterionCommandHandler(
            db, currentUser, failedAccess ?? Substitute.For<IFailedAccessLogger>());
    }

    [Fact]
    public async Task Handle_OwnCriterion_RemovesTheRowPhysically()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        var result = await HandlerFor(db, Owner).Handle(
            new DeleteCompanyWatchCriterionCommand(criterion.Id.Value), ct);
        await db.SaveChangesAsync(ct);

        result.IsSuccess.ShouldBeTrue();

        // THE hard-delete oracle: the row is GONE, not hidden. This read has no filter to see past
        // — the aggregate has no lifecycle state left to hide behind (C-D8/G1; the vestigial
        // filter this assertion once had to switch off with IgnoreQueryFilters was demolished with
        // the deleted_at column). A handler that stamped a row instead of removing it fails here.
        (await db.CompanyWatchCriteria.AnyAsync(ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_RepeatDelete_IsNotFound()
    {
        // The row is GONE, so a second delete cannot no-op on it (the #782 semantics — unlike the
        // idempotent soft-delete siblings whose rows persist to no-op on).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        (await HandlerFor(db, Owner).Handle(
            new DeleteCompanyWatchCriterionCommand(criterion.Id.Value), ct)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var second = await HandlerFor(db, Owner).Handle(
            new DeleteCompanyWatchCriterionCommand(criterion.Id.Value), ct);

        second.IsFailure.ShouldBeTrue();
        second.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_IsTheIdenticalNotFound_LogsTheProbe_AndDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var theirs = await SeedAsync(db, Stranger, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var crossUser = await HandlerFor(db, Owner, failedAccess).Handle(
            new DeleteCompanyWatchCriterionCommand(theirs.Id.Value), ct);

        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, "DeleteCompanyWatchCriterion");

        var unknown = await HandlerFor(db, Owner).Handle(
            new DeleteCompanyWatchCriterionCommand(Guid.NewGuid()), ct);

        crossUser.IsFailure.ShouldBeTrue();
        unknown.IsFailure.ShouldBeTrue();
        crossUser.Error.Code.ShouldBe(unknown.Error.Code);

        // The stranger's criterion survives a cross-user delete attempt.
        (await db.CompanyWatchCriteria.AnyAsync(c => c.UserId == Stranger, ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_FailsClosed_AndDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, ct);

        var result = await HandlerFor(db, userId: null).Handle(
            new DeleteCompanyWatchCriterionCommand(criterion.Id.Value), ct);

        result.IsFailure.ShouldBeTrue();
        (await db.CompanyWatchCriteria.AnyAsync(ct)).ShouldBeTrue();
    }

    private static async Task<CompanyWatchCriterion> SeedAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(["62100"], ["0180"]).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
