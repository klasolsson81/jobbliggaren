using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — synthetic reports for the DMARC storage tests, made the one way production makes them:
/// through <see cref="DmarcAggregateReport.Accept"/>. Every name is under <c>.example</c> or is our own domain, and
/// every address is from the documentation ranges (RFC 5737, RFC 3849); nothing is copied from a real report.
/// </summary>
internal static class DmarcTestReports
{
    public const long Epoch = 4_000_000_001;

    public static DmarcRecordCandidate Record(
        string sourceIp, long count, string dkim = "pass", string spf = "pass", string headerFrom = "jobbliggaren.se") =>
        new(sourceIp, count, headerFrom, dkim, spf);

    /// <summary>
    /// An accepted report received at <paramref name="receivedAt"/>, covering <paramref name="beginsAt"/> to
    /// <paramref name="endsAt"/> (the day before receipt by default), with a digest derived from
    /// <paramref name="content"/> so two calls with the same content are the same report byte for byte.
    /// </summary>
    public static DmarcAggregateReport Accepted(
        string reportId,
        DateTimeOffset receivedAt,
        IReadOnlyList<DmarcRecordCandidate> records,
        string reporter = "reporter.example",
        string? content = null,
        DateTimeOffset? beginsAt = null,
        DateTimeOffset? endsAt = null)
    {
        var ends = endsAt ?? receivedAt;
        var begins = beginsAt ?? ends.AddDays(-1);
        var candidate = new DmarcReportCandidate(
            reporter,
            reportId,
            DmarcIngestionLimits.PolicyDomain,
            begins.ToUnixTimeSeconds(),
            ends.ToUnixTimeSeconds(),
            records,
            SHA256.HashData(Encoding.UTF8.GetBytes(content ?? $"{reporter}|{reportId}")));

        return DmarcAggregateReport.Accept(candidate, receivedAt)
            .ShouldBeOfType<DmarcAcceptance.Accepted>("a test report must pass the acceptance bounds production applies")
            .Report;
    }

    public static SqlDmarcReportStore Store(AppDbContext db, ILogger<SqlDmarcReportStore>? logger = null) =>
        new(db, logger ?? NullLogger<SqlDmarcReportStore>.Instance);

    /// <summary>The four DMARC tables are this suite's alone, so each test starts and ends with them empty.</summary>
    public static async Task ClearAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM dmarc_report_rows; DELETE FROM dmarc_reports; DELETE FROM dmarc_mailbox_messages; "
            + "DELETE FROM dmarc_mailbox_state;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<string>> ReadStringsAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }
}

/// <summary>
/// One run's worth of the production store's calls, in the order the ingestion job makes them: the state row, the epoch,
/// then a claim per message before anything about it is stored.
/// </summary>
internal sealed class DmarcIngestionDriver(IDmarcReportStore store, long epoch = DmarcTestReports.Epoch)
{
    private long _nextPosition;

    public long Epoch => epoch;

    public async Task StartAsync(DateTimeOffset attemptAt)
    {
        var ct = TestContext.Current.CancellationToken;
        await store.EnsureStateAsync(attemptAt, ct);
        await store.EnterEpochAsync(epoch, ct);
        _nextPosition = (await store.ReadStateAsync(ct))!.Cursor!.Position + 1;
    }

    public async Task<long> ClaimAsync(DateTimeOffset receivedAt)
    {
        var position = _nextPosition++;
        await store.ClaimAsync(epoch, position, receivedAt, TestContext.Current.CancellationToken);
        return position;
    }

    public async Task<DmarcMessageOutcome> StoreAsync(DmarcAggregateReport report)
    {
        var position = await ClaimAsync(report.ReceivedAt);
        return await store.StoreAsync(epoch, position, report, TestContext.Current.CancellationToken);
    }
}
