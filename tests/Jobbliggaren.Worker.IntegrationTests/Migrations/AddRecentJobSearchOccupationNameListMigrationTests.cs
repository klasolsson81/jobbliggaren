using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
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
/// #2199 PR 2 — <c>recent_job_searches.occupation_name_list</c> is additive on a populated table:
/// a <c>text[] NOT NULL DEFAULT '{}'</c>, every existing row reading an empty list, an INSERT that
/// omits the column (the previous binary's form) still succeeding, and Down putting the schema back,
/// across Up, Down and a second Up. Run as <c>jobbliggaren_app</c>, the role production migrates with.
///
/// <para>
/// The row seeded at the previous schema is what <c>RecentJobSearch.Capture</c> wrote before this
/// migration: a name-free search with the hash <see cref="FilterHashCalculator"/> computed for it.
/// It is inserted with SQL because the current model already maps the column, which the previous
/// schema does not have.
/// </para>
/// </summary>
public sealed class AddRecentJobSearchOccupationNameListMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261025090206_AddRecentJobSearchOccupationNameList";
    private const string PreviousMigration = "20261024143837_AddDmarcAggregateReports";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
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
    public async Task MigrateAsync_ShouldAddAnEmptyListColumnToExistingRows_KeepingTheirHash_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await OccupationNameColumnAsync()).ShouldBeEmpty();

        var criteria = SearchCriteria.Create(
            occupationGroup: ["DJh5_yyF_hEM"], occupationName: [], municipality: ["AvNB_uwa_6n6"], region: null,
            employmentType: null, worktimeExtent: null, employer: null,
            remote: false, q: "projektledare", sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var seekerId = await SeedSeekerAsync();
        var rowId = Guid.NewGuid();
        await InsertPreviousSchemaRowAsync(rowId, seekerId, criteria);

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync(rowId);

        // The previous binary's INSERT, which names no occupation_name_list, still lands.
        var lateRowId = Guid.NewGuid();
        await InsertPreviousSchemaRowAsync(lateRowId, seekerId, SearchCriteria.Create(
            occupationGroup: ["DJh5_yyF_hEM"], occupationName: [], municipality: null, region: null,
            employmentType: null, worktimeExtent: null, employer: null,
            remote: false, q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value);
        (await OccupationNameValuesAsync(lateRowId)).ShouldBe(["{}"]);

        // Through the current model the existing row is the same search: no name, and the hash the
        // current calculator computes for it, so an identical new search bumps it.
        await using (var read = NewAppContext())
        {
            var row = await read.RecentJobSearches.AsNoTracking()
                .SingleAsync(r => r.Id == new RecentJobSearchId(rowId), Ct);
            row.OccupationName.ShouldBeEmpty();
            row.FilterHash.ShouldBe(FilterHashCalculator.Compute(criteria));
        }

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await OccupationNameColumnAsync()).ShouldBeEmpty();
        (await CountAsync(rowId)).ShouldBe(1, "Down keeps the row and drops only the column");

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync(rowId);
    }

    private async Task AssertUpAsync(Guid rowId)
    {
        (await OccupationNameColumnAsync()).ShouldBe(["ARRAY|NO|ARRAY[]::text[]"]);
        (await OccupationNameValuesAsync(rowId)).ShouldBe(["{}"], "an existing row reads an empty list");
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task<Guid> SeedSeekerAsync()
    {
        var clock = new FixedClock(Now);
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock).Value;
        await using var db = NewAppContext();
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(Ct);
        return seeker.Id.Value;
    }

    private async Task InsertPreviousSchemaRowAsync(Guid rowId, Guid seekerId, SearchCriteria criteria)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO recent_job_searches
              (id, job_seeker_id, filter_hash, q, occupation_group_list, municipality_list, region_list,
               employment_type_list, worktime_extent_list, employer_list, remote, sort_by,
               last_viewed_at, last_seen_count, created_at)
            VALUES
              (@id, @seeker, @hash, @q, @group, @municipality, @region, @employment, @worktime, @employer,
               @remote, @sort, @now, 0, @now)
            """, connection);
        command.Parameters.AddWithValue("id", rowId);
        command.Parameters.AddWithValue("seeker", seekerId);
        command.Parameters.AddWithValue("hash", FilterHashCalculator.Compute(criteria));
        command.Parameters.AddWithValue("q", (object?)criteria.Q ?? DBNull.Value);
        command.Parameters.AddWithValue("group", criteria.OccupationGroup.ToArray());
        command.Parameters.AddWithValue("municipality", criteria.Municipality.ToArray());
        command.Parameters.AddWithValue("region", criteria.Region.ToArray());
        command.Parameters.AddWithValue("employment", criteria.EmploymentType.ToArray());
        command.Parameters.AddWithValue("worktime", criteria.WorktimeExtent.ToArray());
        command.Parameters.AddWithValue("employer", criteria.Employer.ToArray());
        command.Parameters.AddWithValue("remote", criteria.Remote);
        command.Parameters.AddWithValue("sort", (int)criteria.SortBy);
        command.Parameters.AddWithValue("now", Now);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<string>> OccupationNameColumnAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT data_type || '|' || is_nullable || '|' || coalesce(column_default, 'none')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'recent_job_searches'
              AND column_name = 'occupation_name_list'
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task<List<string>> OccupationNameValuesAsync(Guid rowId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT occupation_name_list::text FROM recent_job_searches WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", rowId);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            values.Add(reader.GetString(0));
        return values;
    }

    private async Task<long> CountAsync(Guid rowId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM recent_job_searches WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", rowId);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
