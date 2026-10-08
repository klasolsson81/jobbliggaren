using System.Data;
using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Admin.Feedback;

/// <summary>
/// The per-page rating summary (#1979). Each user's LATEST rating per page inside the window is
/// counted once (<c>DISTINCT ON</c>), so a re-rating replaces the earlier one instead of adding to
/// it; every submission in the window counts toward <c>submissions</c>, rated or not.
/// </summary>
internal sealed class SqlFeedbackRatingSummaryReader(AppDbContext db) : IFeedbackRatingSummaryReader
{
    private const int CommandTimeoutSeconds = 30;

    private const string Sql = """
        WITH latest AS (
            SELECT DISTINCT ON (job_seeker_id, page_key) page_key, rating
            FROM feedback_submissions
            WHERE submitted_at >= @since AND rating IS NOT NULL
            ORDER BY job_seeker_id, page_key, submitted_at DESC, id DESC
        ),
        submitted AS (
            SELECT page_key, count(*)::int AS submissions
            FROM feedback_submissions
            WHERE submitted_at >= @since
            GROUP BY page_key
        )
        SELECT s.page_key,
               s.submissions,
               count(l.rating)::int AS raters,
               count(*) FILTER (WHERE l.rating = 1)::int AS rated1,
               count(*) FILTER (WHERE l.rating = 2)::int AS rated2,
               count(*) FILTER (WHERE l.rating = 3)::int AS rated3,
               count(*) FILTER (WHERE l.rating = 4)::int AS rated4,
               count(*) FILTER (WHERE l.rating = 5)::int AS rated5,
               round(avg(l.rating), 2) AS mean
        FROM submitted s
        LEFT JOIN latest l ON l.page_key = s.page_key
        GROUP BY s.page_key, s.submissions
        ORDER BY s.page_key
        """;

    public async Task<IReadOnlyList<FeedbackPageSummary>> ReadAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(Sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@since", NpgsqlDbType.TimestampTz, since.UtcDateTime);

        var pages = new List<FeedbackPageSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pages.Add(new FeedbackPageSummary(
                PageKey: reader.GetString(0),
                Submissions: reader.GetInt32(1),
                Raters: reader.GetInt32(2),
                Rated1: reader.GetInt32(3),
                Rated2: reader.GetInt32(4),
                Rated3: reader.GetInt32(5),
                Rated4: reader.GetInt32(6),
                Rated5: reader.GetInt32(7),
                Mean: reader.IsDBNull(8) ? null : reader.GetDecimal(8)));
        }

        return pages;
    }
}
