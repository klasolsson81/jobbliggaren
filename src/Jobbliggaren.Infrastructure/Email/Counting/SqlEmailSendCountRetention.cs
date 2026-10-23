using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>#1981 PR 3 (ADR 0180) — the retention's delete; the recording start is never touched.</summary>
internal sealed class SqlEmailSendCountRetention(AppDbContext db) : IEmailSendCountRetention
{
    public Task<int> DeleteBeforeAsync(DateOnly firstKeptDay, CancellationToken cancellationToken) =>
        db.Set<EmailSendDailyCount>()
            .Where(count => count.Day < firstKeptDay)
            .ExecuteDeleteAsync(cancellationToken);
}
