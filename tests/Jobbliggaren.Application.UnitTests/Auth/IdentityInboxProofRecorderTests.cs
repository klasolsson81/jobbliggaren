using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1735 — the Identity side of a first passwordless inbox proof (security-auditor Q-S3). The write it makes
/// and its all-or-nothing answer are pinned end to end in LoginChallengeProofTests; what is pinned here is the
/// adapter's branches: a confirmed address writes nothing, an unconfirmed
/// one is confirmed and its stamp rotated in ONE save, and a write Identity did not persist throws.
/// <c>ConcurrencyFailure</c> is what <c>UserStore.UpdateAsync</c> answers when two first proofs race on two live
/// records.
/// Unconfirmed fixtures name the retired password register's row shape; PasswordlessAccountCreatorTests
/// pins that today's writer emits confirmed accounts only.
/// </summary>
public sealed class IdentityInboxProofRecorderTests
{
    private readonly UserManager<ApplicationUser> _users =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_confirmed_address_is_left_as_it_is()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "confirmed@example.com", EmailConfirmed = true };
        _users.FindByIdAsync(user.Id.ToString()).Returns(user);

        var proof = await RecordAsync(user);

        proof.ShouldBe(InboxProof.AlreadyConfirmed);
        AsyncCallsOn(_users).ShouldBe([nameof(UserManager<ApplicationUser>.FindByIdAsync)]);
    }

    [Fact]
    public async Task An_unconfirmed_address_is_confirmed_and_its_stamp_rotated_in_one_save()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "unconfirmed@example.com", EmailConfirmed = false };
        _users.FindByIdAsync(user.Id.ToString()).Returns(user);
        bool? confirmedAtSave = null;
        _users.UpdateSecurityStampAsync(user)
            .Returns(IdentityResult.Success)
            .AndDoes(ci => confirmedAtSave = ci.Arg<ApplicationUser>().EmailConfirmed);

        var proof = await RecordAsync(user);

        proof.ShouldBe(InboxProof.FirstProofRecorded);
        // Read at the call, not afterwards: the flag must already be set when the one save runs.
        confirmedAtSave.ShouldBe(true);
        AsyncCallsOn(_users).ShouldBe([
            nameof(UserManager<ApplicationUser>.FindByIdAsync),
            nameof(UserManager<ApplicationUser>.UpdateSecurityStampAsync),
        ]);
    }

    [Fact]
    public async Task A_write_identity_did_not_persist_throws_with_its_codes_and_never_the_address()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "racing@example.com", EmailConfirmed = false };
        _users.FindByIdAsync(user.Id.ToString()).Returns(user);
        _users.UpdateSecurityStampAsync(user).Returns(IdentityResult.Failed(
            new IdentityError { Code = "ConcurrencyFailure", Description = "racing@example.com was changed" }));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => RecordAsync(user));

        thrown.Message.ShouldContain("ConcurrencyFailure");
        thrown.Message.ShouldNotContain("@");
    }

    [Fact]
    public async Task A_first_confirmation_refuses_an_account_only_scope_without_the_lifecycle_lock()
    {
        // The retired password registrar at 22aefd8db admitted unconfirmed accounts. Today's
        // passwordless writer emits only confirmed accounts (PasswordlessAccountCreatorTests).
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "first-proof@example.com", EmailConfirmed = false };
        _users.FindByIdAsync(user.Id.ToString()).Returns(user);
        await using var access = AccountAccessTestKit.Coordinator();
        await using var transaction = await access.BeginAsync([user.Id], lifecycle: false, Ct);
        var recorder = new IdentityInboxProofRecorder(_users, access, AccountAccessTestKit.Reader(id => id == user.Id
            ? AccountAccessTestKit.Account(user.Id, user.Email!) with { InboxConfirmed = false } : null));

        await Should.ThrowAsync<InvalidOperationException>(() => recorder.RecordAsync(user.Id, Ct));

        user.EmailConfirmed.ShouldBeFalse();
        await _users.DidNotReceiveWithAnyArgs().UpdateSecurityStampAsync(default!);
        access.Commits.ShouldBe(0);
    }

    private async Task<InboxProof> RecordAsync(ApplicationUser user)
    {
        await using var access = AccountAccessTestKit.Coordinator();
        await using var transaction = await access.BeginAsync([user.Id], lifecycle: !user.EmailConfirmed, Ct);
        var recorder = new IdentityInboxProofRecorder(
            _users, access, AccountAccessTestKit.Reader(id => id == user.Id
                ? AccountAccessTestKit.Account(user.Id, user.Email!) with { InboxConfirmed = user.EmailConfirmed } : null));
        var proof = await recorder.RecordAsync(user.Id, Ct);
        await transaction.CommitAsync(Ct);
        return proof;
    }

    // The async members received, in order. The suffix filter keeps out whatever the substitute's base
    // constructor touches; a DidNotReceive on one named method would let a different write through.
    private static string[] AsyncCallsOn(UserManager<ApplicationUser> users) =>
        [.. users.ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Where(n => n.EndsWith("Async", StringComparison.Ordinal))];
}
