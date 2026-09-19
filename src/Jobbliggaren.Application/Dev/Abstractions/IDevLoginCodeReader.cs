namespace Jobbliggaren.Application.Dev.Abstractions;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Hands a test run the login code the last challenge mail to a
/// reserved address carried (#1735, ADR 0142 D10), so a flow can sign in without a mailbox. It holds the
/// CODE only — never the link, never a body — and each code once, for at most the challenge's lifetime; the
/// code is useless without the challenge id its requester already holds (security-auditor Q15).
///
/// <para>
/// Guarded like <see cref="IDevEmailConfirmer"/>, by two gates keyed on <c>IsDevelopment()</c>: the
/// implementation is registered only in Development (<c>AddDevOnlyTestingSupport</c>), and the endpoint
/// that reads it is mapped only in Development. Deleting <c>Application/Dev/</c> breaks the capture's build.
/// </para>
/// </summary>
public interface IDevLoginCodeReader
{
    /// <summary>
    /// Takes the code captured for <paramref name="email"/>, once. <see langword="null"/> when none is held:
    /// never captured, already taken, expired, or not a reserved recipient.
    /// </summary>
    string? TakeCode(string email);
}
