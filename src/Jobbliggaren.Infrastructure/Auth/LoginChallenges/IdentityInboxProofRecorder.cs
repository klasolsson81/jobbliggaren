using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Jobbliggaren.Infrastructure.Auth.LoginChallenges;

/// <summary>
/// The Identity side of a first passwordless inbox proof (#1735, security-auditor Q-S3). The flag and the
/// stamp rotation leave in ONE <c>UPDATE</c>: <see cref="UserManager{TUser}.UpdateSecurityStampAsync"/> rotates
/// the stamp in memory and then saves the whole user, the flag set just before included.
/// </summary>
internal sealed class IdentityInboxProofRecorder(UserManager<ApplicationUser> userManager) : IInboxProofRecorder
{
    public async Task<InboxProof> RecordAsync(Guid userId, CancellationToken ct)
    {
        // The caller resolved this id from the account table a moment ago, so absence is a race with a
        // hard delete, not a state to answer.
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} vanished between resolve and inbox proof.");

        if (user.EmailConfirmed)
            return InboxProof.AlreadyConfirmed;

        user.EmailConfirmed = true;
        var result = await userManager.UpdateSecurityStampAsync(user);

        // Nothing may follow a write that did not happen: no invalidation, session or audit row. A
        // ConcurrencyFailure is reachable — two first proofs racing on two live records. Codes only: an
        // Identity Description can interpolate the address.
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Inbox proof for user {userId} was not persisted: "
                + string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        return InboxProof.FirstProofRecorded;
    }
}
