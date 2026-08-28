using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// crypto-erasure result, and that number is what M-4 records as its proof.
/// <b>If a command here must be changed to make this pass, that is a finding against the runbook
/// and it is fixed there</b>, never adapted here — and that rule has already been broken once and
/// repaired: an earlier revision quietly added <c>-v ON_ERROR_STOP=1</c> to a <c>psql -c</c> INSERT
/// (where it is inert; a single statement fails loudly either way) instead of reporting that §5's
/// step-5 script was missing it. §5 carries it now.
/// </para>
///
/// <para>
/// <b>What "types §5's commands" does and does not mean here.</b> The commands' shape, flags,
/// pipeline and redirections are §5's; only connection identifiers and file paths are substituted.
/// Four things are deliberately NOT verbatim, named rather than left to be discovered:
/// step 4's staging-table count and the per-user checks are read through Npgsql because the drill
/// needs the values in C#; the seed is the drill's own; §5's <c>--</c> prose inside the step-5
/// script is omitted, since the parity pin's contract is over command lines and psql ignores it;
/// and §5's step-5 heredoc is delivered as <c>-f</c> over a copied file, because ON_ERROR_STOP's
/// semantics belong to script-vs-<c>-c</c> rather than to heredoc-vs-<c>-f</c>.
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
/// <b>Out of scope, deliberately, and each with its reason:</b>
/// </para>
/// <list type="bullet">
/// <item><c>age</c>, <c>rclone</c>, the systemd units and the target's retention — §6 assigns
/// those to the ops half, and §1 gives the reason they cannot live here: the box holds no private
/// key by design, so no CI process can possess that path.</item>
/// <item><b>The privilege axis of §5 step 7.</b> The step IS performed — the vacuity guard reads
/// an encrypted field through the production read path — but over a <b>superuser</b> connection.
/// <c>pg_restore --no-privileges</c> carries no grants and the target cluster has no
/// <c>jobbliggaren_app</c>, which is the very absence that makes step 3's flags an oracle. So the
/// #1229/#1232 privilege class is <b>unproven here</b>, deliberately and not by oversight;
/// proving it needs Phase A run against the restored database after step 3, which is a sequencing
/// decision of its own.</item>
/// <item><b>§5's <c>### 8</c> Reconciliation</b>, which §5 marks "mandatory, not optional". Art. 17
/// completeness for a restored generation rests on <b>that</b>, not on crypto-erasure: the erased
/// user's <c>job_seekers</c> row, name and Identity email come back readable, and only the
/// reconciliation removes them. Nothing below should be read as proof of erasure completeness.</item>
/// </list>
/// </summary>
[Collection("RestoreDrill")]
public class BackupRestoreDrillTests(RestoreDrillFixture fixture)
{
    private readonly RestoreDrillFixture _fixture = fixture;

    private const string ErasedUserCoverLetter = "Ciphertext that must not survive the erasure.";

    /// <summary>The vacuity guard's expected value — asserted back as plaintext after the restore.</summary>
    private const string SurvivorCoverLetter = "Ciphertext that must still decrypt after restore.";

    private const string LateUserCoverLetter = "Written after the main artefact was taken.";

    /// <summary>
    /// How many keys this drill's seed puts in the DEK artefact: the survivor's and the late
    /// user's. The erased user's is gone by then, which is the point of the whole procedure.
    ///
    /// <para>
    /// §5's own guard at this step is only <c>&gt; 0</c> ("Zero here means STOP"). This is
    /// deliberately stricter, because the drill knows its own seed — but it is therefore a
    /// property of the seed and NOT a restatement of the runbook, and a fourth seeded user with
    /// encrypted data must move it.
    /// </para>
    /// </summary>
    private const int StagingRowsAfterLoad = 2;

    /// <summary>
    /// The second restore database, used only by the reversed-pairing counterfactual. Separate
    /// from <see cref="RestoreDrillFixture.RestoreDatabaseName"/> so the counterfactual cannot
    /// disturb the evidence the drill has already recorded.
    /// </summary>
    private const string ReversedPairingDatabaseName = "jobbliggaren_restore_reversed";

