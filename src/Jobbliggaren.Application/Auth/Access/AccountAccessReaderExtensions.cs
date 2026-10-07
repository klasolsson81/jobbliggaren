using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Auth.Access;

public static class AccountAccessReaderExtensions
{
    public static async Task<AccountAccessProof?> ReadCurrentProofAsync(
        this IAccountAccessReader access, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.AccessRevision is not { } revision)
            return null;
        var epoch = await access.ReadEpochAsync(cancellationToken);
        var account = await access.ReadAsync(userId, cancellationToken);
        var proof = new AccountAccessProof(epoch, userId, revision);
        return account is not null && proof.Admits(account) ? proof : null;
    }
}
