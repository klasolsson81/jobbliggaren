using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Backup;

/// <summary>
/// #197 gate M-4, the drill's CI half — <b>the split dump's central claim, executed</b>:
/// a user hard-deleted after a main artefact was taken restores with ciphertext and
/// <b>no key anywhere in what we hold</b>, while a user who was not deleted decrypts normally.
///
/// <para>
/// <b>This class types the runbook's own commands, and that is the decision rather than an
/// accident</b> (senior-cto-advisor bind 2026-08-09, D1). Gate M-4's claim is that
/// <c>docs/runbooks/backup-restore.md</c> §5 has been executed. A drill that re-implemented §5
/// through Npgsql would prove a paraphrase — and, specifically, it would be structurally
/// incapable of catching either half of the defect that already shipped in PR-1:
/// </para>
/// <list type="number">
/// <item>the staging-table name resolving to nothing, because <c>pg_restore -f -</c> emits
/// <c>set_config('search_path', '', false)</c> — a preamble Npgsql never issues, so the failure
/// cannot arise;</item>
/// <item><c>psql</c> printing that error and exiting <b>0</b> anyway — an exit code Npgsql does
/// not have, because it throws.</item>
/// </list>
/// <para>
/// Together those produced a restore that loaded zero keys while evidence count (b) reported
/// every restored user as keyless: a totally failed restore presenting itself as a flawless
/// crypto-erasure result, and that number is what M-4 records as its proof. So the shape, flags,
/// pipeline and redirections below are §5's verbatim; only connection identifiers are substituted.
/// <b>If a command here must be changed to make this pass, that is a finding against the runbook
/// and it is fixed there</b>, never adapted here.
/// </para>
///
/// <para>
/// <b>The exit code is not the oracle, and neither is any single assertion.</b> PR-1's defect
/// exited 0. Every load below is therefore checked twice — once on what the command reported, and
/// once on the state it was supposed to produce (<see cref="StagingRowsAfterLoad"/> and the
/// per-user key assertions). Neither substitutes for the other.
/// </para>
///
/// <para>
/// <b>Why one test rather than several.</b> This is a procedure, not a set of independent claims:
/// each step consumes the previous step's artefact, and the runbook's own checks (the two
/// <c>grep -c</c> counts, the staging-table count) are steps of it. Splitting it across facts
/// would either re-run two container dumps per fact or hide the procedure in a fixture, where a
/// failure would read as "the fixture broke" rather than as which step of §5 did. Every assertion
/// below therefore names the §5 step and the evidence it carries.
/// </para>
///
/// <para>
/// <b>Out of scope, deliberately:</b> <c>age</c>, <c>rclone</c>, the systemd units and the target's
/// retention. §6 assigns those to the ops half, and §1 gives the reason they cannot live here —
/// the box holds no private key by design, so no CI process can possess that path.
/// </para>
/// </summary>
[Collection("RestoreDrill")]
public class BackupRestoreDrillTests(RestoreDrillFixture fixture)
{
    private readonly RestoreDrillFixture _fixture = fixture;

    /// <summary>Seeded plaintext for the user who is hard-deleted after the main artefact.</summary>
    private const string ErasedUserCoverLetter = "Ciphertext that must not survive the erasure.";

    /// <summary>Seeded plaintext for the user who survives — the vacuity guard's expected value.</summary>
    private const string SurvivorCoverLetter = "Ciphertext that must still decrypt after restore.";

    /// <summary>Seeded plaintext for the user who registers AFTER the main artefact was taken.</summary>
    private const string LateUserCoverLetter = "Written after the main artefact was taken.";

