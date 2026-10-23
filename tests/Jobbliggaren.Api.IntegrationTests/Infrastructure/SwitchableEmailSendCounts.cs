using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #1981 PR 3 — the host's read of our own send counts: the real SQL source against the test database, unless a test
/// asks for the answer of a deployment that hands no mail to a provider. The switch is scoped, so a test that forgets
/// to reset it cannot leak into the next.
/// </summary>
internal sealed class SwitchableEmailSendCounts
{
    private int _notConfigured;

    public IDisposable AnswerNotConfigured()
    {
        Interlocked.Increment(ref _notConfigured);
        return new Reset(this);
    }

    internal IEmailSendCountsSource Over(IEmailSendCountsSource real) => new Source(this, real);

    private sealed class Source(SwitchableEmailSendCounts owner, IEmailSendCountsSource real) : IEmailSendCountsSource
    {
        public ValueTask<EmailSendCountsRead> ReadAsync(
            DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            Volatile.Read(ref owner._notConfigured) > 0
                ? ValueTask.FromResult<EmailSendCountsRead>(new EmailSendCountsRead.NotConfigured())
                : real.ReadAsync(firstDay, lastDay, cancellationToken);
    }

    private sealed class Reset(SwitchableEmailSendCounts owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._notConfigured);
        }
    }
}
