using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>
/// #1981 PR 3 (ADR 0180) — the PostgreSQL count: one parameterised upsert per attempt into the Swedish civil date's row.
/// The sender is a singleton, so each write opens a scope of its own, and with it a connection of its own: a failed
/// count can never abort a transaction the caller holds. Api and Worker increment the same row concurrently;
/// <c>ON CONFLICT … DO UPDATE</c> makes that atomic.
/// </summary>
internal sealed class SqlEmailSendTally(IServiceScopeFactory scopes, ILogger<SqlEmailSendTally> logger)
    : EmailSendTally(logger)
{
    protected override async Task WriteAsync(
        EmailKind kind, EmailSendOutcome outcome, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var day = services.GetRequiredService<ISwedishCalendar>()
            .DayOf(services.GetRequiredService<IDateTimeProvider>().UtcNow);
        var kindValue = (short)kind;
        var outcomeValue = (short)outcome;

        await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(
            $"""
            INSERT INTO public.email_send_daily_counts AS t (day, kind, outcome, attempts)
            VALUES ({day}, {kindValue}, {outcomeValue}, 1)
            ON CONFLICT (day, kind, outcome) DO UPDATE SET attempts = t.attempts + 1
            """,
            cancellationToken);
    }
}
