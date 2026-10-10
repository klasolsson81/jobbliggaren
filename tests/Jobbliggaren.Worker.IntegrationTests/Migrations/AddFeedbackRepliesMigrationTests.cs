using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #2069 — the reply table is additive: it appears empty, takes a reply as the reply handler saves it, and leaves
/// every other column and the existing feedback untouched across Up, Down and a second Up.
/// </summary>
public sealed class AddFeedbackRepliesMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261010163145_AddFeedbackReplies";
    private const string PreviousMigration = "20261008101701_AddFeedbackScreenshots";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(Ct);
        _appConnectionString = await TestDatabaseProvisioner.ProvisionAndGetAppConnectionStringAsync(
            _postgres.GetConnectionString(), ct: Ct);
        await using var superuser = new NpgsqlConnection(_postgres.GetConnectionString());
        await superuser.OpenAsync(Ct);
        await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
        await extension.ExecuteNonQueryAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task MigrateAsync_ShouldAddAnEmptyReplyTableAndKeepEverythingElse_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ReplyTableExistsAsync()).ShouldBeFalse();
        var submission = await SeedFeedbackAsync();
        var previousSchema = await ReadExistingSchemaAsync();

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertReplySchemaAsync();
        (await ReadExistingSchemaAsync()).ShouldBe(previousSchema);
        await using (var atUp = NewAppContext())
        {
            (await atUp.FeedbackReplies.AsNoTracking().CountAsync(Ct)).ShouldBe(0);
            var reply = FeedbackReply.Start(
                submission, 0, Guid.NewGuid(), FeedbackReplyText.Create("Tack för din feedback.\n\nåäö").Value, Now)
                .Value;
            atUp.FeedbackReplies.Add(reply);
            await atUp.SaveChangesAsync(Ct);
            reply.RecordAccepted(Now.AddSeconds(1)).IsSuccess.ShouldBeTrue();
            await atUp.SaveChangesAsync(Ct);
        }
        await using (var read = NewAppContext())
        {
            var stored = await read.FeedbackReplies.AsNoTracking().SingleAsync(Ct);
            stored.Text.Value.ShouldBe("Tack för din feedback.\n\nåäö");
            stored.State.ShouldBe(FeedbackReplyState.Accepted);
            stored.SubmittedAt.ShouldBe(submission.SubmittedAt);
            stored.JobSeekerId.ShouldBe(submission.JobSeekerId);
            stored.CompletedAt.ShouldBe(Now.AddSeconds(1));
        }

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await ReplyTableExistsAsync()).ShouldBeFalse();
        (await ReadExistingSchemaAsync()).ShouldBe(previousSchema);

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertReplySchemaAsync();
        await using var reapplied = NewAppContext();
        (await reapplied.FeedbackReplies.AsNoTracking().CountAsync(Ct)).ShouldBe(0);
        (await reapplied.FeedbackSubmissions.AsNoTracking().CountAsync(Ct)).ShouldBe(1);
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task<FeedbackSubmission> SeedFeedbackAsync()
    {
        var clock = new FixedClock(Now);
        var registered = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock).Value;
        var submission = FeedbackSubmission.Submit(registered.Id, Guid.NewGuid(), FeedbackPage.Jobs,
            FeedbackRating.Create(4).Value, FeedbackComment.Create("En syntetisk kommentar.").Value,
            ReportedClientContext.Empty, null, Now).Value;
        await using var db = NewAppContext();
        db.JobSeekers.Add(registered);
        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(FeedbackNotification.QueueFor(submission));
        await db.SaveChangesAsync(Ct);
        return submission;
    }

    private async Task<bool> ReplyTableExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.feedback_replies') IS NOT NULL", connection);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<List<string>> ReadExistingSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || '|' || column_name || '|' || data_type || '|' || is_nullable
                   || '|' || coalesce(character_maximum_length::text, '') || '|' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name <> 'feedback_replies'
            ORDER BY table_name, column_name
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task AssertReplySchemaAsync()
    {
        (await ReplyTableExistsAsync()).ShouldBeTrue();
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using (var command = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable, coalesce(character_maximum_length, 0)
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'feedback_replies'
            ORDER BY column_name
            """, connection))
        {
            var columns = new List<ColumnShape>();
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
                columns.Add(new ColumnShape(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES", reader.GetInt32(3)));
            columns.ShouldBe([
                new("body", "character varying", false, FeedbackReplyText.MaxLength),
                new("completed_at", "timestamp with time zone", true, 0),
                new("created_at", "timestamp with time zone", false, 0),
                new("id", "uuid", false, 0),
                new("job_seeker_id", "uuid", false, 0),
                new("reply_key", "uuid", false, 0),
                new("state", "character varying", false, 16),
                new("submission_id", "uuid", false, 0),
                new("submitted_at", "timestamp with time zone", false, 0),
            ]);
        }
        await using var indexCommand = new NpgsqlCommand(
            """
            SELECT i.indisprimary, i.indisunique, array_agg(a.attname::text ORDER BY k.ordinality)
            FROM pg_index i
            JOIN pg_class t ON t.oid = i.indrelid
            JOIN pg_namespace ns ON ns.oid = t.relnamespace
            JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum, ordinality) ON true
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE ns.nspname = 'public' AND t.relname = 'feedback_replies'
                  AND i.indisvalid AND i.indisready AND i.indpred IS NULL
            GROUP BY i.indexrelid, i.indisprimary, i.indisunique
            """, connection);
        var indexes = new List<IndexShape>();
        await using var indexReader = await indexCommand.ExecuteReaderAsync(Ct);
        while (await indexReader.ReadAsync(Ct))
            indexes.Add(new IndexShape(indexReader.GetBoolean(0), indexReader.GetBoolean(1),
                indexReader.GetFieldValue<string[]>(2)));
        indexes.Count.ShouldBe(3);
        indexes.Where(index => index.Primary && index.Unique).ShouldHaveSingleItem().Columns.ShouldBe(["id"]);
        indexes.Where(index => !index.Primary && index.Unique).ShouldHaveSingleItem().Columns
            .ShouldBe(["submission_id", "reply_key"]);
        indexes.Where(index => !index.Primary && !index.Unique).ShouldHaveSingleItem().Columns.ShouldBe(["job_seeker_id"]);
    }

    private sealed record ColumnShape(string Name, string DataType, bool IsNullable, int MaxLength);
    private sealed record IndexShape(bool Primary, bool Unique, string[] Columns);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
