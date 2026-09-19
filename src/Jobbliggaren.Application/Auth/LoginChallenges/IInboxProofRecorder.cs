namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// Records a first passwordless proof of an account's inbox (#1735, security-auditor Q21/Q-S3). An account
/// whose address was never confirmed may hold a password set by someone who registered the address before
/// its owner did; confirming the address and leaving that password would let it in. So the confirmation,
/// the password's removal and the stamp rotation are ONE write. Reachable only from
/// <see cref="PasswordlessSessionGrant"/>, after a verified or consumed challenge — never a bare
/// force-confirm (ADR 0127 refused exactly that); an architecture test pins the single consumer.
/// </summary>
public interface IInboxProofRecorder
{
    Task<InboxProof> RecordAsync(Guid userId, CancellationToken ct);
}
