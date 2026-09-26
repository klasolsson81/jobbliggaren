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

        var proof = await new IdentityInboxProofRecorder(_users).RecordAsync(user.Id, Ct);

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

        var proof = await new IdentityInboxProofRecorder(_users).RecordAsync(user.Id, Ct);

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
            () => new IdentityInboxProofRecorder(_users).RecordAsync(user.Id, Ct));

        thrown.Message.ShouldContain("ConcurrencyFailure");
        thrown.Message.ShouldNotContain("@");
    }

    // The async members received, in order. The suffix filter keeps out whatever the substitute's base
    // constructor touches; a DidNotReceive on one named method would let a different write through.
    private static string[] AsyncCallsOn(UserManager<ApplicationUser> users) =>
        [.. users.ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Where(n => n.EndsWith("Async", StringComparison.Ordinal))];
}
