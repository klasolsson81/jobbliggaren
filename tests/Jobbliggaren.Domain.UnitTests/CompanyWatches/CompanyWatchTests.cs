using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — invariants for the <see cref="CompanyWatch"/> aggregate root:
/// the <see cref="CompanyWatch.Follow"/> guards, idempotent <see cref="CompanyWatch.SoftDelete"/>
/// (unfollow), and single-row <see cref="CompanyWatch.Refollow"/> (FORK B1 resurrect). The
/// active-partial UNIQUE + Art.17 cascade wiring are pinned by the EF config + the
/// AccountHardDeleteCascadeFitnessTests / HardDeleteAccountsJobIntegrationTests.
/// </summary>
public class CompanyWatchTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly OrganizationNumber ValidOrgNr = OrganizationNumber.Create("5592804784").Value;

    private static CompanyWatch FollowValid() =>
        CompanyWatch.Follow(ValidUserId, ValidOrgNr, Clock).Value;

    // ---------------------------------------------------------------
    // Follow — happy path + guards
    // ---------------------------------------------------------------

    [Fact]
    public void Follow_WithValidData_CreatesActiveEmployerWatch()
    {
        var result = CompanyWatch.Follow(ValidUserId, ValidOrgNr, Clock);

        result.IsSuccess.ShouldBeTrue();
        var watch = result.Value;
        watch.UserId.ShouldBe(ValidUserId);
        watch.OrganizationNumber.ShouldBe(ValidOrgNr);
        watch.TargetType.ShouldBe(CompanyWatchTargetType.Employer);
        watch.CreatedAt.ShouldBe(Clock.UtcNow);
        watch.DeletedAt.ShouldBeNull();
        watch.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Follow_WithEmptyUserId_Fails()
    {
        var result = CompanyWatch.Follow(Guid.Empty, ValidOrgNr, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.UserIdRequired");
    }

    [Fact]
    public void Follow_WithNullOrganizationNumber_Fails()
    {
        var result = CompanyWatch.Follow(ValidUserId, null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.OrganizationNumberRequired");
    }

    // ---------------------------------------------------------------
    // SoftDelete (unfollow) — idempotent
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_OnActiveWatch_StampsDeletedAt()
    {
        var watch = FollowValid();
        var deleteClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(3));

        watch.SoftDelete(deleteClock);

        watch.DeletedAt.ShouldBe(deleteClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_OnAlreadyDeletedWatch_IsIdempotent()
    {
        var watch = FollowValid();
        var firstDelete = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(3));
        watch.SoftDelete(firstDelete);

        watch.SoftDelete(FakeDateTimeProvider.At(Clock.UtcNow.AddDays(9)));

        // The original stamp is retained — no overwrite on a repeat unfollow.
        watch.DeletedAt.ShouldBe(firstDelete.UtcNow);
    }

    // ---------------------------------------------------------------
    // Refollow (FORK B1 resurrect) — single row, clears DeletedAt, refreshes CreatedAt
    // ---------------------------------------------------------------

    [Fact]
    public void Refollow_OnSoftDeletedWatch_ClearsDeletedAtAndRefreshesCreatedAt()
    {
        var watch = FollowValid();
        watch.SoftDelete(FakeDateTimeProvider.At(Clock.UtcNow.AddDays(3)));
        var refollowClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(10));

        watch.Refollow(refollowClock);

        watch.DeletedAt.ShouldBeNull();
        watch.CreatedAt.ShouldBe(refollowClock.UtcNow); // new active period
    }

    [Fact]
    public void Refollow_OnActiveWatch_IsNoOp()
    {
        var watch = FollowValid();
        var originalCreatedAt = watch.CreatedAt;

        watch.Refollow(FakeDateTimeProvider.At(Clock.UtcNow.AddDays(10)));

        watch.DeletedAt.ShouldBeNull();
        watch.CreatedAt.ShouldBe(originalCreatedAt); // unchanged — re-following an active watch is inert
    }

    [Fact]
    public void Follow_RaisesNoDomainEvents()
    {
        // Deliberate (mirrors UserJobAdMatch): the Art.17 cascade is handler-driven by UserId and
        // the PR-4 notification rail is a batch scan — no reactive consumer of a follow event.
        FollowValid().DomainEvents.ShouldBeEmpty();
    }
}
