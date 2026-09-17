using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Persistence;

/// <summary>
/// #1736 (ADR 0142 D6) — the back-compat proof for the optional owned
/// <see cref="TermsAcceptance"/> on <c>job_seekers</c>. The three columns are nullable and were
/// added to a table that already had rows, so "no acceptance" has to load as
/// <c>TermsAcceptance == null</c> rather than as an all-null instance or a crash. Measured
/// 2026-09-17: EF reads an all-null owned row as <c>null</c> with or without
/// <c>Navigation(...).IsRequired(false)</c> — that mutation leaves the read-side facts here green.
/// The facts here pin the read side; the last one pins that a half-stamped row cannot exist at all.
///
/// <para>
/// <b>The premise (AGENTS.md §5 Tests).</b> An all-NULL row is a state NO path in <c>src/</c>
/// produces any more: <c>JobSeeker.Register</c> refuses a null acceptance, the private constructor
/// is the only writer, and all three VO properties are non-nullable inside the type, so a present
/// acceptance can never write an all-null row. Its actor is <b>the rows written before migration
/// <c>20260917153605_AddTermsAcceptanceToJobSeeker</c></b>, whose writer is the acceptance-less
/// <c>Register</c> signature that this change REPLACED rather than overloaded. That actor is not
/// callable from here, so the shape is reached by raw SQL and named, and the pin that the CURRENT
/// writer cannot produce it lives with that writer —
/// <c>JobSeekerTests.Register_WithNullTermsAcceptance_Fails</c> and
/// <c>JobSeekerTests.Register_WithValidData_CarriesTheTermsAcceptanceItWasGiven</c>.
/// </para>
///
/// <para>
/// <b>Base class.</b> <see cref="MalformedJsonbSeedTestBase"/> despite the columns not being jsonb:
/// what it provides is clearing <c>job_seekers</c> on BOTH entry and exit, so a deliberately
/// legacy-shaped row cannot outlive its test in the shared <c>[Collection("Api")]</c> Postgres.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class TermsAcceptanceBackcompatTests(ApiFactory factory)
    : MalformedJsonbSeedTestBase(factory)
{
    protected override IReadOnlyList<string> TablesToClear => ["job_seekers"];

    // A whole second, so the value survives timestamptz's microsecond resolution unchanged and the
    // round-trip below can assert equality rather than a tolerance.
    private static readonly FakeDateTimeProvider SeedClock =
        new(new DateTimeOffset(2026, 9, 17, 14, 14, 33, TimeSpan.Zero));

    private async Task<JobSeeker> SeedSeekerAsync(string displayName, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker
            .Register(Guid.NewGuid(), displayName, TermsAcceptance.AcceptCurrent(SeedClock), SeedClock)
            .Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private Task NullTermsColumnsAsync(Guid jobSeekerId, CancellationToken ct) =>
        ExecuteAsync(
            """
            UPDATE job_seekers
            SET terms_accepted_at = NULL, terms_version = NULL, privacy_policy_version = NULL
            WHERE id = @id
            """,
            jobSeekerId,
            ct);

    // Its own connection, never EF's (the AddTermsAcceptanceToJobSeekerMigrationTests form): the
    // context's connection state stays the context's business.
    private async Task ExecuteAsync(string sql, Guid jobSeekerId, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.GetConnectionString();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", jobSeekerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<TermsAcceptance?> ReloadTermsAcceptanceAsync(JobSeekerId id, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.JobSeekers.SingleAsync(js => js.Id == id, ct);
        return reloaded.TermsAcceptance;
    }

    [Fact]
    public async Task PreMigrationRow_WithAllThreeTermsColumnsNull_LoadsAsNoAcceptance()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync("Pre-migration Row", ct);

        // The shape of a row written before AddTermsAcceptanceToJobSeeker: the columns exist and are
        // NULL because the migration added them nullable to an already-populated table.
        await NullTermsColumnsAsync(seeker.Id.Value, ct);

        var acceptance = await ReloadTermsAcceptanceAsync(seeker.Id, ct);

        acceptance.ShouldBeNull();
    }

    [Fact]
    public async Task PreMigrationRow_WithAllThreeTermsColumnsNull_StillLoadsTheRestOfTheAggregate()
    {
        // The other half of "no crash": the aggregate around the absent acceptance still materializes,
        // so an unstamped legacy account can still sign in, be read and be soft-deleted. A required
        // navigation would have thrown here instead.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync("Legacy Loads Fine", ct);
        await NullTermsColumnsAsync(seeker.Id.Value, ct);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.JobSeekers.SingleAsync(js => js.Id == seeker.Id, ct);

        reloaded.TermsAcceptance.ShouldBeNull();
        reloaded.DisplayName.ShouldBe("Legacy Loads Fine");
        reloaded.UserId.ShouldBe(seeker.UserId);
        reloaded.Preferences.Language.ShouldBe("sv");
    }

    [Fact]
    public async Task StampedRow_RoundTripsAllThreeValuesThroughTheOwnedColumns()
    {
        // The forward path, and the control for the two cases above: with the columns populated the
        // navigation comes back non-null and carries exactly what was stamped. Without this, a
        // mapping that read every row as "no acceptance" would pass both NULL tests.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync("Stamped Row", ct);
        var stamped = seeker.TermsAcceptance.ShouldNotBeNull();

        var reloaded = await ReloadTermsAcceptanceAsync(seeker.Id, ct);

        reloaded.ShouldNotBeNull();
        reloaded.AcceptedAt.ShouldBe(SeedClock.UtcNow);
        reloaded.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);
        reloaded.PrivacyPolicyVersion.ShouldBe(TermsAcceptance.CurrentPrivacyPolicyVersion);
        reloaded.ShouldBe(stamped);
    }

    [Fact]
    public async Task PartiallyNulledRow_IsRefusedByTheAllOrNothingConstraint()
    {
        // ck_job_seekers_terms_all_or_none: the optional owned mapping reads "no stamp" from all three
        // columns NULL, and the database holds that sentinel where raw SQL lives. A half-stamped row is
        // a state the aggregate has no reading for, so Postgres refuses to hold one (security-auditor,
        // PR #1751). Reached by raw SQL because no path in src/ writes a partial stamp.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync("Half Stamped", ct);

        var refusal = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync("UPDATE job_seekers SET terms_version = NULL WHERE id = @id", seeker.Id.Value, ct));

        refusal.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        refusal.ConstraintName.ShouldBe("ck_job_seekers_terms_all_or_none");
    }
}
