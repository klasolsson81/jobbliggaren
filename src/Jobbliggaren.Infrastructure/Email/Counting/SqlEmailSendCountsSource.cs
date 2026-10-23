using System.Data;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>
/// #1981 PR 3 (ADR 0181) — reads our own counts and the recording start in one snapshot, through a scope of its own so
/// it can be a singleton beside the provider reads. An unknown stored value or a missing start fails the read rather
/// than being counted around (<see cref="IEmailSendCountsSource"/>).
/// </summary>
internal sealed class SqlEmailSendCountsSource(IServiceScopeFactory scopes) : IEmailSendCountsSource
{
    public async ValueTask<EmailSendCountsRead> ReadAsync(
        DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var starts = await db.Set<EmailSendCounting>().AsNoTracking()
            .Select(start => start.StartedAt)
            .ToListAsync(cancellationToken);
        if (starts.Count != 1)
        {
            throw new InvalidOperationException(
                $"Found {starts.Count} recording starts for the send counts; the migration writes exactly one.");
        }

        var counts = await db.Set<EmailSendDailyCount>().AsNoTracking()
            .Where(count => count.Day >= firstDay && count.Day <= lastDay)
            .Select(count => new { count.Day, count.Kind, count.Outcome, count.Attempts })
            .ToListAsync(cancellationToken);

        var rows = new List<EmailSendCountRow>(counts.Count);
        foreach (var count in counts)
        {
            if (!Enum.IsDefined(count.Kind) || !Enum.IsDefined(count.Outcome))
            {
                throw new InvalidOperationException(
                    $"A send count holds kind {(int)count.Kind} or outcome {(int)count.Outcome}, "
                    + "which this release does not know.");
            }

            rows.Add(new EmailSendCountRow(count.Day, count.Kind, count.Outcome, count.Attempts));
        }

        return new EmailSendCountsRead.Observed(rows, starts[0]);
    }
}
