using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Feedback;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

public sealed class AddFeedbackScreenshotsMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261008101701_AddFeedbackScreenshots";
    private const string PreviousMigration = "20261007215002_AddFeedback";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
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
    public async Task MigrateAsync_ShouldPreserveFeedbackAndDiscardOnlyScreenshots_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.ShouldContain(ThisMigration);
        migrations.ShouldContain(PreviousMigration);
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ScreenshotTableExistsAsync()).ShouldBeFalse();

        var submission = await SeedFeedbackAsync();
        var previousSchema = await ReadExistingSchemaAsync();
        var previousData = await ReadFeedbackDataAsync(submission.Id);

        await migrator.MigrateAsync(ThisMigration, Ct);

        (await db.Database.GetAppliedMigrationsAsync(Ct)).Last().ShouldBe(ThisMigration);
        await AssertScreenshotSchemaAsync();
        (await ReadExistingSchemaAsync()).ShouldBe(previousSchema);
        (await ReadFeedbackDataAsync(submission.Id)).ShouldBe(previousData);
        await using (var atUp = NewAppContext())
            (await atUp.FeedbackScreenshots.AsNoTracking().CountAsync(Ct)).ShouldBe(0);
        var screenshot = await SeedScreenshotAsync(submission);
        await using (var atUp = NewAppContext())
        {
            var stored = await atUp.FeedbackScreenshots.AsNoTracking().SingleAsync(Ct);
            stored.Id.ShouldBe(screenshot.Id);
            stored.SubmissionId.ShouldBe(submission.Id);
            stored.JobSeekerId.ShouldBe(submission.JobSeekerId);
            stored.SubmittedAt.ShouldBe(submission.SubmittedAt);
            stored.Width.ShouldBe(screenshot.Width);
            stored.Height.ShouldBe(screenshot.Height);
            stored.Content.ToArray().ShouldBe(screenshot.Content.ToArray());
        }

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await ScreenshotTableExistsAsync()).ShouldBeFalse();
        var rolledBackHistory = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();
        rolledBackHistory.ShouldNotContain(ThisMigration);
        rolledBackHistory.Last().ShouldBe(PreviousMigration);
        (await ReadExistingSchemaAsync()).ShouldBe(previousSchema);
        (await ReadFeedbackDataAsync(submission.Id)).ShouldBe(previousData);

        await migrator.MigrateAsync(ThisMigration, Ct);

        (await db.Database.GetAppliedMigrationsAsync(Ct)).Last().ShouldBe(ThisMigration);
        await AssertScreenshotSchemaAsync();
        (await ReadExistingSchemaAsync()).ShouldBe(previousSchema);
        (await ReadFeedbackDataAsync(submission.Id)).ShouldBe(previousData);
        await using var reapplied = NewAppContext();
        (await reapplied.FeedbackScreenshots.AsNoTracking().CountAsync(Ct)).ShouldBe(0);
        (await reapplied.FeedbackSubmissions.AsNoTracking().CountAsync(Ct)).ShouldBe(1);
        (await reapplied.FeedbackNotifications.AsNoTracking().CountAsync(Ct)).ShouldBe(1);
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task<FeedbackSubmission> SeedFeedbackAsync()
    {
        var clock = new FixedClock(Now);
        var registered = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock);
        registered.IsSuccess.ShouldBeTrue();
        var rating = FeedbackRating.Create(4);
        rating.IsSuccess.ShouldBeTrue();
        var comment = FeedbackComment.Create("En syntetisk kommentar åäö.");
        comment.IsSuccess.ShouldBeTrue();
        var submitted = FeedbackSubmission.Submit(registered.Value.Id, Guid.NewGuid(), FeedbackPage.Jobs,
            rating.Value, comment.Value, ReportedClientContext.Empty, null, Now);
        submitted.IsSuccess.ShouldBeTrue();
        var notice = FeedbackNotification.QueueFor(submitted.Value);
        notice.SubmissionId.ShouldBe(submitted.Value.Id);

        await using var db = NewAppContext();
        db.JobSeekers.Add(registered.Value);
        db.FeedbackSubmissions.Add(submitted.Value);
        db.FeedbackNotifications.Add(notice);
        await db.SaveChangesAsync(Ct);
        (await db.FeedbackSubmissions.AsNoTracking().CountAsync(Ct)).ShouldBe(1);
        (await db.FeedbackNotifications.AsNoTracking().CountAsync(Ct)).ShouldBe(1);
        return submitted.Value;
    }

    private async Task<FeedbackScreenshot> SeedScreenshotAsync(FeedbackSubmission submission)
    {
        using var source = new Image<Rgba32>(2, 3, new Rgba32(20, 40, 60, 255));
        using var png = new MemoryStream();
        await source.SaveAsync(png, new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
        }, Ct);
        using var normalizer = new FeedbackScreenshotNormalizer();
        var normalized = await normalizer.NormalizeAsync(png.ToArray(), Ct);
        normalized.IsSuccess.ShouldBeTrue();
        normalized.Value.Width.ShouldBe(2);
        normalized.Value.Height.ShouldBe(3);
        normalized.Value.Content.Length.ShouldBeLessThanOrEqualTo(FeedbackScreenshot.MaxContentBytes);
        using var decoded = Image.Load<Rgba32>(normalized.Value.Content.Span);
        decoded.Frames.Count.ShouldBe(1);
        decoded.Width.ShouldBe(normalized.Value.Width);
        decoded.Height.ShouldBe(normalized.Value.Height);
        decoded.Metadata.GetPngMetadata().ColorType.ShouldBe(PngColorType.RgbWithAlpha);
        decoded.Metadata.GetPngMetadata().BitDepth.ShouldBe(PngBitDepth.Bit8);
        decoded[0, 0].ShouldBe(source[0, 0]);
        var attached = FeedbackScreenshot.AttachTo(submission,
            normalized.Value.Content, normalized.Value.Width, normalized.Value.Height);
        attached.IsSuccess.ShouldBeTrue();
        await using var db = NewAppContext();
        db.FeedbackScreenshots.Add(attached.Value);
        await db.SaveChangesAsync(Ct);
        return attached.Value;
    }

    private async Task<bool> ScreenshotTableExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.feedback_screenshots') IS NOT NULL", connection);
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
            WHERE table_schema = 'public' AND table_name <> 'feedback_screenshots'
            ORDER BY table_name, column_name
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task<(string Submission, string Notice, string Owner)> ReadFeedbackDataAsync(
        FeedbackSubmissionId submissionId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT to_jsonb(s)::text, to_jsonb(n)::text, to_jsonb(o)::text
            FROM public.feedback_submissions s
            JOIN public.feedback_notifications n ON n.submission_id = s.id
            JOIN public.job_seekers o ON o.id = s.job_seeker_id
            WHERE s.id = @submission_id
            """, connection);
        command.Parameters.AddWithValue("submission_id", submissionId.Value);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        var data = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        (await reader.ReadAsync(Ct)).ShouldBeFalse();
        return data;
    }

    private async Task AssertScreenshotSchemaAsync()
    {
        (await ScreenshotTableExistsAsync()).ShouldBeTrue();
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using (var command = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'feedback_screenshots'
            ORDER BY column_name
            """, connection))
        {
            var columns = new List<ColumnShape>();
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
                columns.Add(new ColumnShape(reader.GetString(0), reader.GetString(1), reader.GetString(2) == "YES"));
            columns.ShouldBe([
                new("content", "bytea", false),
                new("height", "integer", false),
                new("id", "uuid", false),
                new("job_seeker_id", "uuid", false),
                new("submission_id", "uuid", false),
                new("submitted_at", "timestamp with time zone", false),
                new("width", "integer", false),
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
            WHERE ns.nspname = 'public' AND t.relname = 'feedback_screenshots'
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
        indexes.Where(index => !index.Primary && index.Unique).ShouldHaveSingleItem().Columns.ShouldBe(["submission_id"]);
        indexes.Where(index => !index.Primary && !index.Unique).ShouldHaveSingleItem().Columns.ShouldBe(["job_seeker_id"]);
    }

    private sealed record ColumnShape(string Name, string DataType, bool IsNullable);
    private sealed record IndexShape(bool Primary, bool Unique, string[] Columns);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