    /// <summary>
    /// §5 step 4's own guard, restated as a constant so the assertion and the message cannot drift
    /// apart: "must be &gt; 0 on any generation that had users. Zero here means STOP."
    /// </summary>
    private const int StagingRowsAfterLoad = 2;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    [Fact]
    public async Task Restore_PairsAnOlderMainArtefactWithTheCurrentDeks_ErasedUserHasNoKey_SurvivorDecrypts()
    {
        var ct = TestContext.Current.CancellationToken;

        var source = new NpgsqlConnectionStringBuilder(_fixture.Source.GetConnectionString());
        var pgUser = source.Username!;
        var pgDatabase = source.Database!;

        // ── SEED, through production entry points ──────────────────────────────────────────────
        //
        // The erased user is soft-deleted first because that is the only state production ever
        // hard-deletes from (HardDeleteAccountsJob selects on deleted_at < cutoff). Seeding a
        // live user and deleting it would assert a production fact off a state production does
        // not reach into this port (CLAUDE.md §5 Tests:).
        var erased = await SeedUserWithEncryptedCoverLetterAsync(ErasedUserCoverLetter, softDeleted: true, ct);
        var survivor = await SeedUserWithEncryptedCoverLetterAsync(SurvivorCoverLetter, softDeleted: false, ct);

        // ── THE MAIN ARTEFACT (jobbliggaren-backup.sh:270) ─────────────────────────────────────
        //
        // --exclude-table-data, NOT --exclude-table: the DEFINITION must travel so the DEK
        // artefact has somewhere to land. The polarity itself is pinned in the script's own
        // fixture suite; what is proved here is what the pair RESTORES to.
        await ExecOkAsync(_fixture.Source,
            $"pg_dump -U {pgUser} -d {pgDatabase} -Fc --no-owner --no-privileges --exclude-table-data=user_data_keys > /tmp/main.dump",
            "the main artefact", ct);

        // ── THE ERASURE, produced by the production actor ──────────────────────────────────────
        //
        // IAccountHardDeleter.HardDeleteAccountAsync — not a hand-written DELETE of the DEK row.
        // The state under test is "this user has been crypto-erased", and the only thing that
        // produces it in src/ is this port (CLAUDE.md §5 Tests:).
        using (var scope = _fixture.SourceServices.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountHardDeleter>()
                .HardDeleteAccountAsync(erased.JobSeekerId.Value, ct);
        }

        // A user who registers BETWEEN the two dumps. Present in the DEK artefact, absent from the
        // main one — which is the cross-generation case step 4's staging table exists for, and
        // which produces evidence count (a).
        var late = await SeedUserWithEncryptedCoverLetterAsync(LateUserCoverLetter, softDeleted: false, ct);

        // ── THE DEK ARTEFACT (jobbliggaren-backup.sh:314) ──────────────────────────────────────
        await ExecOkAsync(_fixture.Source,
            $"pg_dump -U {pgUser} -d {pgDatabase} -Fc --no-owner --no-privileges --data-only --table=user_data_keys > /tmp/deks.dump",
            "the DEK artefact", ct);

        // Precondition, measured rather than assumed: the erasure happened BEFORE this generation,
        // so the erased user's key is not in the artefact we are about to restore.
        (await ScalarAsync(_fixture.SourceServices,
                $"SELECT count(*) FROM user_data_keys WHERE job_seeker_id = '{erased.JobSeekerId.Value}'", ct))
            .ShouldBe("0", "seed precondition: the erased user's DEK is gone from the source before the DEK dump");

        // ── TRANSPORT: the artefacts leave one cluster and land on another ─────────────────────
        //
        // ReadFileAsync returns byte[] and CopyAsync takes byte[]; both are binary-safe, which a
        // custom-format (-Fc) dump requires. This is also the only step that proves the artefact
        // is self-contained — a dump that never moves is never proven portable.
        await TransportAsync("/tmp/main.dump", ct);
        await TransportAsync("/tmp/deks.dump", ct);

        // ── §5 STEP 3: restore the main artefact into a FRESH database ─────────────────────────
        //
        // The target cluster has never heard of jobbliggaren_app. That is what makes
        // --no-owner --no-privileges an oracle here rather than a no-op: without them this
        // restore fails with `role "jobbliggaren_app" does not exist`.
        await ExecOkAsync(_fixture.Target,
            $"createdb -U postgres {RestoreDrillFixture.RestoreDatabaseName}",
            "§5 step 3 createdb", ct);
        await ExecOkAsync(_fixture.Target,
            $"pg_restore -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} --no-owner --no-privileges /tmp/main.dump",
            "§5 step 3 pg_restore of the main artefact", ct);

        // ── §5 STEP 4: load the DEKs THROUGH A STAGING TABLE ───────────────────────────────────
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -c 'CREATE TABLE _dek_restore (LIKE user_data_keys);'",
            "§5 step 4 CREATE TABLE _dek_restore", ct);

        // The substitution. Schema-qualified on BOTH sides, exactly as §5 has it after the
        // measured defect: pg_restore's search_path preamble makes an unqualified target resolve
        // to nothing, and the error is silent without ON_ERROR_STOP.
        await ExecOkAsync(_fixture.Target,
            @"pg_restore -f - /tmp/deks.dump | sed 's/^COPY public\.user_data_keys /COPY public._dek_restore /' > /tmp/deks.sql",
            "§5 step 4 pg_restore | sed", ct);

