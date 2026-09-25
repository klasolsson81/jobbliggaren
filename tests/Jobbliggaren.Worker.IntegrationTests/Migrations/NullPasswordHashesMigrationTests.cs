using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Identity.Migrations;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #1857 (epic #1732 part 5b, ADR 0142) — <c>20260925172152_NullPasswordHashes</c> against a real Postgres:
/// every stored password hash is nulled and both stamps are rotated in the same statement, nothing else on
/// the row moves, a re-run changes nothing, and the migration refuses to reverse.
///
/// <para>
/// <b>Why a container of its own.</b> The journey stops one migration short of this one to seed, steps
/// across it and tries to step back. That needs a database whose migration position the test steers, and a
/// shared fixture's position is fixed before any test runs. One journey method, because xunit news the
/// class, and with it the container, per test method.
/// </para>
///
/// <para>
/// <b>Who wrote each seeded row</b> (AGENTS.md §5 <c>Tests:</c>).
/// <list type="bullet">
/// <item>R1, a hash on an unconfirmed address: the retired <c>POST /auth/register</c>, whose
/// <c>UserAccountService.CreateUserAsync</c> made exactly this call, <c>UserManager.CreateAsync(user, password)</c>
/// (removed in <c>41a49394</c>).</item>
/// <item>R2, a hash on a confirmed address: R1's call, then the write the retired <c>POST /auth/verify-email</c>
/// made after its token check (<c>ConfirmEmailAsync</c> sets the flag and saves the user).</item>
/// <item>R3, no hash: the live writer, <c>UserAccountService.CreatePasswordlessUserAsync</c>.</item>
/// </list>
/// No path in <c>src/</c> writes R1's or R2's shape any more; the current writer's account is born confirmed
/// and without a hash, pinned by
/// <c>LoginChallengeCompleteTests.A_new_address_that_accepts_the_terms_gets_a_passwordless_account_and_a_persistent_session</c>.
/// R1 and R2 carry a password because this migration's premise is a table that holds hashes. The password
/// is made at run time and the hash comes from Identity's hasher inside <c>CreateAsync</c>.
/// </para>
///
/// <para>
/// <b>The stale save.</b> <c>UserStore.UpdateAsync</c> rewrites the whole row under a check on the
/// <c>concurrency_stamp</c> it loaded. The api and worker run while §3c applies this migration, so a
/// <c>UserManager</c> that loaded a row before the commit can save it after: the inbox-proof recorder, the
/// address swap and the admin role seeder all save that way. Without a new <c>concurrency_stamp</c> that
/// save would write the old hash back.
/// </para>
/// </summary>
public sealed class NullPasswordHashesMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20260925172152_NullPasswordHashes";
    private const string PreviousMigration = "20260917195454_DropAuthProviderColumns";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;
    private ServiceProvider? _identity;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _appConnectionString = await TestDatabaseProvisioner
            .ProvisionAndGetAppConnectionStringAsync(
                _postgres.GetConnectionString(),
                includeIdentitySchema: true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _appConnectionString,
            })
            .Build();

        // The Worker's own Identity composition, so every seed and save below goes through the real
        // UserManager and UserStore.
        _identity = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDbExceptionInspector, DbExceptionInspector>()
            .AddCoreIdentityForWorker(configuration)
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_identity is not null)
            await _identity.DisposeAsync();

        await _postgres.DisposeAsync();
    }

    private ServiceProvider Identity => _identity ?? throw new InvalidOperationException("Not initialized.");

    private AppIdentityDbContext NewIdentityContext() =>
        new(MigrationsOptionsFactory.BuildIdentityOptions(_appConnectionString));

    private static string NewAddress() => $"jbl-5b-{Guid.NewGuid():N}@example.com";

    // The composed PasswordOptions require every character class.
    private static string RuntimePassword() =>
        $"{Guid.NewGuid():N}{Guid.NewGuid():N}"
        + $"{Guid.NewGuid():N}{Guid.NewGuid():N}".ToUpperInvariant()
        + "!";

    private async Task<Guid> CreateAsTheRetiredRegisterDidAsync(bool thenConfirmAsVerifyEmailDid)
    {
        await using var scope = Identity.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = NewAddress();
        var user = new ApplicationUser { UserName = email, Email = email };
        (await users.CreateAsync(user, RuntimePassword())).Succeeded.ShouldBeTrue();

        if (thenConfirmAsVerifyEmailDid)
        {
            user.EmailConfirmed = true;
            (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        }

        return user.Id;
    }

    private async Task<Guid> CreateAsTheLiveWriterDoesAsync(CancellationToken ct)
    {
        await using var scope = Identity.CreateAsyncScope();
        var created = await ActivatorUtilities.CreateInstance<UserAccountService>(scope.ServiceProvider)
            .CreatePasswordlessUserAsync(NewAddress(), ct);

        created.IsSuccess.ShouldBeTrue();
        return created.Value;
    }

    /// <summary>
    /// A row as Postgres holds it. <see cref="OtherColumns"/> is the row without the three columns the
    /// migration may write, so one comparison covers every column it must not.
    /// </summary>
    private sealed record Row(
        string? PasswordHash,
        string? SecurityStamp,
        string? ConcurrencyStamp,
        bool EmailConfirmed,
        string OtherColumns,
        string WholeRow);

    private async Task<Row> ReadRowAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT password_hash, security_stamp, concurrency_stamp, email_confirmed,
                   (to_jsonb(u) - ARRAY['password_hash', 'security_stamp', 'concurrency_stamp'])::text,
                   to_jsonb(u)::text
            FROM identity."AspNetUsers" u
            WHERE id = @id
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).ShouldBeTrue($"account {id} must exist");

        return new Row(
            await reader.IsDBNullAsync(0, ct) ? null : reader.GetString(0),
            await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1),
            await reader.IsDBNullAsync(2, ct) ? null : reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    /// <summary>The predicate §3c's pre-read and read-back count with.</summary>
    private async Task<long> CountHashedAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """SELECT count(*) FROM identity."AspNetUsers" WHERE password_hash IS NOT NULL""",
            conn);

        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private async Task<List<string>> ReadColumnCatalogAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'AspNetUsers'
            ORDER BY column_name
            """,
            conn);

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(0));

        return columns;
    }

    private async Task<Dictionary<Guid, Row>> ReadRowsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var rows = new Dictionary<Guid, Row>();
        foreach (var id in ids)
            rows[id] = await ReadRowAsync(id, ct);

        return rows;
    }

    private async Task RowsShouldStillBeAsync(Dictionary<Guid, Row> expected, CancellationToken ct)
    {
        foreach (var (id, row) in expected)
            (await ReadRowAsync(id, ct)).ShouldBe(row);
    }

    [Fact]
    public async Task NullPasswordHashes_NullsEveryHash_RotatesOnlyThoseStamps_IsIdempotent_AndRefusesToReverse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewIdentityContext();
        var migrator = db.GetService<IMigrator>();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.IndexOf(PreviousMigration).ShouldBe(assembly.IndexOf(ThisMigration) - 1);

        // --- 0. One short of this migration.
        await migrator.MigrateAsync(PreviousMigration, ct);

        // --- 1. The three rows, and a UserManager that loads R2 now and saves it only in step 9.
        var r1 = await CreateAsTheRetiredRegisterDidAsync(thenConfirmAsVerifyEmailDid: false);
        var r2 = await CreateAsTheRetiredRegisterDidAsync(thenConfirmAsVerifyEmailDid: true);
        var r3 = await CreateAsTheLiveWriterDoesAsync(ct);
        Guid[] all = [r1, r2, r3];
        Guid[] hashed = [r1, r2];

        await using var staleScope = Identity.CreateAsyncScope();
        var staleUsers = staleScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var staleR2 = (await staleUsers.FindByIdAsync(r2.ToString())).ShouldNotBeNull();

        // --- 2. The premise: two hashed rows, one of them unconfirmed, and a row without a hash.
        var hashedBefore = await CountHashedAsync(ct);
        hashedBefore.ShouldBe(hashed.Length);
        var catalogBefore = await ReadColumnCatalogAsync(ct);
        var before = await ReadRowsAsync(all, ct);

        foreach (var id in hashed)
        {
            before[id].PasswordHash.ShouldNotBeNull();
            before[id].SecurityStamp.ShouldNotBeNull();
            before[id].ConcurrencyStamp.ShouldNotBeNull();
        }

        before[r1].EmailConfirmed.ShouldBeFalse();
        before[r2].EmailConfirmed.ShouldBeTrue();
        before[r3].PasswordHash.ShouldBeNull();
        before[r3].SecurityStamp.ShouldNotBeNull();

        // --- 3. Up applies the constant and nothing else, inside EF's migration transaction. Down refuses
        // while its operations are being built, so no SQL for it can exist.
        var up = new NullPasswordHashes().UpOperations.ShouldHaveSingleItem().ShouldBeOfType<SqlOperation>();
        up.Sql.ShouldBe(NullPasswordHashes.NullPasswordHashesSql);
        up.SuppressTransaction.ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => new NullPasswordHashes().DownOperations);

        // --- 3b. The constant is one statement over exactly the hashed rows. A bare UPDATE reports the rows
        // it wrote; two statements would report their sum and a DO block -1. Rolled back, so the migration
        // below still meets the premise.
        await using (var probe = NewIdentityContext())
        {
            await using var transaction = await probe.Database.BeginTransactionAsync(ct);
            (await probe.Database.ExecuteSqlRawAsync(NullPasswordHashes.NullPasswordHashesSql, ct))
                .ShouldBe((int)hashedBefore);
            await transaction.RollbackAsync(ct);
        }

        await RowsShouldStillBeAsync(before, ct);

        // --- 4. Across this migration.
        await migrator.MigrateAsync(ThisMigration, ct);
        (await db.Database.GetAppliedMigrationsAsync(ct)).Last().ShouldBe(ThisMigration);

        // --- 5. The hashed rows: no hash, both stamps new and different per row, every other column as it was.
        var after = await ReadRowsAsync(all, ct);

        foreach (var id in hashed)
        {
            after[id].PasswordHash.ShouldBeNull();
            after[id].SecurityStamp.ShouldNotBeNull();
            after[id].SecurityStamp.ShouldNotBe(before[id].SecurityStamp);
            after[id].ConcurrencyStamp.ShouldNotBeNull();
            after[id].ConcurrencyStamp.ShouldNotBe(before[id].ConcurrencyStamp);
            after[id].OtherColumns.ShouldBe(before[id].OtherColumns);
        }

        after[r1].SecurityStamp.ShouldNotBe(after[r2].SecurityStamp);
        after[r1].ConcurrencyStamp.ShouldNotBe(after[r2].ConcurrencyStamp);

        // Already inside OtherColumns, and named because the inbox-proof recorder branches on it: an address
        // the retired register left unconfirmed must still be unconfirmed.
        after[r1].EmailConfirmed.ShouldBeFalse();
        after[r2].EmailConfirmed.ShouldBeTrue();

        // Identity treats the stamp as opaque and refuses only a null one, in the member token generation reads.
        await using (var reader = Identity.CreateAsyncScope())
        {
            var users = reader.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = (await users.FindByIdAsync(r1.ToString())).ShouldNotBeNull();
            (await users.GetSecurityStampAsync(fresh)).ShouldBe(after[r1].SecurityStamp);
        }

        // --- 6. The row without a hash did not move at all, stamps included.
        after[r3].WholeRow.ShouldBe(before[r3].WholeRow);

        // --- 7. The read-back §3c takes, and no schema change.
        (await CountHashedAsync(ct)).ShouldBe(0);
        (await ReadColumnCatalogAsync(ct)).ShouldBe(catalogBefore);

        // --- 8. A re-run finds nothing to write and changes nothing.
        (await db.Database.ExecuteSqlRawAsync(NullPasswordHashes.NullPasswordHashesSql, ct)).ShouldBe(0);
        await RowsShouldStillBeAsync(after, ct);

        // --- 9. The UserManager from step 1 saves R2 now. Its load predates the migration, so the save is
        // refused rather than writing the old hash back.
        var staleSave = await staleUsers.UpdateAsync(staleR2);
        staleSave.Errors.Select(e => e.Code).ShouldBe([nameof(IdentityErrorDescriber.ConcurrencyFailure)]);
        (await ReadRowAsync(r2, ct)).ShouldBe(after[r2]);

        // --- 10. Back one: refused, and nothing moved.
        await Should.ThrowAsync<NotSupportedException>(() => migrator.MigrateAsync(PreviousMigration, ct));
        (await db.Database.GetAppliedMigrationsAsync(ct)).Last().ShouldBe(ThisMigration);
        await RowsShouldStillBeAsync(after, ct);

        // --- 11. `dotnet ef migrations script <this> <previous>` makes this call: no rollback script can be
        // generated at all.
        Should.Throw<NotSupportedException>(() => migrator.GenerateScript(ThisMigration, PreviousMigration));

        // --- 12. A second run from a fresh context takes the history table's lock and completes, so the
        // refusal left neither that lock nor a half-applied migration behind. The provider logs no lock event,
        // so the statement it executes is what is read.
        var executed = new List<string>();
        var observed = new DbContextOptionsBuilder<AppIdentityDbContext>(
                MigrationsOptionsFactory.BuildIdentityOptions(_appConnectionString))
            .LogTo(
                (eventId, _) => eventId.Id == RelationalEventId.CommandExecuting.Id,
                e => executed.Add(((CommandEventData)e).Command.CommandText))
            .Options;

        await using (var second = new AppIdentityDbContext(observed))
        {
            await second.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);
            (await second.Database.GetAppliedMigrationsAsync(ct)).Last().ShouldBe(ThisMigration);
        }

        executed.ShouldContain(sql => sql.StartsWith(
            """LOCK TABLE identity."__EFMigrationsHistory" """, StringComparison.Ordinal));
        await RowsShouldStillBeAsync(after, ct);

        // --- 13. The live recorder over the row this migration left: an unconfirmed address without a hash.
        // Its first proof still confirms the address and rotates the stamp.
        await using (var proof = Identity.CreateAsyncScope())
        {
            var recorder = new IdentityInboxProofRecorder(
                proof.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
            (await recorder.RecordAsync(r1, ct)).ShouldBe(InboxProof.FirstProofRecorded);
        }

        var proven = await ReadRowAsync(r1, ct);
        proven.EmailConfirmed.ShouldBeTrue();
        proven.PasswordHash.ShouldBeNull();
        proven.SecurityStamp.ShouldNotBe(after[r1].SecurityStamp);
    }
}
