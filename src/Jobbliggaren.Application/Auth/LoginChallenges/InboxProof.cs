namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>Whether proving the inbox changed the account.</summary>
public enum InboxProof
{
    /// <summary>The address was already confirmed: nothing was written.</summary>
    AlreadyConfirmed,

    /// <summary>
    /// The address was unconfirmed: it is now confirmed, the password is removed and the security stamp is
    /// rotated, in one Identity write.
    /// </summary>
    FirstProofRecorded,
}