        // §5's own two grep checks, run because the runbook instructs the operator to run them.
        // NOTE: `grep -c` exits 1 when the count is 0, so the SECOND of these legitimately exits
        // non-zero. Asserting exit 0 here would be a rig defect that fails a correct runbook;
        // the number on stdout is the measurement, and it is what is asserted.
        (await ExecCapturingAsync(_fixture.Target,
                @"grep -c '^COPY public\._dek_restore ' /tmp/deks.sql", ct))
            .ShouldBe("1", "§5 step 4: the substituted COPY must appear exactly once");
        (await ExecCapturingAsync(_fixture.Target,
                @"grep -c '^COPY public\.user_data_keys ' /tmp/deks.sql", ct))
            .ShouldBe("0", "§5 step 4: no COPY may still target user_data_keys directly");

        // ── THE STAGING TABLE IS NOT OPTIONAL — the claim §5 step 4 makes, measured ────────────
        //
        // Same pg_restore output, WITHOUT the substitution, aimed straight at user_data_keys.
        // The DEK artefact carries the late user, whose owner is absent from THIS generation, so
        // the FK (fk_user_data_keys_job_seekers, ON DELETE CASCADE) rejects that row. Run before
        // step 5's INSERT so the table is still empty and the second assertion means something.
        await ExecOkAsync(_fixture.Target,
            "pg_restore -f - /tmp/deks.dump > /tmp/deks_unsubstituted.sql",
            "the unsubstituted DEK SQL", ct);

        var direct = await _fixture.Target.ExecAsync(
            ["sh", "-c", $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -f /tmp/deks_unsubstituted.sql"],
            ct);

        direct.ExitCode.ShouldNotBe(0L,
            "loading the DEK artefact straight at user_data_keys must FAIL — this is why §5 step 4 " +
            $"says the staging table is not optional. stdout: {direct.Stdout} stderr: {direct.Stderr}");
        // Shouldly 4.3 has no customMessage overload for string containment — the second argument
        // binds to `Case`, and the whole expression then resolves against IEnumerable<char>. The
        // sibling encryption suites carry the same note. Asserting the boolean keeps the message.
        direct.Stderr.Contains("fk_user_data_keys_job_seekers", StringComparison.Ordinal)
            .ShouldBeTrue(
                "the failure must be the foreign key, not some other error that happens to be " +
                $"non-zero. stderr: {direct.Stderr}");

        // The load-bearing half: the COPY aborts WHOLE. The good rows go down with the orphan,
        // which is what makes the staging table necessary rather than merely tidy — a per-row
        // rejection would have let the other keys through and needed no indirection at all.
        (await ScalarAsync(_fixture.RestoredServices, "SELECT count(*) FROM user_data_keys", ct))
            .ShouldBe("0",
                "the aborted COPY must have loaded NOTHING — §5 step 4: 'would abort the whole COPY'");

        // Now the real load, through the staging table.
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -f /tmp/deks.sql",
            "§5 step 4 psql -f deks.sql", ct);

        // AND VERIFY THE LOAD ITSELF, because the two greps above cannot: they verify the
        // substitution, not the load. This is the assertion whose absence let PR-1's zero-key
        // restore report a perfect result.
        (await ScalarAsync(_fixture.RestoredServices, "SELECT count(*) FROM _dek_restore", ct))
            .ShouldBe(StagingRowsAfterLoad.ToString(CultureInfo.InvariantCulture),
                "§5 step 4: the staging table must hold the survivor's and the late user's keys. " +
                "Zero here means the restore loaded no keys at all, and every count below would " +
                "then be measuring THAT rather than an erasure.");

