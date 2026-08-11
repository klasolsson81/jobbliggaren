using System.Collections.Concurrent;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #241 — deterministic recording fake for <see cref="IEmailSender"/> in Api integration.
/// Registered last-wins in <see cref="ApiFactory"/> (parity with <see cref="RecordingBackgroundJobController"/>) so the
/// integration host NEVER composes the real transactional provider. Without it, a gitignored
/// <c>appsettings.Local.json</c> carrying <c>Email:Provider=Ses</c> + live IAM keys makes the host
/// resolve <c>SesEmailSender</c> and an email-SUCCESS path would attempt a real send to an
/// <c>@example.com</c> recipient — which in the SES sandbox is an unverified address, so it fails
/// (the shape #220 measured against the previous provider). The override bypasses the config-order
/// problem entirely: a forced <c>Email__Provider=Console</c> env var does NOT win because
/// <c>appsettings.Local.json</c> is layered AFTER environment variables (verified empirically), but a
/// last-wins DI singleton in <c>ConfigureServices</c> runs after the whole host is composed.
/// <para>
/// Recording (not a pure no-op) so tests can positively assert a side-effect ("a confirmation email
/// was queued to X") without touching the network. Append-only + thread-safe; tests assert by the
/// unique per-test recipient, so the singleton's collection-shared lifetime needs no reset. Records
/// only the kind + recipient — never any body content (secret/PII hygiene, even in a test fake).
/// </para>
/// </summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<RecordedEmail> _sent = new();

    /// <summary>Snapshot of every email queued through this fake since host start.</summary>
    public IReadOnlyList<RecordedEmail> Sent => [.. _sent];

    private volatile bool _canDeliver = true;

    /// <summary>
    /// <see langword="true"/> by default — this fake RECORDS, which is the test-suite analogue of
    /// delivering (#1087). Answering <see langword="false"/> unconditionally would make every
    /// delivery-dependent handler refuse before reaching the send, and the assertions over
    /// <see cref="Sent"/> would then pass or fail for reasons unrelated to what they check.
    /// </summary>
    public bool CanDeliver => _canDeliver;

    /// <summary>
    /// Flips this fake to incapable for the duration of the returned scope, so a test can drive the
    /// refusal path end to end. <b>A scope rather than a bare setter, and the reason is structural:</b>
    /// this instance is a singleton shared by every host <see cref="ApiFactory"/> builds, so a leaked
    /// <c>false</c> would silently convert unrelated later tests in the <c>Api</c> collection into
    /// refusal tests — passing or failing for a cause they never name. <c>using</c> makes the reset
    /// impossible to forget; a <c>finally</c> would only make it easy to remember.
    /// <para>
    /// <b>Why the capability is flipped in place instead of on a dedicated host.</b> A
    /// <c>WithWebHostBuilder</c> override would be the cleaner seam, but it would be the FOURTH
    /// <c>WebApplicationFactory</c> in this suite, and the suite sits one below EF's process-global
    /// <c>ManyServiceProvidersCreatedWarning</c> ceiling — the next host fells whichever collection
    /// fixture initialises after it (CLAUDE.md §11, #1190, and the same reasoning already written at
    /// <c>ApiFactory.CreateRegistrationsClosedClient</c>). Safe because <c>[Collection("Api")]</c>
    /// serialises every class that shares this fixture.
    /// </para>
    /// </summary>
    internal IDisposable Incapable()
    {
        _canDeliver = false;
        return new CapabilityScope(this);
    }

    private sealed class CapabilityScope(RecordingEmailSender owner) : IDisposable
    {
        public void Dispose() => owner._canDeliver = true;
    }

    public Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.MatchNotification, toEmail));
        return Task.CompletedTask;
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.FollowedCompanyNotification, toEmail));
        return Task.CompletedTask;
    }

    public Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.EmailChangeConfirmation, toEmail));
        return Task.CompletedTask;
    }

    public Task SendEmailChangedNotificationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.EmailChangedNotification, toEmail));
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.EmailConfirmation, toEmail));
        return Task.CompletedTask;
    }

    public Task SendAccountExistsNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.AccountExistsNotice, toEmail));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string toEmail,
        PasswordResetEmail content,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.PasswordReset, toEmail));
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue(new RecordedEmail(RecordedEmailKind.PasswordChangedNotice, toEmail));
        return Task.CompletedTask;
    }
}

/// <summary>Which <see cref="IEmailSender"/> method recorded the send.</summary>
internal enum RecordedEmailKind
{
    MatchNotification,
    FollowedCompanyNotification,
    EmailChangeConfirmation,
    EmailChangedNotification,
    EmailConfirmation,
    AccountExistsNotice,
    PasswordReset,
    PasswordChangedNotice,
}

/// <summary>A single email queued through <see cref="RecordingEmailSender"/> (kind + recipient only).</summary>
internal sealed record RecordedEmail(RecordedEmailKind Kind, string ToEmail);
