using Jobbliggaren.Application.Admin.Email;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>#1981 PR 3 (ADR 0180) — the answer where no mail is handed to a provider, so nothing is counted.</summary>
internal sealed class NotConfiguredEmailSendCounts : IEmailSendCountsSource
{
    public ValueTask<EmailSendCountsRead> ReadAsync(
        DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
        ValueTask.FromResult<EmailSendCountsRead>(new EmailSendCountsRead.NotConfigured());
}
