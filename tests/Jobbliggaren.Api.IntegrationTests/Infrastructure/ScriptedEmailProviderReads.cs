using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #1981 — the provider reads every host <see cref="ApiFactory"/> builds resolves, in place of the configured source, so
/// no test reaches the provider whatever a gitignored <c>appsettings.Local.json</c> says. Both reads answer "not
/// configured" unless a test scripts them with <see cref="Script"/>: a scope on this singleton rather than a derived
/// host, for the reasons <see cref="RecordingEmailSender.Incapable"/> gives.
/// </summary>
internal sealed class ScriptedEmailProviderReads : IEmailProviderUsageSource
{
    private static readonly Answers NotConfigured =
        new(new EmailProviderUsageRead.NotConfigured(), new EmailProviderPlanRead.NotConfigured());

    private Answers _current = NotConfigured;

    public ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _current).Usage);

    public ValueTask<EmailProviderPlanRead> ReadPlanAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _current).Plan);

    /// <summary>Both reads answer as scripted until the returned scope is disposed, which restores the answers before it.</summary>
    internal IDisposable Script(EmailProviderUsageRead usage, EmailProviderPlanRead plan)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(plan);

        var previous = Interlocked.Exchange(ref _current, new Answers(usage, plan));
        return new ScriptScope(this, previous);
    }

    private sealed record Answers(EmailProviderUsageRead Usage, EmailProviderPlanRead Plan);

    private sealed class ScriptScope(ScriptedEmailProviderReads owner, Answers previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Volatile.Write(ref owner._current, previous);
            }
        }
    }
}
