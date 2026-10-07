using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Access;

public sealed class AccountAccessProofTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(3, 2, 3, true)]
    [InlineData(7, 2, 3, true)]
    [InlineData(0, 2, 3, false)]
    [InlineData(2, 2, 3, false)]
    public void Admits_FlowEpochComparedWithTargetCutoff_UsesTheOriginalFlow(
        long flowEpoch, long revision, long cutoff, bool admitted)
    {
        var account = Active(revision, cutoff);
        var proof = new AccountAccessProof(flowEpoch, UserId, revision);

        proof.Admits(account).ShouldBe(admitted);
    }

    [Fact]
    public void Admits_RevisionBeforeSuspendAndReinstate_RemainsRefused()
    {
        var proof = new AccountAccessProof(0, UserId, 0);
        var reinstated = Active(revision: 2, cutoff: 2);

        proof.Admits(reinstated).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => proof.Bind(reinstated));
        proof.AccessRevision.ShouldBe(0);
        proof.FlowEpoch.ShouldBe(0);
    }

    [Fact]
    public void Admits_UnreachableRebasedEpochWithStaleRevision_DegradesByDenyingAccess()
    {
        // No issuer produces this rebased credential; malformed proof data must still fail closed.
        var proof = new AccountAccessProof(5, UserId, 0);
        var reinstated = Active(revision: 2, cutoff: 2);

        proof.Admits(reinstated).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => proof.Bind(reinstated));
    }

    [Fact]
    public void Admits_ProofBoundToAnotherUser_RefusesIdentityReplacement()
    {
        var proof = new AccountAccessProof(0, Guid.NewGuid(), 0);

        proof.Admits(Active()).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => proof.Bind(Active()));
    }

    [Fact]
    public void Bind_UnknownAddressBeforeRegistration_PreservesItsOriginalEpoch()
    {
        var original = new AccountAccessProof(9);
        var account = Active();

        var bound = original.Bind(account);

        bound.FlowEpoch.ShouldBe(9);
        bound.UserId.ShouldBe(UserId);
        bound.AccessRevision.ShouldBe(0);
        original.UserId.ShouldBeNull();
        original.AccessRevision.ShouldBeNull();
    }

    [Fact]
    public void Bind_UnknownAddressBeforeTargetTransition_RefusesLateBinding()
    {
        var original = new AccountAccessProof(0);

        Should.Throw<InvalidOperationException>(() => original.Bind(Active(revision: 2, cutoff: 2)));
        original.UserId.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 2, false)]
    public void Admits_LegacyGenerationZero_AcceptsOnlyAnAccountWithoutTransitions(
        long revision, long cutoff, bool admitted) =>
        AccountAccessProof.Legacy.Admits(Active(revision, cutoff)).ShouldBe(admitted);

    [Fact]
    public void Admits_SuspendedAccount_RefusesEvenACurrentProof()
    {
        var suspended = Active(revision: 1, cutoff: 1) with { IsSuspended = true };

        new AccountAccessProof(1, UserId, 1).Admits(suspended).ShouldBeFalse();
        suspended.IsEffectiveAdmin.ShouldBeFalse();
    }

    [Fact]
    public void Admits_ProfilePendingDeletion_RefusesEvenAfterReinstatement()
    {
        var clock = new FixedClock(Now);
        var registered = JobSeeker.Register(UserId, TermsAcceptance.AcceptCurrent(clock), clock);
        registered.IsSuccess.ShouldBeTrue();
        registered.Value.SoftDelete(clock);
        registered.Value.DeletedAt.ShouldBe(Now);
        var pendingDeletion = Active(revision: 2, cutoff: 2) with { DeletedAt = registered.Value.DeletedAt };

        new AccountAccessProof(2, UserId, 2).Admits(pendingDeletion).ShouldBeFalse();
        pendingDeletion.HasLiveProfile.ShouldBeFalse();
        pendingDeletion.IsEffectiveAdmin.ShouldBeFalse();
    }

    [Fact]
    public void Admits_HistoricalSplitRegistrationWithoutProfile_RefusesAccess()
    {
        // AccountRegistrar at 22aefd8db committed Identity before a failed profile save. The current writer pin is
        // AccountRegistrationAtomicityTests.OpenAsync_ShouldLeaveNoIdentityOrProfile_WhenAuditSaveFails.
        var withoutProfile = Active() with { HasProfile = false };

        new AccountAccessProof(0, UserId, 0).Admits(withoutProfile).ShouldBeFalse();
        withoutProfile.IsEffectiveAdmin.ShouldBeFalse();
    }

    [Fact]
    public void IsEffectiveAdmin_LiveUsableAccount_RequiresTheAdminRole()
    {
        Active().IsEffectiveAdmin.ShouldBeTrue();
        (Active() with { IsAdmin = false }).IsEffectiveAdmin.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Admits_UnreachableMissingInbox_DegradesByDenyingAccess(string? email)
    {
        // Unreachable through the current passwordless writer: a broken Identity inbox invariant.
        var broken = Active() with { Email = email };

        broken.CanAuthenticate.ShouldBeFalse();
        broken.IsEffectiveAdmin.ShouldBeFalse();
        new AccountAccessProof(0, UserId, 0).Admits(broken).ShouldBeFalse();
    }

    [Fact]
    public void Admits_UnreachableNegativeFlowEpoch_DegradesByDenyingAccess() =>
        new AccountAccessProof(-1, UserId, 0).Admits(Active()).ShouldBeFalse();

    [Fact]
    public void ToString_AccountSnapshot_DoesNotPrintTheInbox()
    {
        var account = Active();

        account.ToString().ShouldNotContain(account.Email!);
        account.ToString().ShouldContain(UserId.ToString());
    }

    [Fact]
    public void Admits_UnreachableOperatorInboxOverwriteWithinOneRevision_RefusesTheOriginalProof()
    {
        var original = Active();
        var proof = new AccountAccessProof(0, UserId, 0) { ExpectedEmail = original.Email };
        // Unreachable through current address writers, which advance revision: an operator overwrites Identity's inbox.
        var changed = original with { Email = "changed@example.com" };

        proof.Admits(original).ShouldBeTrue();
        proof.Bind(original).ExpectedEmail.ShouldBe(original.Email);
        proof.Admits(changed).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => proof.Bind(changed));
        proof.ExpectedEmail.ShouldBe(original.Email);
        proof.ToString().ShouldNotContain(original.Email!);
    }

    [Fact]
    public void Admits_UnreachableOperatorSpellingOverwriteWithinOneRevision_RefusesTheOriginalProof()
    {
        // Unreachable through current address writers: an operator changes only the stored spelling.
        var original = Active();
        var proof = new AccountAccessProof(0, UserId, 0) { ExpectedEmail = original.Email };

        proof.Admits(original with { Email = "Account@example.com" }).ShouldBeFalse();
        proof.FlowEpoch.ShouldBe(0);
        proof.AccessRevision.ShouldBe(0);
    }

    private static AccountAccessSnapshot Active(long revision = 0, long cutoff = 0) =>
        new(UserId, "account@example.com", IsSuspended: false, revision, cutoff,
            HasProfile: true, DeletedAt: null, IsAdmin: true);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