    /// <summary>
    /// The mechanism's own dump scope, mirrored from <c>deploy/systemd/jobbliggaren-backup.sh</c>
    /// so this drill restores the artefact production actually produces.
    ///
    /// <para>
    /// <b>Why it is a constant and not a literal at each call site:</b> the same reason
    /// <c>RestoreDrillRunbookParityTests</c> gives for the ciphertext pattern — the runbook is text
    /// an operator types and must carry the literal, but the drill is code, and a copy in code is a
    /// second truth that drifts. <c>BackupDumpScopeParityTests</c> binds this declaration to the
    /// script's own flag, so a divergence fails the build.
    /// </para>
    ///
    /// <para>
    /// <b>Why the drill was blind without it.</b> Until #1532 both dump lines here were
    /// schema-wide and so was the script, so the parity held by accident. The moment the script
    /// narrowed, this drill kept restoring a WIDER artefact than the box produces — and the
    /// oracle two screens down (<c>errors ignored on restore</c>) is precisely the assertion that
    /// would have caught the allow-list form dropping <c>CREATE EXTENSION pg_trgm</c> while still
    /// emitting the two GIN indexes that need it. It was aimed at the wrong command, not absent.
    /// </para>
    /// </summary>
    private const string BackupDumpScope = "--exclude-schema=hangfire";

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
            $"pg_dump -U {pgUser} -d {pgDatabase} -Fc --no-owner --no-privileges {BackupDumpScope} --exclude-table-data=user_data_keys > /tmp/main.dump",
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
        // The target cluster has never heard of production's roles. THESE flags — the restore's,
        // not the dump's — are what make that survivable: measured 2026-08-09, dropping them here
        // fails with `role "jobbliggaren_migrations" does not exist`, while dropping them from the
        // pg_dump above changes nothing, because this command strips ownership either way.
        await ExecOkAsync(_fixture.Target,
            $"createdb -U postgres {RestoreDrillFixture.RestoreDatabaseName}",
            "§5 step 3 — create the restore database", ct);
        var mainRestore = await _fixture.Target.ExecAsync(
            ["sh", "-c", $"pg_restore -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} --no-owner --no-privileges /tmp/main.dump"],
            ct);

        mainRestore.ExitCode.ShouldBe(0L,
            $"§5 step 3 pg_restore failed. stdout: {mainRestore.Stdout} stderr: {mainRestore.Stderr}");

        // THE EXIT CODE IS NOT THE ORACLE HERE EITHER, and pg_restore says so itself: `-e,
        // --exit-on-error   exit on error, default is to continue`. Without --exit-on-error it
        // restores what it can, prints `errors ignored on restore: N`, and the status of that path
        // is undocumented. A partial restore that dropped resumes, parsed_resumes or the identity
        // schema is invisible to every assertion below, because they read only the three tables
        // this drill seeds.
        mainRestore.Stderr.Contains("errors ignored on restore", StringComparison.Ordinal).ShouldBeFalse(
            $"§5 step 3 restored with ignored errors, which pg_restore does NOT surface in its exit " +
            $"code. stderr: {mainRestore.Stderr}");

