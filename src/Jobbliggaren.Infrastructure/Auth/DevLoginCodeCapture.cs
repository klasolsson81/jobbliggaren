using System.Collections.Concurrent;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Holds the login codes <see cref="DevLoginCodeCapturingEmailSender"/>
/// saw go out, for <see cref="IDevLoginCodeReader"/> (#1735, security-auditor Q15). Only a recipient
/// <see cref="ConsoleEmailSender.IsReservedRecipient"/> admits is held — the same member the Console sender's
/// body guard uses, so the two cannot drift — keyed by the address's fingerprint, never the address. A held
/// code is taken once and lapses with the challenge; a full capture refuses new addresses rather than grow.
/// </summary>
internal sealed class DevLoginCodeCapture(IDateTimeProvider clock) : IDevLoginCodeReader
{
    internal const int Capacity = 256;

    private readonly ConcurrentDictionary<string, HeldCode> _codes = new(StringComparer.Ordinal);

    private readonly record struct HeldCode(string Code, DateTimeOffset CapturedAt);

    internal void Capture(string email, LoginCode code)
    {
        if (!ConsoleEmailSender.IsReservedRecipient(email))
            return;

        var now = clock.UtcNow;
        foreach (var entry in _codes)
        {
            if (!IsLive(entry.Value, now))
                _codes.TryRemove(entry);
        }

        var key = SubjectFingerprint.Hex(email);
        if (_codes.Count >= Capacity && !_codes.ContainsKey(key))
            return;

        // A newer mail's code replaces the older one: the store burned the older challenge when it minted it.
        _codes[key] = new HeldCode(code.Reveal(), now);
    }

    public string? TakeCode(string email) =>
        _codes.TryRemove(SubjectFingerprint.Hex(email), out var held) && IsLive(held, clock.UtcNow)
            ? held.Code
            : null;

    private static bool IsLive(HeldCode held, DateTimeOffset now) =>
        now - held.CapturedAt < LoginChallengePolicy.ChallengeTtl;
}

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Wraps the composed <see cref="IEmailSender"/> and, after a login
/// challenge mail carrying a code has gone out, hands the code to <see cref="DevLoginCodeCapture"/>. Every
/// mail is forwarded unchanged; a send that throws captures nothing. Only
/// <c>DependencyInjection.AddDevLoginCodeCapture</c> composes it, and only in Development.
/// </summary>
internal sealed class DevLoginCodeCapturingEmailSender(IEmailSender inner, DevLoginCodeCapture capture) : IEmailSender
{
    public bool CanDeliver => inner.CanDeliver;

    public async Task SendLoginChallengeAsync(
        string toEmail, LoginChallengeEmail content, CancellationToken cancellationToken)
    {
        await inner.SendLoginChallengeAsync(toEmail, content, cancellationToken);

        if (content is LoginChallengeEmail.CodeAndLink withCode)
            capture.Capture(toEmail, withCode.Code);
    }

    public Task SendMatchNotificationEmailAsync(
        string toEmail, MatchNotificationEmail content, CancellationToken cancellationToken) =>
        inner.SendMatchNotificationEmailAsync(toEmail, content, cancellationToken);

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail, FollowedCompanyNotificationEmail content, CancellationToken cancellationToken) =>
        inner.SendFollowedCompanyNotificationEmailAsync(toEmail, content, cancellationToken);

    public Task SendEmailChangeConfirmationAsync(
        string toEmail, EmailChangeConfirmationEmail content, CancellationToken cancellationToken) =>
        inner.SendEmailChangeConfirmationAsync(toEmail, content, cancellationToken);

    public Task SendEmailChangedNotificationAsync(string toEmail, CancellationToken cancellationToken) =>
        inner.SendEmailChangedNotificationAsync(toEmail, cancellationToken);

    public Task SendEmailConfirmationAsync(
        string toEmail, EmailConfirmationEmail content, CancellationToken cancellationToken) =>
        inner.SendEmailConfirmationAsync(toEmail, content, cancellationToken);

    public Task SendAccountExistsNoticeAsync(string toEmail, CancellationToken cancellationToken) =>
        inner.SendAccountExistsNoticeAsync(toEmail, cancellationToken);

    public Task SendPasswordResetAsync(
        string toEmail, PasswordResetEmail content, CancellationToken cancellationToken) =>
        inner.SendPasswordResetAsync(toEmail, content, cancellationToken);

    public Task SendPasswordChangedNoticeAsync(string toEmail, CancellationToken cancellationToken) =>
        inner.SendPasswordChangedNoticeAsync(toEmail, cancellationToken);
}
