using System.Collections.Concurrent;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #241 — deterministic recording fake for <see cref="IEmailSender"/> in Api integration.
/// Registered last-wins in <see cref="ApiFactory"/> (parity with <see cref="ApiKmsFake"/>) so the
/// integration host NEVER composes the real transactional provider. Without it, a gitignored
/// <c>appsettings.Local.json</c> carrying <c>Email:Provider=Resend</c> + a live key makes the host
/// resolve <c>ResendEmailSender</c>; Resend's test-mode only sends to the account owner, so any
/// email-SUCCESS path to an <c>@example.com</c> recipient gets a 403 <c>ResendException</c> → 500
/// (four tests green in CI, red locally — the #220 residual). The override bypasses the config-order
/// problem entirely: a forced <c>Email__Provider=Console</c> env var does NOT win because
/// <c>appsettings.Local.json</c> is layered AFTER environment variables (verified empirically), but a
/// last-wins DI singleton in <c>ConfigureServices</c> runs after the whole host is composed.
/// <para>
/// Recording (not a pure no-op) so tests can positively assert a side-effect ("a confirmation email
/// was queued to X") without touching the network. Append-only + thread-safe; tests assert by the
/// unique per-test recipient, so the singleton's collection-shared lifetime needs no reset. Records
/// only the kind + recipient — never the plaintext invitation token or any body content (secret/PII
/// hygiene, even in a test fake).
/// </para>
/// </summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<RecordedEmail> _sent = new();

    /// <summary>Snapshot of every email queued through this fake since host start.</summary>
    public IReadOnlyList<RecordedEmail> Sent => [.. _sent];

    public Task SendInvitationEmailAsync(
        string toEmail,
        string plaintextToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.Invitation, toEmail));
        return Task.CompletedTask;
    }

    public Task SendWaitlistConfirmationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.WaitlistConfirmation, toEmail));
        return Task.CompletedTask;
    }

    public Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        MatchNotificationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.MatchNotification, toEmail));
        return Task.CompletedTask;
    }
}

/// <summary>Which <see cref="IEmailSender"/> method recorded the send.</summary>
internal enum RecordedEmailKind
{
    Invitation,
    WaitlistConfirmation,
    MatchNotification,
}

/// <summary>A single email queued through <see cref="RecordingEmailSender"/> (kind + recipient only).</summary>
internal sealed record RecordedEmail(RecordedEmailKind Kind, string ToEmail);