        // ── §5 STEP 5: insert only the rows belonging to a user this generation has ────────────
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -c 'INSERT INTO user_data_keys SELECT * FROM _dek_restore WHERE job_seeker_id IN (SELECT id FROM job_seekers);'",
            "§5 step 5 INSERT", ct);

        // Evidence (a) — DEK rows dropped as belonging to nobody in this generation.
        (await ScalarAsync(_fixture.RestoredServices,
                "SELECT count(*) FROM _dek_restore d WHERE d.job_seeker_id NOT IN (SELECT id FROM job_seekers)", ct))
            .ShouldBe("1", "evidence (a): exactly the late user's key is dropped as an orphan");
        (await ScalarAsync(_fixture.RestoredServices,
                $"SELECT count(*) FROM user_data_keys WHERE job_seeker_id = '{late.JobSeekerId.Value}'", ct))
            .ShouldBe("0", "evidence (a), named: the dropped orphan is the user who registered after the artefact");

        // ── THE CLAIM THE SPLIT DUMP EXISTS FOR ────────────────────────────────────────────────
        //
        // Evidence (b2), not (b). §5 is explicit about why: DEK rows are created lazily, so (b)
        // mixes erased users with users who never wrote encrypted data and would overstate the
        // result. (b2) is the erasure SIGNATURE — ciphertext present, key absent.
        (await ScalarAsync(_fixture.RestoredServices,
                "SELECT count(*) FROM job_seekers j WHERE j.id NOT IN (SELECT job_seeker_id FROM user_data_keys) " +
                $"AND EXISTS (SELECT 1 FROM applications a WHERE a.job_seeker_id = j.id AND a.cover_letter LIKE '{FieldEncryptionSentinel.SqlLikePattern}')", ct))
            .ShouldBe("1", "evidence (b2): exactly one restored user has ciphertext but no key");

        // Named, so the count above cannot be right for the wrong user.
        (await ScalarAsync(_fixture.RestoredServices,
                $"SELECT count(*) FROM job_seekers WHERE id = '{erased.JobSeekerId.Value}'", ct))
            .ShouldBe("1", "the erased user IS in the restore — the main artefact predates the erasure");
        (await ScalarAsync(_fixture.RestoredServices,
                $"SELECT count(*) FROM user_data_keys WHERE job_seeker_id = '{erased.JobSeekerId.Value}'", ct))
            .ShouldBe("0",
                "ADR 0049 Beslut 2, executed: the erased user's key is in NO artefact we hold, so " +
                "their field-encrypted columns are unreadable by any combination of what we have");
        var erasedCiphertext = await ScalarAsync(_fixture.RestoredServices,
            $"SELECT cover_letter FROM applications WHERE id = '{erased.ApplicationId.Value}'", ct);
        // Same Shouldly 4.3 overload trap as above: ShouldStartWith's second argument is `Case`.
        erasedCiphertext.ShouldNotBeNull();
        erasedCiphertext.StartsWith(FieldEncryptionSentinel.VersionPrefix, StringComparison.Ordinal)
            .ShouldBeTrue(
                "and their ciphertext IS restored — which is what makes the missing key the control " +
                "rather than the absence of data");

        // ── THE VACUITY GUARD ──────────────────────────────────────────────────────────────────
        //
        // Without this, "the erased user has no key" is also true of a restore that loaded no keys
        // at all — the exact shape PR-1 shipped. The survivor must decrypt THROUGH PRODUCTION'S
        // OWN READ PATH (the materialization interceptor + the DEK unwrap), not through a raw
        // SELECT, because a raw SELECT would only prove bytes are present.
        (await ScalarAsync(_fixture.RestoredServices,
                $"SELECT count(*) FROM user_data_keys WHERE job_seeker_id = '{survivor.JobSeekerId.Value}'", ct))
            .ShouldBe("1", "vacuity guard precondition: the survivor's key WAS loaded");

        using (var scope = _fixture.RestoredServices.CreateScope())
        {
            await WarmOwnerDekAsync(scope, survivor.JobSeekerId, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var restored = await db.Applications
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(a => a.Id == survivor.ApplicationId, ct);

            restored.CoverLetter.ShouldBe(SurvivorCoverLetter,
                "the non-erased user's cover letter must come back as PLAINTEXT through the " +
                "production read path — this is what proves the restore is usable at all, and " +
                "therefore that the erased user's missing key is a control and not a broken restore");
        }

        // ── §5's remaining two steps, run so the drill covers the procedure end to end ─────────
        //
        // The staging table is dropped only now: the evidence (a) query above reads it, so a
        // drop placed where the runbook prints it (immediately after step 5's SQL block) would
        // have taken the drill's own measurement with it.
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -c 'DROP TABLE _dek_restore;'",
            "§5 step 5 DROP TABLE _dek_restore", ct);

        // §5 step 6. A restore carries no planner statistics — pg_dump omits them unless
        // --statistics is passed, and neither dump passes it. Nothing here ASSERTS a plan, so
        // this step's value in CI is that it is proven to run at all against the restored schema;
        // its real consequence is on the operator's database.
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -c 'ANALYZE;'",
            "§5 step 6 ANALYZE", ct);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a single-line shell command in <paramref name="container"/> and requires exit 0,
    /// folding stdout AND stderr into the failure message — a container exec that fails otherwise
    /// reports only a number.
    ///
    /// <para>
    /// <b>Single-line by construction.</b> These strings are C# literals in a CRLF working tree,
    /// so a multi-line script would carry <c>\r</c> into <c>sh</c> and break commands in ways
    /// <c>git diff</c> does not show. Every command below is one physical line, which is the shape
    /// that cannot carry the defect.
    /// </para>
    /// </summary>
    private static async Task ExecOkAsync(
        PostgreSqlContainer container, string command, string what, CancellationToken ct)
    {
        var result = await container.ExecAsync(["sh", "-c", command], ct);

        // 127 is `command not found`, and it must never be scored as a legitimate refusal — that
        // misreading cost #197 PR-1 sixteen assertions that measured nothing.
        result.ExitCode.ShouldNotBe(127L,
            $"{what}: a tool was not found in the container (exit 127). This is a broken rig, not a result. " +
            $"stderr: {result.Stderr}");
        result.ExitCode.ShouldBe(0L,
            $"{what} failed. command: {command} stdout: {result.Stdout} stderr: {result.Stderr}");
    }

    /// <summary>
    /// Runs a single-line shell command and returns trimmed stdout WITHOUT asserting the exit
    /// code. Used only for <c>grep -c</c>, which exits 1 on a zero count — a correct result that
    /// an exit-code assertion would fail.
    /// </summary>
    private static async Task<string> ExecCapturingAsync(
        PostgreSqlContainer container, string command, CancellationToken ct)
    {
        var result = await container.ExecAsync(["sh", "-c", command], ct);
        return result.Stdout.Trim();
    }

    private async Task TransportAsync(string path, CancellationToken ct)
    {
        var bytes = await _fixture.Source.ReadFileAsync(path, ct);
        bytes.Length.ShouldBeGreaterThan(0, $"{path} must be a non-empty artefact before transport");
        await _fixture.Target.CopyAsync(bytes, path, ct: ct);
    }

    /// <summary>Reads a scalar as text through a graph's own connection.</summary>
    private static async Task<string?> ScalarAsync(
        ServiceProvider services, string sql, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : raw.ToString();
    }

    /// <summary>
    /// Warms the scope's DEK cache, which both the encrypting SaveChanges interceptor and the
    /// decrypting materialization interceptor read. Same shape as the sibling encryption suites'.
    /// </summary>
    private static async Task WarmOwnerDekAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek);
    }

    /// <summary>
    /// Seeds one account through production entry points: an Identity user,
    /// <see cref="JobSeeker.Register"/>, and an <see cref="DomainApplication"/> whose cover letter
    /// the field-encryption interceptor writes as ciphertext — which is also what creates the
    /// wrapped-DEK row. Optionally soft-deletes it, which is the only state
    /// <c>HardDeleteAccountsJob</c> ever hard-deletes from.
    /// </summary>
    private async Task<(JobSeekerId JobSeekerId, ApplicationId ApplicationId)>
        SeedUserWithEncryptedCoverLetterAsync(string coverLetter, bool softDeleted, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var deletedAt = now.AddDays(-31);

        JobSeekerId jsId;
        using (var scope = _fixture.SourceServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var email = $"drill-{Guid.NewGuid():N}@test.local";
            var user = new ApplicationUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, "RestoreDrillPass123!"))
                .Succeeded.ShouldBeTrue("seed: the Identity user must be created");

            var seeker = JobSeeker.Register(user.Id, "Restore Drill Seed", new FixedClock(deletedAt.AddDays(-1))).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);
            jsId = seeker.Id;
        }

        ApplicationId appId;
        using (var scope = _fixture.SourceServices.CreateScope())
        {
            await WarmOwnerDekAsync(scope, jsId, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = DomainApplication.Create(
                jsId, jobAdId: null, coverLetter: coverLetter, manualPosting: null,
                new FixedClock(now)).Value;
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        if (softDeleted)
        {
            using var scope = _fixture.SourceServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeker = await db.JobSeekers.IgnoreQueryFilters().SingleAsync(js => js.Id == jsId, ct);
            seeker.SoftDelete(new FixedClock(deletedAt));
            await db.SaveChangesAsync(ct);
        }

        return (jsId, appId);
    }
}
