using System.Data.Common;
using Jobbliggaren.Application.Auth.Activity;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Auth.Activity;

/// <summary>
/// #2161 (ADR 0172) — PostgreSQL implementation of <see cref="IAccountActivityRecorder"/>: one parameterised upsert
/// per write, keyed through the account's profile row, so an account without a profile gets no row and the foreign
/// key bounds what may be written. A value only moves forward and is stored at minute precision. It takes the
/// concrete <see cref="AppDbContext"/> because the <c>Database</c> facade is deliberately absent from
/// <c>IAppDbContext</c> (the <c>AuditTrailEraser</c> form).
/// </summary>
internal sealed partial class SqlAccountActivityRecorder(
    AppDbContext db,
    AccountActivityClaims claims,
    IDateTimeProvider clock,
    ILogger<SqlAccountActivityRecorder> logger) : IAccountActivityRecorder
{
    /// <summary>The longest a write may take before it is abandoned and logged (ADR 0172 D5).</summary>
    internal static readonly TimeSpan WriteBudget = TimeSpan.FromSeconds(2);

    public Task RecordLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        var minute = AccountActivityClaims.MinuteOf(clock.UtcNow);
        claims.Prime(userId, minute);
        return WriteAsync(userId, minute, isLogin: true, cancellationToken);
    }

    public Task RecordRequestAsync(Guid userId, CancellationToken cancellationToken)
    {
        var minute = AccountActivityClaims.MinuteOf(clock.UtcNow);
        return claims.TryClaim(userId, minute)
            ? WriteAsync(userId, minute, isLogin: false, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task WriteAsync(Guid userId, DateTimeOffset minute, bool isLogin, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(WriteBudget);
        try
        {
            await (isLogin
                ? db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO public.account_activity AS a (job_seeker_id, last_login_at, last_active_at)
                    SELECT js.id, {minute}, {minute} FROM public.job_seekers js WHERE js.user_id = {userId}
                    ON CONFLICT (job_seeker_id) DO UPDATE SET
                        last_login_at = GREATEST(a.last_login_at, EXCLUDED.last_login_at),
                        last_active_at = GREATEST(a.last_active_at, EXCLUDED.last_active_at)
                    """,
                    budget.Token)
                : db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO public.account_activity AS a (job_seeker_id, last_active_at)
                    SELECT js.id, {minute} FROM public.job_seekers js WHERE js.user_id = {userId}
                    ON CONFLICT (job_seeker_id) DO UPDATE SET last_active_at = EXCLUDED.last_active_at
                        WHERE a.last_active_at < EXCLUDED.last_active_at
                    """,
                    budget.Token));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            LogProfileGone(logger, userId);
        }
        catch (DbException ex)
        {
            LogWriteFailed(logger, userId, ex.GetType().Name, (ex as PostgresException)?.SqlState);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogWriteAbandoned(logger, userId, WriteBudget.TotalSeconds);
        }
    }

    [LoggerMessage(EventId = 2701, Level = LogLevel.Warning,
        Message = "Account activity was not recorded for user {UserId}: {ExceptionType} (SQLSTATE {SqlState})")]
    private static partial void LogWriteFailed(ILogger logger, Guid userId, string exceptionType, string? sqlState);

    [LoggerMessage(EventId = 2702, Level = LogLevel.Debug,
        Message = "Account activity was not recorded for user {UserId}: the profile was erased during the write")]
    private static partial void LogProfileGone(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2703, Level = LogLevel.Warning,
        Message = "Account activity was not recorded for user {UserId}: the write exceeded its {BudgetSeconds} s budget")]
    private static partial void LogWriteAbandoned(ILogger logger, Guid userId, double budgetSeconds);
}
