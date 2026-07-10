using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #714 — an <see cref="IEmailSender"/> that throws on the two registration sends
/// (<see cref="SendEmailConfirmationAsync"/> = the fresh branch,
/// <see cref="SendAccountExistsNoticeAsync"/> = the taken/duplicate-swallow branch). Used by the
/// send-failure-symmetry test to prove that a transport fault yields the SAME response for a fresh and
/// a taken address (CTO-bind Risk 1: symmetry is the load-bearing invariant — a divergent failure mode
/// would itself be an enumeration distinguisher). The other sends are irrelevant to registration and
/// are left as no-ops.
/// </summary>
internal sealed class ThrowingEmailSender : IEmailSender
{
    private static Task Throw(string kind) =>
        throw new InvalidOperationException($"ThrowingEmailSender: simulated transport failure ({kind}).");

    public Task SendEmailConfirmationAsync(
        string toEmail, EmailConfirmationEmail content,
        EmailConfirmationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        => Throw("email-confirmation");

    public Task SendAccountExistsNoticeAsync(
        string toEmail, AccountExistsNoticeIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        => Throw("account-exists-notice");

    public Task SendMatchNotificationEmailAsync(
        string toEmail, MatchNotificationEmail content,
        MatchNotificationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail, FollowedCompanyNotificationEmail content,
        FollowedCompanyNotificationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SendEmailChangeConfirmationAsync(
        string toEmail, EmailChangeConfirmationEmail content,
        EmailChangeConfirmationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task SendEmailChangedNotificationAsync(
        string toEmail, EmailChangedNotificationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
