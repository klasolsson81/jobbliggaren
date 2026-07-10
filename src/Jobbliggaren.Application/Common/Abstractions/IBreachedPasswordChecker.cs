namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Port for checking a prospective password against a corpus of known-breached passwords
/// (NIST SP 800-63B §5.1.1.2 blocklist requirement; #616, MAJOR-1 Alt A part 2). Implemented in
/// Infrastructure over the HIBP Pwned Passwords k-anonymity range API: only the first five
/// characters of the password's SHA-1 hex ever leave the process — the password itself, the full
/// hash, and the suffix never do, and nothing is logged from the credential.
/// </summary>
public interface IBreachedPasswordChecker
{
    /// <summary>
    /// Checks <paramref name="password"/> against the breach corpus. Transport failures are
    /// classified as <see cref="BreachCheckVerdict.Unavailable"/> — never thrown — except a
    /// genuine caller cancellation, which propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<BreachCheckVerdict> CheckAsync(string password, CancellationToken cancellationToken);
}
