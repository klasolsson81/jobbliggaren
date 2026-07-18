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
    private static readonly BrandGroupId ValidBrandGroupId = BrandGroupId.Create("volvo-koncernen").Value;

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
        // XOR side of the discriminator: an EMPLOYER watch carries no brand-group id.
        watch.BrandGroupId.ShouldBeNull();
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
    // FollowBrandGroup (#311 PR-5, ADR 0087 D4) — the BrandGroup XOR side
    // ---------------------------------------------------------------

    [Fact]
    public void FollowBrandGroup_WithValidData_CreatesActiveBrandGroupWatch()
    {
        var result = CompanyWatch.FollowBrandGroup(ValidUserId, ValidBrandGroupId, Clock);

        result.IsSuccess.ShouldBeTrue();
        var watch = result.Value;
        watch.UserId.ShouldBe(ValidUserId);
        watch.BrandGroupId.ShouldBe(ValidBrandGroupId);
        // XOR side of the discriminator: a BRAND_GROUP watch carries no org.nr (the masking/scan
        // paths must never treat a group watch as an employer follow).
        watch.OrganizationNumber.ShouldBeNull();
        watch.TargetType.ShouldBe(CompanyWatchTargetType.BrandGroup);
        watch.CreatedAt.ShouldBe(Clock.UtcNow);
        watch.DeletedAt.ShouldBeNull();
        watch.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void FollowBrandGroup_WithEmptyUserId_Fails()
    {
        var result = CompanyWatch.FollowBrandGroup(Guid.Empty, ValidBrandGroupId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.UserIdRequired");
    }

    [Fact]
    public void FollowBrandGroup_WithNullBrandGroupId_Fails()
    {
        var result = CompanyWatch.FollowBrandGroup(ValidUserId, null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.BrandGroupIdRequired");
    }

    [Fact]
    public void FollowBrandGroup_DoesNotBackfillToken_HasNoOrgNr()
    {
        // A group watch has no org.nr to tokenise — the #544 backfill must skip it (never NRE on
        // the null org.nr) and report no conversion.
        var watch = CompanyWatch.FollowBrandGroup(ValidUserId, ValidBrandGroupId, Clock).Value;

        var converted = watch.ApplyOrganizationNumberTokenBackfill(
            OrganizationNumber.FromTrusted("anytoken"));

        converted.ShouldBeFalse();
        watch.OrganizationNumber.ShouldBeNull();
    }

    // The enum is stored BY NAME (varchar(20)); a rename/reorder would silently corrupt persisted
    // rows. Pin both the member name string and its numeric value.
    [Fact]
    public void CompanyWatchTargetType_BrandGroup_HasStableNameAndValue()
    {
        Enum.GetName(CompanyWatchTargetType.BrandGroup).ShouldBe("BrandGroup");
        ((int)CompanyWatchTargetType.BrandGroup).ShouldBe(1);
        ((int)CompanyWatchTargetType.Employer).ShouldBe(0);
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

    // ---------------------------------------------------------------
    // Per-watch filter (bevaknings-reconcile RF-2, 2026-07-12)
    // ---------------------------------------------------------------

    private static WatchFilterSpec ValidFilter() =>
        WatchFilterSpec.Create(["kommun_a"], ["lan_a"], onlyMatched: true).Value;

    [Fact]
    public void Follow_CreatesWatchWithoutFilter()
    {
        FollowValid().Filter.ShouldBeNull();
    }

    [Fact]
    public void SetFilter_OnActiveWatch_SetsFilter()
    {
        var watch = FollowValid();
        var filter = ValidFilter();

        var result = watch.SetFilter(filter);

        result.IsSuccess.ShouldBeTrue();
        watch.Filter.ShouldBe(filter);
    }

    [Fact]
    public void SetFilter_OnDeletedWatch_Fails()
    {
        var watch = FollowValid();
        watch.SoftDelete(Clock);

        var result = watch.SetFilter(ValidFilter());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.NotActive");
        watch.Filter.ShouldBeNull();
    }

    [Fact]
    public void SetFilter_WithNull_Fails()
    {
        var watch = FollowValid();

        var result = watch.SetFilter(null!);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.FilterRequired");
    }

    [Fact]
    public void ClearFilter_RemovesFilter_AndIsIdempotent()
    {
        var watch = FollowValid();
        watch.SetFilter(ValidFilter());

        watch.ClearFilter();
        watch.Filter.ShouldBeNull();

        watch.ClearFilter(); // idempotent no-op
        watch.Filter.ShouldBeNull();
    }

    [Fact]
    public void CompanyWatch_Unfollow_ClearsFilter()
    {
        // RF-2 sub-bind (senior-cto-advisor 2026-07-12): unfollow ends the 6(1)(b) relation —
        // the profiling-adjacent filter preference must not sit latent on the soft-deleted row.
        var watch = FollowValid();
        watch.SetFilter(ValidFilter());

        watch.SoftDelete(FakeDateTimeProvider.At(Clock.UtcNow.AddDays(3)));

        watch.Filter.ShouldBeNull();
    }

    [Fact]
    public void CompanyWatch_RefollowAfterFilteredUnfollow_StartsWithDefaultFilter()
    {
        // RF-2 sub-bind: the resurrected follow is a clean show-all follow — no silently
        // inherited narrowing (§5 transparency; the exact resurrect trap the underlag flagged).
        var watch = FollowValid();
        watch.SetFilter(ValidFilter());
        watch.SoftDelete(FakeDateTimeProvider.At(Clock.UtcNow.AddDays(3)));

        watch.Refollow(FakeDateTimeProvider.At(Clock.UtcNow.AddDays(10)));

        watch.DeletedAt.ShouldBeNull();
        watch.Filter.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // ApplyOrganizationNumberTokenBackfill (#544, ADR 0090 D5) — idempotent-by-shape backfill
    // ---------------------------------------------------------------

    // A personnummer-shaped (third digit 0) legal-form: a legacy PLAINTEXT enskild-firma org.nr as
    // stored before #544 (Follow stores the VO verbatim; only the executor tokenises).
    private static readonly OrganizationNumber PnrShapedPlaintext =
        OrganizationNumber.Create("9001011234").Value;

    // A 64-char lowercase-hex HMAC token — the at-rest form of a pnr-shaped org.nr after #544. Handed
    // in already-wrapped: the Domain never sees the pepper (the Application layer computed it).
    private static readonly OrganizationNumber Token =
        OrganizationNumber.FromTrusted("91febfd18014665c2a686bb4e29c4400a806e46badb333758482bafa873f2e95");

    private static readonly OrganizationNumber OtherToken =
        OrganizationNumber.FromTrusted("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");

    [Fact]
    public void ApplyOrganizationNumberTokenBackfill_OnPlaintextPnrShapedValue_ConvertsToToken_ReturnsTrue()
    {
        // The one convertible case: a 10-digit personnummer-shaped PLAINTEXT value becomes the token,
        // discarding the plaintext in place (irreversible — the point of the backfill).
        var watch = CompanyWatch.Follow(ValidUserId, PnrShapedPlaintext, Clock).Value;

        var converted = watch.ApplyOrganizationNumberTokenBackfill(Token);

        converted.ShouldBeTrue();
        watch.OrganizationNumber.ShouldBe(Token);
        watch.OrganizationNumber!.Value.ShouldBe(
            "91febfd18014665c2a686bb4e29c4400a806e46badb333758482bafa873f2e95");
    }

    [Fact]
    public void ApplyOrganizationNumberTokenBackfill_OnAbOrgNr_IsNoOp_ReturnsFalse()
    {
        // An AB org.nr (ValidOrgNr, third digit 9) is NOT personnummer-shaped → public data → stays
        // plaintext. The backfill must never tokenise it (it would break the scan's SQL IN match).
        var watch = FollowValid();

        var converted = watch.ApplyOrganizationNumberTokenBackfill(Token);

        converted.ShouldBeFalse();
        watch.OrganizationNumber.ShouldBe(ValidOrgNr, "an AB org.nr stays plaintext at rest");
    }

    [Fact]
    public void ApplyOrganizationNumberTokenBackfill_OnAlreadyTokenisedValue_IsNoOp_ReturnsFalse()
    {
        // Idempotent BY SHAPE: an already-tokenised value has length ≠ 10, so a re-run (or the run
        // after a crash) never double-tokenises. Passing a DIFFERENT token proves it does not overwrite.
        var watch = CompanyWatch.Follow(ValidUserId, Token, Clock).Value;

        var converted = watch.ApplyOrganizationNumberTokenBackfill(OtherToken);

        converted.ShouldBeFalse();
        watch.OrganizationNumber.ShouldBe(Token, "an already-tokenised value is a fixed point — never re-tokenised");
    }

    [Fact]
    public void ApplyOrganizationNumberTokenBackfill_WithNull_ReturnsFalse_AndDoesNotMutate()
    {
        var watch = CompanyWatch.Follow(ValidUserId, PnrShapedPlaintext, Clock).Value;

        var converted = watch.ApplyOrganizationNumberTokenBackfill(null!);

        converted.ShouldBeFalse();
        watch.OrganizationNumber.ShouldBe(PnrShapedPlaintext, "a null token argument is a no-op, never a wipe");
    }
}
