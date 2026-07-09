namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Pays a fixed password-hash verification cost on a login path that would otherwise short-circuit
/// before any cryptographic work, so response timing does not reveal whether an account exists
/// (account-enumeration via a timing side-channel — #481 Low). Consumed by
/// <see cref="UserAccountService.ValidateCredentialsAsync"/> on the unknown-email branch, which would
/// otherwise return immediately while a known email with a wrong password pays a full PBKDF2
/// derivation.
/// </summary>
public interface ILoginTimingEqualizer
{
    /// <summary>
    /// Runs one dummy PBKDF2 verification against a fixed, never-matching hash so the caller pays the
    /// same key-derivation cost as a real wrong-password check. The verdict is intentionally
    /// discarded; only the timing matters.
    /// </summary>
    void Equalize(string? candidatePassword);
}
