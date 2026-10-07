using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Access;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Identity.Migrations;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #1976: migrate populated predecessor rows as the real app role; preserve the global security epoch
/// across re-runs and account erasure. Every test owns its PostgreSQL container and migration position.
/// Historical rows name the pre-migration passwordless writer. Used epochs come from the real access
/// transition; deletion candidates come from JobSeeker.SoftDelete and the hard-deleter's own predicate.
/// </summary>
public sealed class AddAccountAccessSuspensionMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261007074622_AddAccountAccessSuspension";
    private const string PreviousMigration = "20260925172152_NullPasswordHashes";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;
    private ServiceProvider? _identity;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(Ct);
        _appConnectionString = await TestDatabaseProvisioner.ProvisionAndGetAppConnectionStringAsync(
            _postgres.GetConnectionString(), includeIdentitySchema: true, ct: Ct);

        await using var superuser = new NpgsqlConnection(_postgres.GetConnectionString());
        await superuser.OpenAsync(Ct);
        await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
        await extension.ExecuteNonQueryAsync(Ct);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = _appConnectionString,
        }).Build();
        _identity = new ServiceCollection()
            .AddLogging()
            .AddScoped<ProtectedAccountTransaction>()
            .AddScoped<ProtectedAccountTransactionInterceptor>()
            .AddSingleton<IDbExceptionInspector, DbExceptionInspector>()
            .AddScoped(provider => NewAppContext(provider.GetRequiredService<ProtectedAccountTransactionInterceptor>()))
            .AddCoreIdentityForWorker(configuration)
            .AddScoped<SqlAccountAccess>()
            .AddScoped<IAccountAccessCoordinator>(provider => provider.GetRequiredService<SqlAccountAccess>())
            .AddScoped<IAccountAccessReader>(provider => provider.GetRequiredService<SqlAccountAccess>())
            .AddScoped<IAccountAccessWriter>(provider => provider.GetRequiredService<SqlAccountAccess>())
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_identity is not null)
            await _identity.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Up_PopulatedPredecessor_AddsActiveGenerationZeroAndExactlyOneEpoch()
    {
        await using var db = NewIdentityContext();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.ShouldContain(ThisMigration);
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        await migrator.MigrateAsync(PreviousMigration, Ct);
        var historicalId = await InsertPreMigrationPasswordlessAccountAsync();
        var predecessor = await ReadUnchangedUserColumnsAsync(historicalId);

        await migrator.MigrateAsync(ThisMigration, Ct);

        (await db.Database.GetAppliedMigrationsAsync(Ct)).Last().ShouldBe(ThisMigration);
        var migrated = await db.Users.AsNoTracking().SingleAsync(user => user.Id == historicalId, Ct);
        AssertGenerationZero(migrated);
        (await ReadUnchangedUserColumnsAsync(historicalId)).ShouldBe(predecessor);
        (await db.AccountSecurityEpochs.AsNoTracking().ToListAsync(Ct)).ShouldHaveSingleItem()
            .Value.ShouldBe(0);

        // Pin the current UserManager-backed writer as well as the historical row's backfill.
        var newId = await CreateCurrentPasswordlessAccountAsync();
        AssertGenerationZero(await db.Users.AsNoTracking().SingleAsync(user => user.Id == newId, Ct));

        var secondRow = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO identity.account_security_epoch (id, value) VALUES (2, 0)"));
        secondRow.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        var negativeEpoch = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            "UPDATE identity.account_security_epoch SET value = -1 WHERE id = 1"));
        negativeEpoch.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ReadEpochAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Down_UnusedEpoch_ReversesAndCanBeAppliedAgain()
    {
        await using var db = NewIdentityContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        var historicalId = await InsertPreMigrationPasswordlessAccountAsync();
        var predecessor = await ReadUnchangedUserColumnsAsync(historicalId);
        await migrator.MigrateAsync(ThisMigration, Ct);
        var guard = new AddAccountAccessSuspension().DownOperations[0].ShouldBeOfType<SqlOperation>();
        guard.SuppressTransaction.ShouldBeFalse();

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldNotContain(ThisMigration);
        (await ReadSchemaAsync()).ShouldNotContain(column => column.StartsWith(
            "account_security_epoch|", StringComparison.Ordinal));
        (await ReadSchemaAsync()).ShouldNotContain(column => column.StartsWith(
            "AspNetUsers|access_revision|", StringComparison.Ordinal));
        (await ReadUnchangedUserColumnsAsync(historicalId)).ShouldBe(predecessor);

        await migrator.MigrateAsync(ThisMigration, Ct);

        AssertGenerationZero(await db.Users.AsNoTracking().SingleAsync(user => user.Id == historicalId, Ct));
        (await db.AccountSecurityEpochs.AsNoTracking().ToListAsync(Ct)).ShouldHaveSingleItem()
            .Value.ShouldBe(0);
        (await ReadUnchangedUserColumnsAsync(historicalId)).ShouldBe(predecessor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Down_UsedEpochAfterTransitionedAccountsAreErased_RefusesAndPreservesHistory(
        bool operatorErasesLastAdmin)
    {
        await using var db = NewIdentityContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(ThisMigration, Ct);
        await using (var app = NewAppContext())
            await app.Database.MigrateAsync(Ct);

        var actorId = await CreateCurrentPasswordlessAccountAsync();
        var targetId = await CreateCurrentPasswordlessAccountAsync();
        await RegisterProfileAsync(actorId);
        var targetProfileId = await RegisterProfileAsync(targetId);
        await GrantBootstrapAdminAsync(actorId);

        await using (var scope = Identity.CreateAsyncScope())
        {
            var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var access = scope.ServiceProvider.GetRequiredService<SqlAccountAccess>();
            await using var transaction = await access.BeginAsync([actorId, targetId], lifecycle: true, Ct);
            var changed = await access.ChangeAsync(actorId, targetId, suspended: true, Ct);
            changed.IsSuccess.ShouldBeTrue();
            changed.Value.AccessRevision.ShouldBe(1);
            changed.Value.IsSuspended.ShouldBeTrue();
            await app.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }
        (await ReadEpochAsync()).ShouldBe(1);

        await EraseMatureAccountAsync(targetId, targetProfileId);
        (await db.Users.AsNoTracking().AnyAsync(user => user.AccessRevision != 0, Ct)).ShouldBeFalse();
        (await db.Users.AsNoTracking().CountAsync(Ct)).ShouldBe(1);

        if (operatorErasesLastAdmin)
        {
            // Deliberately unreachable through the current lifecycle writer, which refuses last-admin
            // removal: operator damage deletes all remaining users outside that protocol. This arm
            // asserts only that Down degrades safely when the invariant is broken.
            (await ExecuteAsync("DELETE FROM identity.\"AspNetUsers\"")).ShouldBe(1);
            (await db.Users.AsNoTracking().CountAsync(Ct)).ShouldBe(0);
        }

        var schemaBefore = await ReadSchemaAsync();
        var historyBefore = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();
        await migrator.MigrateAsync(ThisMigration, Ct);
        (await ReadEpochAsync()).ShouldBe(1);

        await AssertDownRefusedByEpochGuardAsync(migrator);
        (await ReadEpochAsync()).ShouldBe(1);
        (await ReadSchemaAsync()).ShouldBe(schemaBefore);
        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldBe(historyBefore);
        await using var fresh = NewIdentityContext();
        await fresh.GetService<IMigrator>().MigrateAsync(ThisMigration, Ct);
        (await ReadEpochAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Down_UnreachableMissingSingleton_RefusesWithoutChangingSchemaOrHistory()
    {
        await using var db = NewIdentityContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(ThisMigration, Ct);
        // Operator damage, never a production account transition: deletion of the mandatory singleton.
        (await ExecuteAsync("DELETE FROM identity.account_security_epoch WHERE id = 1")).ShouldBe(1);
        var schemaBefore = await ReadSchemaAsync();
        var historyBefore = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();

        await AssertDownRefusedByEpochGuardAsync(migrator);
        (await ReadSchemaAsync()).ShouldBe(schemaBefore);
        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldBe(historyBefore);
        (await db.AccountSecurityEpochs.AsNoTracking().CountAsync(Ct)).ShouldBe(0);
    }

    private ServiceProvider Identity => _identity ?? throw new InvalidOperationException("Not initialized.");
    private AppIdentityDbContext NewIdentityContext() =>
        new(MigrationsOptionsFactory.BuildIdentityOptions(_appConnectionString));
    private AppDbContext NewAppContext(ProtectedAccountTransactionInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    private static async Task AssertDownRefusedByEpochGuardAsync(IMigrator migrator)
    {
        var refused = (await Record.ExceptionAsync(() => migrator.MigrateAsync(PreviousMigration, Ct)))
            .ShouldNotBeNull("Down must fail because the security epoch cannot be reversed.");
        var diagnostic = $"Down exception chain:{Environment.NewLine}{refused}";
        var chain = new List<Exception>();
        for (Exception? current = refused; current is not null; current = current.InnerException)
            chain.Add(current);

        var postgresErrors = chain.OfType<PostgresException>().ToList();
        postgresErrors.Count.ShouldBe(1, diagnostic);
        postgresErrors[0].SqlState.ShouldBe(PostgresErrorCodes.ObjectNotInPrerequisiteState, diagnostic);
    }

    private static void AssertGenerationZero(ApplicationUser user)
    {
        user.IsSuspended.ShouldBeFalse();
        user.AccessRevision.ShouldBe(0);
        user.CredentialCutoff.ShouldBe(0);
        user.PasswordHash.ShouldBeNull();
        user.EmailConfirmed.ShouldBeTrue();
    }

    private async Task<Guid> CreateCurrentPasswordlessAccountAsync()
    {
        await using var scope = Identity.CreateAsyncScope();
        var userId = Guid.NewGuid();
        var access = scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>();
        await using var transaction = await access.BeginAsync([userId], lifecycle: false, Ct);
        var created = await ActivatorUtilities.CreateInstance<UserAccountService>(scope.ServiceProvider)
            .CreatePasswordlessUserAsync(userId, $"suspension-{userId:N}@example.com", Ct);
        created.IsSuccess.ShouldBeTrue();
        created.Value.ShouldBe(userId);
        await transaction.CommitAsync(Ct);
        return created.Value;
    }

    private async Task<JobSeekerId> RegisterProfileAsync(Guid userId)
    {
        var clock = new FixedClock(Now);
        var registered = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock);
        registered.IsSuccess.ShouldBeTrue();
        await using var db = NewAppContext();
        db.JobSeekers.Add(registered.Value);
        await db.SaveChangesAsync(Ct);
        return registered.Value.Id;
    }

    private async Task GrantBootstrapAdminAsync(Guid userId)
    {
        await using var scope = Identity.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>();
        await using var transaction = await access.BeginAsync([userId], lifecycle: true, Ct);
        (await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(userId, Ct))
            .ShouldNotBeNull().CanAuthenticate.ShouldBeTrue();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        (await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.GetUsersInRoleAsync(Roles.Admin)).ShouldBeEmpty();
        var user = (await users.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
        (await users.AddToRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
        await transaction.CommitAsync(Ct);
    }

    private async Task EraseMatureAccountAsync(Guid userId, JobSeekerId profileId)
    {
        await using var scope = Identity.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountAccessReader>();
        await using (var deletion = await coordinator.BeginAsync([userId], lifecycle: true, Ct))
        {
            (await scope.ServiceProvider.GetRequiredService<IAccountAccessWriter>().CanRemoveAccessAsync(userId, Ct))
                .ShouldBeTrue();
            var profile = await app.JobSeekers.SingleAsync(seeker => seeker.Id == profileId, Ct);
            var deletedAt = Now.AddDays(-32);
            profile.SoftDelete(new FixedClock(deletedAt));
            profile.DeletedAt.ShouldBe(deletedAt);
            await app.SaveChangesAsync(Ct);
            await deletion.CommitAsync(Ct);
        }

        // This fixture writes neither audit nor DEK rows. Their real zero-row outcomes are the only
        // substitute seams; eligibility, cascades, profile removal and Identity deletion are real.
        var hardDeleter = new AccountHardDeleter(
            app,
            scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            Substitute.For<IAuditTrailEraser>(),
            Substitute.For<IUserDataKeyStore>(),
            new FixedClock(Now),
            NullLogger<AccountHardDeleter>.Instance,
            coordinator,
            reader);
        (await hardDeleter.GetAccountsReadyForHardDeleteAsync(Now.AddDays(-30), Ct)).ShouldContain(profileId.Value);
        await hardDeleter.HardDeleteAccountAsync(profileId.Value, Ct);
        (await app.JobSeekers.IgnoreQueryFilters().AnyAsync(seeker => seeker.Id == profileId, Ct)).ShouldBeFalse();
        (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString())).ShouldBeNull();
    }

    private async Task<Guid> InsertPreMigrationPasswordlessAccountAsync()
    {
        // UserAccountService.CreatePasswordlessUserAsync before AddAccountAccessSuspension wrote a
        // confirmed, hash-free Identity row. The new model cannot write before its columns exist;
        // Up_PopulatedPredecessor_AddsActiveGenerationZeroAndExactlyOneEpoch pins today's writer.
        var id = Guid.NewGuid();
        var email = $"pre-suspension-{id:N}@example.com";
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity."AspNetUsers"
                (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
                 security_stamp, concurrency_stamp, phone_number_confirmed, two_factor_enabled,
                 lockout_enabled, access_failed_count)
            VALUES (@id, @email, @normalized, @email, @normalized, true, @security, @concurrency,
                    false, false, true, 0)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("normalized", email.ToUpperInvariant());
        command.Parameters.AddWithValue("security", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("concurrency", Guid.NewGuid().ToString());
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        return id;
    }

    private async Task<string> ReadUnchangedUserColumnsAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT (to_jsonb(u) - ARRAY['access_revision', 'credential_cutoff', 'is_suspended'])::text
            FROM identity."AspNetUsers" u WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", userId);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<long> ReadEpochAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT value FROM identity.account_security_epoch WHERE id = 1", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<List<string>> ReadSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || '|' || column_name || '|' || data_type || '|' || is_nullable
                   || '|' || coalesce(column_default, '')
            FROM information_schema.columns WHERE table_schema = 'identity'
            ORDER BY table_name, column_name
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task<int> ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
