using System.Globalization;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Jobbliggaren.Infrastructure.Email.Counting;

/// <summary>
/// #1981 PR 3 (ADR 0180) — counts one attempt to hand a mail to the provider. <see cref="RecordAsync"/> is the only
/// entry, and it cannot throw: whatever the write does — fail, hang, ignore its token — the send it follows keeps its
/// outcome, which is what the claim-then-send callers decide on (security-auditor 5d, ADR 0124). Its own timer bounds
/// it at <see cref="Budget"/>, so the bound holds whether or not the write honours its token. The Warning therefore
/// means "not confirmed within the budget": an abandoned write may still commit.
/// <para>
/// The signature carries the kind and the outcome and nothing else — no recipient, no time, no text and no caller
/// token — and must stay so: the day is taken from the server clock inside the write (ADR 0180).
/// </para>
/// </summary>
internal abstract partial class EmailSendTally(ILogger logger)
{
    /// <summary>
    /// The longest a count may hold up the send it follows (ADR 0180, ADR 0045 class (c)). It sits inside the delivery
    /// spine, between the provider's answer and the caller's bookkeeping.
    /// </summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(400);

    private const string NoSqlState = "-";

    public async ValueTask RecordAsync(EmailKind kind, EmailSendOutcome outcome)
    {
        var budget = new CancellationTokenSource(Budget);
        Task? write = null;
        try
        {
            write = WriteAsync(kind, outcome, budget.Token);
            await write.WaitAsync(Budget);
        }
        catch (Exception ex)
        {
            // No filter: nothing the caller holds reaches this method, so every exception here is the count's own.
            // The type name and a SQLSTATE only, never the exception or its message.
            LogNotConfirmed(
                logger,
                TokenOrNumber(kind),
                outcome,
                ex.GetType().Name,
                (ex as PostgresException)?.SqlState ?? NoSqlState);
        }
        finally
        {
            if (write is null || write.IsCompleted)
            {
                budget.Dispose();
            }
            else
            {
                // An abandoned write keeps running until its own token fires; the source is disposed after it, and its
                // exception is observed so that it never surfaces as an unobserved task exception.
                _ = write.ContinueWith(
                    static (abandoned, state) =>
                    {
                        _ = abandoned.Exception;
                        ((CancellationTokenSource)state!).Dispose();
                    },
                    budget,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    /// <summary>Adds one attempt to today's count for the kind and the outcome.</summary>
    protected abstract Task WriteAsync(EmailKind kind, EmailSendOutcome outcome, CancellationToken cancellationToken);

    private static string TokenOrNumber(EmailKind kind) =>
        Enum.IsDefined(kind) ? EmailKinds.TokenOf(kind) : ((int)kind).ToString(CultureInfo.InvariantCulture);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning,
        Message = "[EmailSendTally] A {EmailKind} send with outcome {Outcome} was not confirmed as counted: "
            + "{ExceptionType} (SQLSTATE {SqlState})")]
    private static partial void LogNotConfirmed(
        ILogger logger, string emailKind, EmailSendOutcome outcome, string exceptionType, string sqlState);
}
