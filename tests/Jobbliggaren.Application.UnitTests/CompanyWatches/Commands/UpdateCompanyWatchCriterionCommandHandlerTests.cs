using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Application.CompanyWatches.Commands.UpdateCompanyWatchCriterion;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// #560 PR-3 — PATCH-partial update: present member changes, absent member untouched,
/// present-but-blank label CLEARS (the three-state Label contract), IDOR posture (C-D10).
/// </summary>
public class UpdateCompanyWatchCriterionCommandHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));

    private static UpdateCompanyWatchCriterionCommandHandler HandlerFor(
        AppDbContext db, Guid? userId, IFailedAccessLogger? failedAccess = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new UpdateCompanyWatchCriterionCommandHandler(
            db, currentUser, Clock, failedAccess ?? Substitute.For<IFailedAccessLogger>());
    }

    [Fact]
    public async Task Handle_PresentCriteria_ReplacesTheWholePredicate_AndLeavesTheLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, label: "IT i Stockholm", ct);

        var result = await HandlerFor(db, Owner).Handle(
            new UpdateCompanyWatchCriterionCommand(
                criterion.Id.Value,
                Label: null,
                new CompanyWatchCriteriaInput(["62201"], ["1480"])),
            ct);
        await db.SaveChangesAsync(ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = await db.CompanyWatchCriteria.SingleAsync(ct);
        stored.Criteria.SniCodes.ShouldBe(["62201"]);
        stored.Criteria.MunicipalityCodes.ShouldBe(["1480"]);
        // Absent (null) Label = untouched — the PATCH three-state contract.
        stored.Label.ShouldBe("IT i Stockholm");
        stored.UpdatedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public async Task Handle_PresentLabel_Renames_AndLeavesThePredicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, label: null, ct);

        var result = await HandlerFor(db, Owner).Handle(
            new UpdateCompanyWatchCriterionCommand(criterion.Id.Value, "Nya namnet", Criteria: null),
            ct);
        await db.SaveChangesAsync(ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = await db.CompanyWatchCriteria.SingleAsync(ct);
        stored.Label.ShouldBe("Nya namnet");
        stored.Criteria.SniCodes.ShouldBe(["62100"]);
    }

    [Fact]
    public async Task Handle_PresentBlankLabel_ClearsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, label: "Gammalt namn", ct);

        var result = await HandlerFor(db, Owner).Handle(
            new UpdateCompanyWatchCriterionCommand(criterion.Id.Value, "  ", Criteria: null), ct);
        await db.SaveChangesAsync(ct);

        result.IsSuccess.ShouldBeTrue();
        (await db.CompanyWatchCriteria.SingleAsync(ct)).Label.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_InvalidPresentCriteria_FailsWithoutPartialWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, label: "Namn", ct);

        // Criteria fails (empty kommun axis) while Label would have succeeded — the handler runs
        // Criteria FIRST and returns, so nothing (including the label) is half-applied.
        var result = await HandlerFor(db, Owner).Handle(
            new UpdateCompanyWatchCriterionCommand(
                criterion.Id.Value, "Nytt namn", new CompanyWatchCriteriaInput(["62201"], [])),
            ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.MunicipalityRequired");

        // NB: the UnitOfWork pipeline never commits a failed Result, so production discards the
        // tracked state — asserting the DOMAIN objects here shows nothing mutated at all.
        var stored = await db.CompanyWatchCriteria.SingleAsync(ct);
        stored.Label.ShouldBe("Namn");
        stored.Criteria.SniCodes.ShouldBe(["62100"]);
    }

    [Fact]
    public async Task Handle_AnotherUsersCriterion_IsTheIdenticalNotFound_AndLogsTheProbe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var theirs = await SeedAsync(db, Stranger, label: null, ct);

        var failedAccess = Substitute.For<IFailedAccessLogger>();
        var crossUser = await HandlerFor(db, Owner, failedAccess).Handle(
            new UpdateCompanyWatchCriterionCommand(theirs.Id.Value, "Kapat", null), ct);

        failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", theirs.Id.Value, Owner, "UpdateCompanyWatchCriterion");

        var unknown = await HandlerFor(db, Owner).Handle(
            new UpdateCompanyWatchCriterionCommand(Guid.NewGuid(), "Kapat", null), ct);

        // Same code, same kind — indistinguishable, so no existence oracle (IDOR).
        crossUser.IsFailure.ShouldBeTrue();
        unknown.IsFailure.ShouldBeTrue();
        crossUser.Error.Code.ShouldBe(unknown.Error.Code);
        (await db.CompanyWatchCriteria.SingleAsync(c => c.UserId == Stranger, ct))
            .Label.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_FailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var criterion = await SeedAsync(db, Owner, label: null, ct);

        var result = await HandlerFor(db, userId: null).Handle(
            new UpdateCompanyWatchCriterionCommand(criterion.Id.Value, "Nytt", null), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.Unauthorized");
    }

    private static async Task<CompanyWatchCriterion> SeedAsync(
        AppDbContext db, Guid userId, string? label, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(["62100"], ["0180"]).Value;
        var criterion = CompanyWatchCriterion.Create(userId, spec, label, Clock).Value;
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion;
    }
}
