using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the retention's deletes: reports received before the cutoff, whose records the database's
/// cascade takes with them, and ledger rows received before it. The state row is never touched.
/// </summary>
internal sealed class SqlDmarcReportRetention(AppDbContext db) : IDmarcReportRetention
{
    public async ValueTask<DmarcRetentionResult> DeleteReceivedBeforeAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var before = cutoff.ToUniversalTime();

        var reports = await db.Set<DmarcStoredReport>()
            .Where(report => report.ReceivedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
        var messages = await db.Set<DmarcMailboxMessage>()
            .Where(message => message.ReceivedAt < before)
            .ExecuteDeleteAsync(cancellationToken);

        return new DmarcRetentionResult(reports, messages);
    }
}
