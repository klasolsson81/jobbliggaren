using System.Diagnostics;
using System.Globalization;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Migrate;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// Invokes the built production executable against this class's private PostgreSQL container.
/// Refusals must preserve both Identity DDL and the schema grants bootstrap would otherwise issue.
/// Unknown history rows name a newer image's migrate step and use EF's own history writer.
/// The unreadable-history case explicitly represents operator damage and asserts only safe denial.
/// </summary>
public sealed class IdentityBootstrapCliTests : IAsyncLifetime
{
    private const string SeededByNewerImage = "20991231235959_SeededByNewerImage";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(Ct);
        // Phase A creates the actual role identities and public grants; Identity is deliberately
        // absent so a refused initial invocation cannot hide a CREATE SCHEMA side effect.
        await TestDatabaseProvisioner.ProvisionAndGetAppConnectionStringAsync(
            _postgres.GetConnectionString(), includeIdentitySchema: false, ct: Ct);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Bootstrap_ShouldCreateIdentityAndGrantAccess_WhenExplicitInitialHistoryIsEmpty()
    {
        (await ReadIdentitySurfaceAsync()).ShouldBeEmpty();

        (await InvokeAsync(["bootstrap", "--initial"])).ShouldBe(0);

        await AssertCompleteCandidateAsync();
        (await HasAppSchemaAccessAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Bootstrap_ShouldRejectMalformedArguments_BeforeReadingCredentialsOrCreatingSchema()
    {
        var compiled = CompiledManifest();
        string[][] malformed =
        [
            ["bootstrap"],
            ["bootstrap", "--expect-history"],
            ["bootstrap", "--expect-history", "", "--expect-migrations"],
            ["bootstrap", "--initial", "extra"],
            ["bootstrap", "--expect-history", "", "--expect-migrations", ""],
            ["bootstrap", "--expect-history", "", "--expect-migrations", $"{compiled[0]},{compiled[0]}"],
            ["bootstrap", "--expect-history", "", "--expect-migrations", $"{compiled[1]},{compiled[0]}"],
            ["bootstrap", "--expect-history", "", "--expect-migrations", $"{compiled[0]}\n"],
        ];

        foreach (var arguments in malformed)
        {
            var result = await InvokeDetailedAsync(arguments, poisonCredentialFile: true);
            result.ExitCode.ShouldNotBe(0);
            result.UsageReported.ShouldBeTrue();
            result.RuntimeFailureReported.ShouldBeFalse();
            (await ReadIdentitySurfaceAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Bootstrap_ShouldLeaveSchemaAbsent_WhenApprovedSetDoesNotMatchCandidate()
    {
        var compiled = CompiledManifest();

        (await InvokeAsync(BoundArguments([], compiled[..^1]))).ShouldNotBe(0);

        (await ReadIdentitySurfaceAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bootstrap_ShouldApplyExactlyApprovedAddition_WhenHistoryMatchesPredecessor()
    {
        var compiled = CompiledManifest();
        await StageIdentityAsync(compiled[^2]);
        await RevokeAppIdentityGrantsAsync();
        (await HasAppSchemaAccessAsync()).ShouldBeFalse();

        (await InvokeAsync(BoundArguments(compiled[..^1], [compiled[^1]]))).ShouldBe(0);

        await AssertCompleteCandidateAsync();
        (await HasAppSchemaAccessAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Bootstrap_ShouldResumeSameBoundCandidate_WhenAllApprovedMigrationsAlreadyApplied()
    {
        var compiled = CompiledManifest();
        await StageIdentityAsync(compiled[^1]);
        var historyBefore = await ReadAppliedHistoryAsync();
        await RevokeAppIdentityGrantsAsync();

        (await InvokeAsync(BoundArguments(compiled[..^1], [compiled[^1]]))).ShouldBe(0);

        (await ReadAppliedHistoryAsync()).ShouldBe(historyBefore);
        await AssertCompleteCandidateAsync();
        (await HasAppSchemaAccessAsync()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bootstrap_ShouldPreserveSchemaAndRevokedGrants_WhenActualPredecessorIsWrongOrPartial(
        bool partialHistory)
    {
        var compiled = CompiledManifest();
        await StageIdentityAsync(partialHistory ? compiled[0] : compiled[^2]);
        await RevokeAppIdentityGrantsAsync();
        var before = await ReadIdentitySurfaceAsync();
        var arguments = partialHistory
            ? BoundArguments(compiled[..^1], [compiled[^1]])
            : BoundArguments([], compiled);

        (await InvokeAsync(arguments)).ShouldNotBe(0);

        (await ReadIdentitySurfaceAsync()).ShouldBe(before);
        (await HasAppSchemaAccessAsync()).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bootstrap_ShouldPreserveSchemaAndRevokedGrants_WhenNewerImageWroteAheadOrDivergentHistory(
        bool candidateWasComplete)
    {
        var compiled = CompiledManifest();
        await StageIdentityAsync(candidateWasComplete ? compiled[^1] : compiled[^2]);
        await SeedNewerImageHistoryAsync();
        await RevokeAppIdentityGrantsAsync();
        var before = await ReadIdentitySurfaceAsync();

        (await InvokeAsync(BoundArguments(compiled[..^1], [compiled[^1]]))).ShouldNotBe(0);

        (await ReadIdentitySurfaceAsync()).ShouldBe(before);
        (await HasAppSchemaAccessAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Bootstrap_ShouldPreserveSchemaAndRevokedGrants_WhenOperatorDamageMakesHistoryUnreadable()
    {
        var compiled = CompiledManifest();
        await StageIdentityAsync(compiled[^2]);
        await RevokeAppIdentityGrantsAsync();
        // Unreachable through EF's writer: an operator renamed a required history column.
        // This test makes only the fail-closed assertion; corruption is never treated as first boot.
        await ExecuteAsync(
            "ALTER TABLE identity.\"__EFMigrationsHistory\" RENAME COLUMN migration_id TO operator_damaged_id");
        await using (var damaged = NewIdentityContext())
        {
            var unreadable = await Should.ThrowAsync<PostgresException>(
                () => damaged.Database.GetAppliedMigrationsAsync(Ct));
            unreadable.SqlState.ShouldBe(PostgresErrorCodes.UndefinedColumn);
        }
        var before = await ReadIdentitySurfaceAsync();

        (await InvokeAsync(BoundArguments(compiled[..^1], [compiled[^1]]))).ShouldNotBe(0);

        (await ReadIdentitySurfaceAsync()).ShouldBe(before);
        (await HasAppSchemaAccessAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Bootstrap_ShouldPreserveSchemaAndRevokedGrants_WhenInitialIntentMeetsExistingHistory()
    {
        await StageIdentityAsync(CompiledManifest()[0]);
        await RevokeAppIdentityGrantsAsync();
        var before = await ReadIdentitySurfaceAsync();

        (await InvokeAsync(["bootstrap", "--initial"])).ShouldNotBe(0);

        (await ReadIdentitySurfaceAsync()).ShouldBe(before);
        (await HasAppSchemaAccessAsync()).ShouldBeFalse();
    }

    private AppIdentityDbContext NewIdentityContext() =>
        new(MigrationsOptionsFactory.BuildIdentityOptions(_postgres.GetConnectionString()));

    private string[] CompiledManifest()
    {
        using var db = NewIdentityContext();
        var compiled = db.Database.GetMigrations().ToArray();
        compiled.Length.ShouldBeGreaterThan(1);
        return compiled;
    }

    private async Task StageIdentityAsync(string lastMigration)
    {
        foreach (var statement in PhaseASchemaGrants.IdentitySchema)
            await ExecuteAsync(statement.Sql);
        await using var db = NewIdentityContext();
        await db.GetService<IMigrator>().MigrateAsync(lastMigration, Ct);
        (await db.Database.GetAppliedMigrationsAsync(Ct)).Last().ShouldBe(lastMigration);
    }

    private async Task SeedNewerImageHistoryAsync()
    {
        await using var db = NewIdentityContext();
        var compiled = db.Database.GetMigrations().ToArray();
        compiled.ShouldNotContain(SeededByNewerImage);
        var pendingBefore = (await db.Database.GetPendingMigrationsAsync(Ct)).ToArray();
        var history = db.GetService<IHistoryRepository>();
        var insert = history.GetInsertScript(new HistoryRow(
            SeededByNewerImage,
            typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0"));
        await db.Database.ExecuteSqlRawAsync(insert, Ct);
        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldContain(SeededByNewerImage);
        // EF tolerates this newer-image shape, so its pending read alone cannot enforce the bound.
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBe(pendingBefore);
    }

    private async Task RevokeAppIdentityGrantsAsync()
    {
        await ExecuteAsync($"REVOKE ALL ON SCHEMA identity FROM {Roles.App}");
        await ExecuteAsync($"REVOKE ALL ON ALL TABLES IN SCHEMA identity FROM {Roles.App}");
        await ExecuteAsync($"REVOKE ALL ON ALL SEQUENCES IN SCHEMA identity FROM {Roles.App}");
        await ExecuteAsync($"ALTER DEFAULT PRIVILEGES IN SCHEMA identity REVOKE ALL ON TABLES FROM {Roles.App}");
        await ExecuteAsync($"ALTER DEFAULT PRIVILEGES IN SCHEMA identity REVOKE ALL ON SEQUENCES FROM {Roles.App}");
        (await HasAppSchemaAccessAsync()).ShouldBeFalse();
    }

    private async Task AssertCompleteCandidateAsync()
    {
        (await ReadAppliedHistoryAsync()).ShouldBe(CompiledManifest());
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT value FROM identity.account_security_epoch WHERE id = 1", connection);
        (await command.ExecuteScalarAsync(Ct)).ShouldBe(0L);
    }

    private async Task<string[]> ReadAppliedHistoryAsync()
    {
        await using var db = NewIdentityContext();
        return (await db.Database.GetAppliedMigrationsAsync(Ct)).ToArray();
    }

    private async Task<bool> HasAppSchemaAccessAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT has_schema_privilege(@role, 'identity', 'USAGE') AND has_schema_privilege(@role, 'identity', 'CREATE')",
            connection);
        command.Parameters.AddWithValue("role", Roles.App);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<string[]> ReadIdentitySurfaceAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT 'schema|' || nspname || '|' || pg_get_userbyid(nspowner) || '|' || coalesce(nspacl::text, '')
            FROM pg_namespace WHERE nspname = 'identity'
            UNION ALL
            SELECT 'relation|' || c.relname || '|' || c.relkind::text || '|' || pg_get_userbyid(c.relowner)
                   || '|' || coalesce(c.relacl::text, '')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'identity'
            UNION ALL
            SELECT 'column|' || table_name || '|' || column_name || '|' || data_type
                   || '|' || is_nullable || '|' || coalesce(column_default, '')
            FROM information_schema.columns WHERE table_schema = 'identity'
            UNION ALL
            SELECT 'default-acl|' || pg_get_userbyid(d.defaclrole) || '|' || d.defaclobjtype::text || '|' || d.defaclacl::text
            FROM pg_default_acl d JOIN pg_namespace n ON n.oid = d.defaclnamespace WHERE n.nspname = 'identity'
            ORDER BY 1
            """, connection);
        var surface = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
            while (await reader.ReadAsync(Ct))
                surface.Add(reader.GetString(0));

        await using var historyExists = new NpgsqlCommand(
            "SELECT to_regclass('identity.\"__EFMigrationsHistory\"') IS NOT NULL", connection);
        if ((bool)(await historyExists.ExecuteScalarAsync(Ct))!)
        {
            await using var history = new NpgsqlCommand(
                "SELECT to_jsonb(h)::text FROM identity.\"__EFMigrationsHistory\" h ORDER BY 1", connection);
            await using var rows = await history.ExecuteReaderAsync(Ct);
            while (await rows.ReadAsync(Ct))
                surface.Add($"history|{rows.GetString(0)}");
        }
        return surface.ToArray();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<int> InvokeAsync(string[] arguments) =>
        (await InvokeDetailedAsync(arguments)).ExitCode;

    private async Task<CliResult> InvokeDetailedAsync(string[] arguments, bool poisonCredentialFile = false)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "migrate-cli");
        var assembly = Path.Combine(directory, "Jobbliggaren.Migrate.dll");
        File.Exists(assembly).ShouldBeTrue("The project dependency must copy the real CLI runtime.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("MIGRATE_", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);

        var database = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString());
        start.Environment["MIGRATE_DB_HOST"] = database.Host;
        start.Environment["MIGRATE_DB_PORT"] = database.Port.ToString(CultureInfo.InvariantCulture);
        start.Environment["MIGRATE_DB_NAME"] = database.Database;
        start.Environment["MIGRATE_MASTER_USERNAME"] = database.Username;
        start.Environment["MIGRATE_MASTER_PASSWORD"] = database.Password;
        start.Environment["MIGRATE_SSL_MODE"] = nameof(SslMode.Disable);
        if (poisonCredentialFile)
            start.Environment["MIGRATE_MASTER_PASSWORD_FILE"] = Path.Combine(directory, "missing-test-credential");

        using var process = Process.Start(start).ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        try
        {
            await process.WaitForExitAsync(Ct);
            await Task.WhenAll(stdout, stderr);
            var output = await stdout + await stderr;
            // Structured event ids distinguish argument refusal from a credential-file exception;
            // both paths currently exit 1. No localized exception message or output is asserted.
            return new CliResult(process.ExitCode,
                output.Contains("Migrate[202]", StringComparison.Ordinal),
                output.Contains("Migrate[999]", StringComparison.Ordinal));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static string[] BoundArguments(IReadOnlyList<string> predecessor, IReadOnlyList<string> additions) =>
        ["bootstrap", "--expect-history", string.Join(',', predecessor), "--expect-migrations", string.Join(',', additions)];

    private sealed record CliResult(int ExitCode, bool UsageReported, bool RuntimeFailureReported);
}
