using Jobbliggaren.Domain.JobAds;
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
/// #2191 — <c>job_ads.application_url</c> is additive: a nullable <c>varchar(2000)</c> with no default, every existing row
/// reading NULL until the nightly sync writes it, and Down puts the schema back, across Up, Down and a second Up. Run as
/// <c>jobbliggaren_app</c>, the role production migrates with.
///
/// <para>
/// The row seeded at the previous schema is an imported Active ad, the shape <c>JobAd.Import</c> wrote before this
/// migration. It is inserted with SQL because the current model already maps the column, which the previous schema does
/// not have.
/// </para>
/// </summary>
public sealed class AddJobAdApplicationUrlMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261019094325_AddJobAdApplicationUrl";
    private const string PreviousMigration = "20261018164310_AddCompanyWatchCriterionAdHits";
    private static readonly DateTimeOffset Published = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
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
    public async Task MigrateAsync_ShouldAddANullableColumnReadingNullLeavingEveryOtherColumnAsItWas_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ApplicationUrlColumnAsync()).ShouldBeEmpty();
        var adId = Guid.NewGuid();
        await SeedImportedAdAsync(adId);
        var previousSchema = await ReadJobAdsSchemaWithoutTheColumnAsync();

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync(adId);
        (await ReadJobAdsSchemaWithoutTheColumnAsync()).ShouldBe(previousSchema);

        // The model writes and reads the column as the app role, through the value object's conversion.
        var employerPage = ApplicationUrl.FromSource("https://karriar.example.se/jobb/4567890")!;
        await using (var writer = NewAppContext())
        {
            await writer.JobAds.Where(j => j.Id == new JobAdId(adId))
                .ExecuteUpdateAsync(set => set.SetProperty(j => j.ApplicationUrl, employerPage), Ct);
        }
        await using (var read = NewAppContext())
        {
            (await read.JobAds.AsNoTracking().Where(j => j.Id == new JobAdId(adId))
                    .Select(j => j.ApplicationUrl).SingleAsync(Ct))
                .ShouldBe(employerPage);
        }

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await ApplicationUrlColumnAsync()).ShouldBeEmpty();
        (await ReadJobAdsSchemaWithoutTheColumnAsync()).ShouldBe(previousSchema);
        (await CountAsync(adId)).ShouldBe(1, "Down keeps the row and drops only the column");

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync(adId);
    }

    private async Task AssertUpAsync(Guid adId)
    {
        (await ApplicationUrlColumnAsync()).ShouldBe(["character varying|YES|2000|none"]);
        (await ApplicationUrlValuesAsync(adId)).ShouldBe([null], "no data step: the existing row reads NULL until a sync writes it");
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task SeedImportedAdAsync(Guid adId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO job_ads (id, title, description, url, company_name, source, status, published_at, created_at,
                                 remote, external_source, external_id)
            VALUES (@id, 'Integration developer', 'Vi söker en integrationsutvecklare.',
                    'https://arbetsformedlingen.se/platsbanken/annonser/30123456', 'Exempel AB', 'Platsbanken', 'Active',
                    @published, @published, false, 'Platsbanken', '30123456')
            """, connection);
        command.Parameters.AddWithValue("id", adId);
        command.Parameters.AddWithValue("published", Published);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<string>> ApplicationUrlColumnAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT data_type || '|' || is_nullable || '|' || coalesce(character_maximum_length::text, '')
                   || '|' || coalesce(column_default, 'none')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'job_ads' AND column_name = 'application_url'
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task<List<string?>> ApplicationUrlValuesAsync(Guid adId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT application_url FROM job_ads WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", adId);
        var values = new List<string?>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        return values;
    }

    private async Task<long> CountAsync(Guid adId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM job_ads WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", adId);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<List<string>> ReadJobAdsSchemaWithoutTheColumnAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT column_name || '|' || data_type || '|' || is_nullable
                   || '|' || coalesce(character_maximum_length::text, '') || '|' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'job_ads' AND column_name <> 'application_url'
            ORDER BY column_name
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }
}
