using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Application.CompanyWatches.Commands.CreateCompanyWatchCriterion;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// #560 PR-3 — create-handler: owner-scoping (C-D10), the SERVER-side <c>MaxPerUser</c> cap
/// (C-D11 — reject, never evict) and Domain-error propagation.
/// </summary>
public class CreateCompanyWatchCriterionCommandHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));

    private static readonly CompanyWatchCriteriaInput ValidInput =
        new(["62100"], ["0180"]);

    private static CreateCompanyWatchCriterionCommandHandler HandlerFor(
        AppDbContext db, Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new CreateCompanyWatchCriterionCommandHandler(db, currentUser, Clock);
    }

    [Fact]
    public async Task Handle_ValidInput_PersistsTheCriterion_AndReturnsItsId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var result = await HandlerFor(db, Owner).Handle(
            new CreateCompanyWatchCriterionCommand(ValidInput, "IT i Stockholm"), ct);
        await db.SaveChangesAsync(ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = await db.CompanyWatchCriteria.SingleAsync(ct);
        stored.Id.Value.ShouldBe(result.Value);
        stored.UserId.ShouldBe(Owner);
        stored.Label.ShouldBe("IT i Stockholm");
        stored.Criteria.SniCodes.ShouldBe(["62100"]);
        stored.Criteria.MunicipalityCodes.ShouldBe(["0180"]);
    }

    [Fact]
    public async Task Handle_AtTheCap_RejectsWithConflict_AndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        await SeedCriteriaAsync(db, Owner, CompanyWatchCriterion.MaxPerUser, ct);

        var result = await HandlerFor(db, Owner).Handle(
            new CreateCompanyWatchCriterionCommand(ValidInput, null), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.MaxPerUser");
        // Conflict (409): a well-formed request the CURRENT STATE disallows — not a malformed
        // request (400). The central kind-mapper owns the translation (§3).
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        (await db.CompanyWatchCriteria.CountAsync(ct))
            .ShouldBe(CompanyWatchCriterion.MaxPerUser);
    }

    [Fact]
    public async Task Handle_OneBelowTheCap_Succeeds()
    {
        // The boundary from the other side — without it, an off-by-one (`>` for `>=`) and an
        // always-reject both look identical to the at-the-cap test.
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        await SeedCriteriaAsync(db, Owner, CompanyWatchCriterion.MaxPerUser - 1, ct);

        var result = await HandlerFor(db, Owner).Handle(
            new CreateCompanyWatchCriterionCommand(ValidInput, null), ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_TheCapCountsOnlyTheOwnRows()
    {
        // Another user's 20 criteria must not consume MY budget (the cap is per-user, C-D11).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        await SeedCriteriaAsync(db, Guid.NewGuid(), CompanyWatchCriterion.MaxPerUser, ct);

        var result = await HandlerFor(db, Owner).Handle(
            new CreateCompanyWatchCriterionCommand(ValidInput, null), ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_InvalidSpec_PropagatesTheDomainError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        // Both axes required (Fork B1) — the pipeline validator normally catches this first, but
        // the handler must be correct without the front door (§2.4).
        var result = await HandlerFor(db, Owner).Handle(
            new CreateCompanyWatchCriterionCommand(new CompanyWatchCriteriaInput(["62100"], []), null), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.MunicipalityRequired");
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_FailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();

        var result = await HandlerFor(db, userId: null).Handle(
            new CreateCompanyWatchCriterionCommand(ValidInput, null), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.Unauthorized");
        (await db.CompanyWatchCriteria.AnyAsync(ct)).ShouldBeFalse();
    }

    private static async Task SeedCriteriaAsync(
        AppDbContext db, Guid userId, int count, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.Create(["62100"], ["0180"]).Value;
        for (var i = 0; i < count; i++)
        {
            db.CompanyWatchCriteria.Add(
                CompanyWatchCriterion.Create(userId, spec, label: null, Clock).Value);
        }

        await db.SaveChangesAsync(ct);
    }
}