        // ── §5 STEP 4: load the DEKs THROUGH A STAGING TABLE ───────────────────────────────────
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -c 'CREATE TABLE _dek_restore (LIKE user_data_keys);'",
            "§5 step 4 — create the staging table", ct);

        // The substitution. Schema-qualified on BOTH sides, exactly as §5 has it after the
        // measured defect: pg_restore's search_path preamble makes an unqualified target resolve
        // to nothing, and the error is silent without ON_ERROR_STOP.
        await ExecOkAsync(_fixture.Target,
            @"pg_restore -f - /tmp/deks.dump | sed 's/^COPY public\.user_data_keys /COPY public._dek_restore /' > /tmp/deks.sql",
            "§5 step 4 — redirect the COPY at the staging table", ct);

        // §5's own two grep checks, run because the runbook instructs the operator to run them.
        // NOTE: `grep -c` exits 1 when the count is 0, so the SECOND of these legitimately exits
        // non-zero. Asserting exit 0 here would be a rig defect that fails a correct runbook;
        // the number on stdout is the measurement, and it is what is asserted.
        (await ExecCountAsync(_fixture.Target,
                @"grep -c '^COPY public\._dek_restore ' /tmp/deks.sql", ct))
            .ShouldBe("1", "§5 step 4: the substituted COPY must appear exactly once");
        (await ExecCountAsync(_fixture.Target,
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

        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -f /tmp/deks.sql",
            "§5 step 4 — load the substituted DEK SQL", ct);

        // AND VERIFY THE LOAD ITSELF, because the two greps above cannot: they verify the
        // substitution, not the load. This is the assertion whose absence let PR-1's zero-key
        // restore report a perfect result.
        (await ScalarAsync(_fixture.RestoredServices, "SELECT count(*) FROM _dek_restore", ct))
            .ShouldBe(StagingRowsAfterLoad.ToString(CultureInfo.InvariantCulture),
                "§5 step 4: the staging table must hold the survivor's and the late user's keys. " +
                "Zero here means the restore loaded no keys at all, and every count below would " +
                "then be measuring THAT rather than an erasure.");

        // ── §5 STEP 5: ONE script-fed invocation, and the shape is the whole point ─────────────
        //
        // §5 runs the INSERT and all three evidence queries in a SINGLE psql invocation fed from a
        // script. That is the one shape where `-v ON_ERROR_STOP=1` decides anything: without it an
        // INSERT that hits the FK prints its error, psql CONTINUES INTO THE EVIDENCE QUERIES, and
        // exits 0 — so the counts are computed against a table the INSERT never populated.
        //
        // An earlier revision of this drill ran the INSERT as `psql -c '…'` with the flag attached
        // and read the evidence back through Npgsql. Measured in a throwaway postgres:18: a
        // single-statement `-c` exits 1 on error WITH OR WITHOUT the flag, so that flag was inert,
        // and the restructuring had moved the failure mode out of reach entirely. Deleting
        // `-v ON_ERROR_STOP=1` from the runbook would have turned nothing in this suite red.
        //
        // So the evidence is asserted against §5's OWN stdout, not against a paraphrase of its
        // queries. The script is delivered as bytes with LF endings built explicitly — never as a
        // multi-line C# literal, which in this CRLF tree would carry \r into the SQL.
        var step5Sql = string.Join('\n',
        [
            "INSERT INTO user_data_keys",
            "SELECT * FROM _dek_restore",
            "WHERE job_seeker_id IN (SELECT id FROM job_seekers);",
            "SELECT count(*) AS deks_dropped_as_orphans",
            "FROM _dek_restore d WHERE d.job_seeker_id NOT IN (SELECT id FROM job_seekers);",
            "SELECT count(*) AS users_without_a_key_TOTAL",
            "FROM job_seekers j WHERE j.id NOT IN (SELECT job_seeker_id FROM user_data_keys);",
            "SELECT count(*) AS users_with_ciphertext_but_no_key",
            "FROM job_seekers j",
            "WHERE j.id NOT IN (SELECT job_seeker_id FROM user_data_keys)",
            "  AND EXISTS (",
            "    SELECT 1 FROM applications a",
            $"    WHERE a.job_seeker_id = j.id AND a.cover_letter LIKE '{FieldEncryptionSentinel.SqlLikePattern}'",
            "  );",
            "",
        ]);
        await _fixture.Target.CopyAsync(Encoding.UTF8.GetBytes(step5Sql), "/tmp/step5.sql", ct: ct);

        var step5 = await _fixture.Target.ExecAsync(
            ["sh", "-c", $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -v ON_ERROR_STOP=1 -f /tmp/step5.sql"],
            ct);

        step5.ExitCode.ShouldBe(0L,
            $"§5 step 5 failed. stdout: {step5.Stdout} stderr: {step5.Stderr}");

        // psql's own report line, matched as a WHOLE LINE: Contains("INSERT 0 1") is also
        // satisfied by `INSERT 0 10` and `INSERT 0 1000`, and StagingRowsAfterLoad's own docblock
        // invites a fourth seeded user.
        Lines(step5.Stdout)
            .Any(l => l.Trim() == $"INSERT 0 {StagingRowsAfterLoad - 1}")
            .ShouldBeTrue(
                $"§5 step 5's INSERT must report exactly {StagingRowsAfterLoad - 1} row(s) loaded — " +
                $"the staged keys minus the orphan dropped by the WHERE. stdout: {step5.Stdout}");

        // Evidence (a) — DEK rows dropped as belonging to nobody in this generation, read off the
        // invocation's own output.
        EvidenceFrom(step5.Stdout, "deks_dropped_as_orphans")
            .ShouldBe("1", "evidence (a): exactly the late user's key is dropped as an orphan");
        (await ScalarAsync(_fixture.RestoredServices,
                $"SELECT count(*) FROM user_data_keys WHERE job_seeker_id = '{late.JobSeekerId.Value}'", ct))
            .ShouldBe("0", "evidence (a), named: the dropped orphan is the user who registered after the artefact");

        // ── THE CLAIM THE SPLIT DUMP EXISTS FOR ────────────────────────────────────────────────
        //
        // Evidence (b2), not (b). §5 is explicit about why: DEK rows are created lazily, so (b)
        // mixes erased users with users who never wrote encrypted data and would overstate the
        // result. (b2) is the erasure SIGNATURE — ciphertext present, key absent.
        EvidenceFrom(step5.Stdout, "users_with_ciphertext_but_no_key")
            .ShouldBe("1", "evidence (b2): exactly one restored user has ciphertext but no key");

        // (b) is read too, because §5 tells the operator to record it and because the gap between
        // the two numbers is the thing §5's comment warns about. Here they are equal — no seeded
        // user lacks encrypted data — so this pins that the drill's (b2) is not silently (b).
        EvidenceFrom(step5.Stdout, "users_without_a_key_TOTAL")
            .ShouldBe("1", "evidence (b): on this seed every keyless user also has ciphertext, so (b) == (b2)");

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
        // Same Shouldly 4.3 overload trap as above.
        erasedCiphertext.ShouldNotBeNull();
        erasedCiphertext.StartsWith(FieldEncryptionSentinel.VersionPrefix, StringComparison.Ordinal)
            .ShouldBeTrue(
                "and their ciphertext IS restored — which is what makes the missing key the control " +
                "rather than the absence of data");

        // ── §5 STEP 7, AND THE VACUITY GUARD — the same block ──────────────────────────────────
        //
        // §5 step 7 is "Boot the application against the restored database and READ AN ENCRYPTED
        // FIELD through it". This is that step, as an application GRAPH rather than a booted host,
        // and over the SUPERUSER connection: the restored cluster has no application roles (see
        // the class docblock's scope list). It runs AFTER step 5 and the counts are never re-read
        // afterwards, because GetOrCreateDataKeyAsync WRITES — reaching a keyless user through
        // this path mints a fresh key row and would drive the evidence toward zero.
        //
        // It is also the vacuity guard. Without it, "the erased user has no key" is equally true
        // of a restore that loaded no keys at all — the exact shape PR-1 shipped. The survivor
        // must decrypt THROUGH PRODUCTION'S OWN READ PATH (the materialization interceptor + the
        // DEK unwrap), not through a raw SELECT, because a raw SELECT would only prove bytes are
        // present.
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

        // ── THE COUNTERFACTUAL THAT MAKES STEP 0 LOAD-BEARING ─────────────────────────────────
        //
        // Everything above shows (b2) counting an erased user. This shows (b2) counting a user who
        // was NEVER erased, under the one pairing the mechanism forbids — a DEK artefact OLDER
        // than the main artefact, which is exactly what a run whose DEK leg failed after its main
        // artefact uploaded leaves offsite (jobbliggaren-backup.sh's UNPAIRED_MAIN_WARNING).
        //
        // The two states are byte-identical in the restore: ciphertext present, key absent. So
        // (b2) is an erasure count ONLY IF STEP 0 PASSED, and this is the measurement that says so
        // rather than the runbook asserting it. Without this, §5's evidence could report silent
        // data loss as a successful Art. 17 erasure and nothing would contradict it.
        var postDek = await SeedUserWithEncryptedCoverLetterAsync(
            "Registered after the DEK generation was taken.", softDeleted: false, ct);

        // A main artefact NEWER than the DEK artefact already on the target. This is the reversal.
        await ExecOkAsync(_fixture.Source,
            $"pg_dump -U {pgUser} -d {pgDatabase} -Fc --no-owner --no-privileges {BackupDumpScope} --exclude-table-data=user_data_keys > /tmp/main-newer.dump",
            "a main artefact newer than the DEK generation", ct);
        await TransportAsync("/tmp/main-newer.dump", ct);

        await ExecOkAsync(_fixture.Target,
            $"createdb -U postgres {ReversedPairingDatabaseName}",
            "the reversed-pairing restore database", ct);
        var reversedRestore = await _fixture.Target.ExecAsync(
            ["sh", "-c", $"pg_restore -U postgres -d {ReversedPairingDatabaseName} --no-owner --no-privileges /tmp/main-newer.dump"],
            ct);

        reversedRestore.ExitCode.ShouldBe(0L,
            $"restore of the newer main artefact failed. stderr: {reversedRestore.Stderr}");
        reversedRestore.Stderr.Contains("errors ignored on restore", StringComparison.Ordinal).ShouldBeFalse(
            "same guard as step 3 - pg_restore continues past errors by default, and the two " +
            $"absence assertions below would otherwise rest on a partial restore. stderr: {reversedRestore.Stderr}");
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {ReversedPairingDatabaseName} -v ON_ERROR_STOP=1 -c 'CREATE TABLE _dek_restore (LIKE user_data_keys);'",
            "the reversed pairing's staging table", ct);
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {ReversedPairingDatabaseName} -v ON_ERROR_STOP=1 -f /tmp/deks.sql",
            "the OLDER DEK generation, loaded against a NEWER main artefact", ct);
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {ReversedPairingDatabaseName} -v ON_ERROR_STOP=1 -c 'INSERT INTO user_data_keys SELECT * FROM _dek_restore WHERE job_seeker_id IN (SELECT id FROM job_seekers);'",
            "the reversed pairing's key load", ct);

        var reversed = await ExecScalarAsync(_fixture.Target,
            $"psql -U postgres -d {ReversedPairingDatabaseName} -tAc \"SELECT count(*) FROM job_seekers j WHERE j.id NOT IN (SELECT job_seeker_id FROM user_data_keys) AND EXISTS (SELECT 1 FROM applications a WHERE a.job_seeker_id = j.id AND a.cover_letter LIKE '{FieldEncryptionSentinel.SqlLikePattern}')\"",
            "the reversed pairing's (b2)", ct);

        // ONE. Exactly what the legitimate restore above reported — where it meant one erasure.
        // Here it means ZERO erasures and one user whose key is permanently gone. The two
        // generations produce the same number for opposite reasons, and no query in §5 can tell
        // them apart; only step 0 can, before the restore starts.
        reversed.ShouldBe("1",
            "under a REVERSED pairing (b2) reports the same 1 the legitimate restore reports — but " +
            "nobody was erased in this generation at all. The one it counts merely registered " +
            "after the DEK artefact was taken, and their data is permanently unreadable: silent " +
            "data loss wearing erasure's clothes. This is why §5 step 0 is a refusal and not a " +
            "formality, and why (b2) is erasure evidence ONLY once step 0 has passed.");

        var reversedNamed = await ExecScalarAsync(_fixture.Target,
            $"psql -U postgres -d {ReversedPairingDatabaseName} -tAc \"SELECT count(*) FROM user_data_keys WHERE job_seeker_id = '{postDek.JobSeekerId.Value}'\"",
            "the reversed pairing's named keyless user", ct);

        reversedNamed.ShouldBe("0",
            "named, so the count above cannot be right for the wrong user: the keyless one is the " +
            "user who registered after the DEK generation and was never erased");

        // And the erased user is absent here entirely — this main artefact post-dates the erasure —
        // so the 1 above cannot be them. Without this the counterfactual would be arguable.
        var erasedInReversed = await ExecScalarAsync(_fixture.Target,
            $"psql -U postgres -d {ReversedPairingDatabaseName} -tAc \"SELECT count(*) FROM job_seekers WHERE id = '{erased.JobSeekerId.Value}'\"",
            "the erased user's absence from the reversed generation", ct);

        erasedInReversed.ShouldBe("0",
            "the erased user is not in this generation at all, so the (b2) of 1 above is entirely " +
            "the never-erased user — which is what makes the two signatures indistinguishable");

        // ── §5's remaining steps ──────────────────────────────────────────────────────────────
        //
        // The staging table is dropped only now: the evidence (a) query above reads it, so a
        // drop placed where the runbook prints it (immediately after step 5's SQL block) would
        // have taken the drill's own measurement with it.
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -c 'DROP TABLE _dek_restore;'",
            "§5 step 5 — drop the staging table", ct);

        // §5 step 6. A restore carries no planner statistics — pg_dump omits them unless
        // --statistics is passed, and neither dump passes it. Nothing here ASSERTS a plan, so
        // this step's value in CI is that it is proven to run at all against the restored schema;
        // its real consequence is on the operator's database.
        await ExecOkAsync(_fixture.Target,
            $"psql -U postgres -d {RestoreDrillFixture.RestoreDatabaseName} -c 'ANALYZE;'",
            "§5 step 6 — refresh planner statistics", ct);
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
    /// code. For <c>grep -c</c> only, which exits 1 on a zero count — a correct result an
    /// exit-code assertion would fail. Anything else uses <see cref="ExecScalarAsync"/>.
    /// </summary>
    private static async Task<string> ExecCountAsync(
        PostgreSqlContainer container, string command, CancellationToken ct)
    {
        var result = await container.ExecAsync(["sh", "-c", command], ct);

        // The exemption is from the ZERO-vs-ONE distinction only, never from a broken rig. 127 is
        // `command not found`, and scoring it as a legitimate result is what cost #197 PR-1
        // sixteen assertions that measured nothing.
        result.ExitCode.ShouldNotBe(127L,
            $"a tool was not found in the container (exit 127) running: {command}. This is a broken " +
            $"rig, not a result. stderr: {result.Stderr}");

        return result.Stdout.Trim();
    }

    private async Task TransportAsync(string path, CancellationToken ct)
    {
        var bytes = await _fixture.Source.ReadFileAsync(path, ct);
        bytes.Length.ShouldBeGreaterThan(0, $"{path} must be a non-empty artefact before transport");
        await _fixture.Target.CopyAsync(bytes, path, ct: ct);
    }

    /// <summary>
    /// Reads one of §5 step 5's evidence counts out of that invocation's own psql output.
    ///
    /// <para>
    /// psql's aligned default prints a single-column single-row result as three lines — the column
    /// alias, a rule, then the value — and §5 does not pass <c>-t</c> or <c>-A</c>, so this is the
    /// surface an operator actually reads. Reading it here rather than re-querying through Npgsql
    /// is what makes the assertion an assertion about §5's output instead of about a paraphrase of
    /// its SQL.
    /// </para>
    /// </summary>
    private static string EvidenceFrom(string psqlStdout, string alias)
    {
        var lines = Lines(psqlStdout);

        // Case-insensitive because Postgres FOLDS unquoted identifiers to lower case, so §5's
        // `AS users_without_a_key_TOTAL` comes back as `users_without_a_key_total`. Matching the
        // runbook's own spelling exactly would fail on that one alias and on no other, which is
        // the kind of near-miss that reads as a real defect.
        var header = Array.FindIndex(
            lines, l => string.Equals(l.Trim(), alias, StringComparison.OrdinalIgnoreCase));

        header.ShouldBeGreaterThanOrEqualTo(0,
            $"§5 step 5's output carries no '{alias}' result. Either the query did not run — which " +
            $"is what a missing ON_ERROR_STOP after a failed INSERT produces — or §5's aliases " +
            $"moved. stdout: {psqlStdout}");
        (header + 2).ShouldBeLessThan(lines.Length,
            $"'{alias}' has a header but no value row. stdout: {psqlStdout}");

        return lines[header + 2].Trim();
    }

    /// <summary>
    /// Container output split into lines, normalised so a CRLF-emitting tool reads the same as an
    /// LF one. One definition, because two spellings of a split is how the two halves of an
    /// assertion drift apart.
    /// </summary>
    private static string[] Lines(string output) =>
        output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>
    /// A single scalar read through <c>psql -tAc</c> inside the container, requiring exit 0. The
    /// exit code IS meaningful here — unlike <see cref="ExecCountAsync"/>'s <c>grep -c</c> — so a
    /// failed read reports as itself instead of as an empty string mismatching an expected value.
    /// Callers interpolate GUIDs and constants only.
    /// </summary>
    private static async Task<string> ExecScalarAsync(
        PostgreSqlContainer container, string command, string what, CancellationToken ct)
    {
        var result = await container.ExecAsync(["sh", "-c", command], ct);

        result.ExitCode.ShouldNotBe(127L,
            $"{what}: a tool was not found in the container (exit 127). stderr: {result.Stderr}");
        result.ExitCode.ShouldBe(0L,
            $"{what} failed. command: {command} stdout: {result.Stdout} stderr: {result.Stderr}");

        return result.Stdout.Trim();
    }

    /// <summary>
    /// Reads a scalar as text through a graph's own connection. Callers pass GUIDs and constants
    /// only; nothing here is user-shaped, and an edit that interpolates a string should add a
    /// parameter overload rather than widen this one.
    /// </summary>
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
    /// the field-encryption interceptor writes as ciphertext. Optionally soft-deletes it, which is
    /// the only state <c>HardDeleteAccountsJob</c> ever hard-deletes from.
    ///
    /// <para>
    /// The wrapped-DEK row is created by <see cref="WarmOwnerDekAsync"/> →
    /// <c>IUserDataKeyStore.GetOrCreateDataKeyAsync</c>, which runs BEFORE the interceptor — not by
    /// the interceptor itself. Production reaches the same port through
    /// <c>FieldEncryptionKeyPrefetchBehavior</c>, which is what makes this state one <c>src/</c>
    /// produces (CLAUDE.md §5 <c>Tests:</c>).
    /// </para>
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
