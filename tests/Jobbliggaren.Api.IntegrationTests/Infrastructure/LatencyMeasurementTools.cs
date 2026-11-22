using System.Data.Common;
using System.Diagnostics;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// What the observe-only latency harnesses share (#1663 PR 2's <c>CriterionNewAdsLatencyMeasurement</c> and PR 3's
/// <c>NotificationFeedLatencyMeasurement</c>): warmed timing with a nearest-rank percentile, the statements one context
/// sends, and their plans. The durations go to test output and into a dated report; nothing here asserts a budget.
/// </summary>
internal static class LatencyMeasurementTools
{
    internal const int Iterations = 40;

    /// <summary>Five warm-up calls, then <paramref name="iterations"/> timed ones; p50 and p95 by nearest rank.</summary>
    internal static async Task<(double P50, double P95)> MeasureAsync(
        Func<Task> call, CancellationToken ct, int iterations = Iterations)
    {
        for (var i = 0; i < 5; i++)
            await call();

        var samples = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await call();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return (Percentile(samples, 0.50), Percentile(samples, 0.95));
    }

    internal static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    /// <summary>
    /// <c>EXPLAIN (ANALYZE, BUFFERS)</c> of a captured statement inside a transaction that is rolled back, so a write is
    /// measured without being kept. A read behaves as it would outside one.
    /// </summary>
    internal static async Task<List<string>> ExplainAsync(AppDbContext db, CapturedCommand statement, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS) " + statement.Sql, connection, transaction);
            explain.Parameters.AddRange(statement.Parameters.Select(p => p.Clone()).ToArray());
            var plan = new List<string>();
            await using (var reader = await explain.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    plan.Add(reader.GetString(0));
            }

            await transaction.RollbackAsync(ct);
            return plan;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

internal sealed record CapturedCommand(string Sql, NpgsqlParameter[] Parameters);

/// <summary>The statements EF sends for one context while the capture is open, read from its diagnostic events.</summary>
internal sealed class CommandCapture
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly DbContext _context;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<CapturedCommand> _commands = [];

    public CommandCapture(DbContext context)
    {
        _context = context;
        var all = DiagnosticListener.AllListeners.Subscribe(this);
        lock (_subscriptions)
            _subscriptions.Add(all);
    }

    public IReadOnlyList<CapturedCommand> Commands
    {
        get
        {
            lock (_commands)
                return [.. _commands];
        }
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name != DbLoggerCategory.Name)
            return;
        var subscription = listener.Subscribe(this);
        lock (_subscriptions)
            _subscriptions.Add(subscription);
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Key != RelationalEventId.CommandExecuting.Name
            || value.Value is not CommandEventData data
            || !ReferenceEquals(data.Context, _context))
            return;

        var parameters = data.Command.Parameters.Cast<DbParameter>()
            .Select(p => ((NpgsqlParameter)p).Clone())
            .ToArray();
        lock (_commands)
            _commands.Add(new CapturedCommand(data.Command.CommandText, parameters));
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
        }
    }
}
