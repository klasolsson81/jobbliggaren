using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #544 (ADR 0090 D5) — Testcontainers integration tests for <see cref="BackfillCompanyWatchOrgNrTokenJob"/>
/// against REAL Postgres. The one-off backfill tokenises existing PLAINTEXT personnummer-shaped
/// <c>company_watches.organization_number</c> values (enskild-firma follows created via #455 before
/// this change) into HMAC tokens at rest, discarding the plaintext personnummer in place.
/// </summary>
/// <remarks>
/// <b>NEVER EF-InMemory:</b> the at-rest witness reads the <c>organization_number</c> column straight
/// from Postgres (past the EF value-converter) to prove the ON-DISK form, and the widened 64-char
/// column is a Postgres schema fact InMemory hides.
/// <para>
/// <b>Shared-DB determinism (B5 counts):</b> the <c>[Collection("Worker")]</c> Postgres is shared +
/// never reset, and the backfill is a GLOBAL sweep — sibling tests seed pnr-shaped plaintext rows too
/// (the scan suite follows via <c>CompanyWatch.Follow</c>, which stores plaintext). So each test first
/// PRE-DRAINS (a destructive run tokenises every pre-existing pnr plaintext row), then seeds its OWN
/// known rows; the measured run then sees exactly those. The backfill is idempotent by shape, so the
/// pre-drain is a no-op on already-tokenised rows and never disturbs a sibling's own assertions
/// (each filters by its own ids). Legacy rows are seeded via <c>CompanyWatch.Follow</c> — bypassing
/// the tokenising <c>CompanyWatchFollowExecutor</c> seam — so they hold the pre-#544 plaintext form.
/// </para>
/// </remarks>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class BackfillCompanyWatchOrgNrTokenJobTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    [Fact]
    public async Task RunAsync_TokenisesLegacyPlaintextPnrRows_IncludingSoftDeleted_ThenIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;

        // Pre-drain: tokenise every pre-existing pnr plaintext row so the measured run below counts
        // ONLY this test's three seeded rows (shared, never-reset Postgres — see class remarks).
        await RunBackfillAsync(dryRun: false, ct);

        var (activeId, activePnr) = await SeedLegacyPlaintextWatchAsync(active: true, pnrShaped: true, ct);
        var (deletedId, deletedPnr) = await SeedLegacyPlaintextWatchAsync(active: false, pnrShaped: true, ct);
        var (abId, ab) = await SeedLegacyPlaintextWatchAsync(active: true, pnrShaped: false, ct);

        var counts = await RunBackfillAsync(dryRun: false, ct);

        counts.DryRun.ShouldBeFalse();
        counts.Tokenised.ShouldBe(2, "exactly the two legacy plaintext pnr rows this test seeded are tokenised");
        counts.SoftDeletedTokenised.ShouldBe(1,
            "B5 coverage witness: the soft-deleted (unfollowed) pnr row STILL holds a plaintext " +
            "personnummer and MUST be tokenised too (IgnoreQueryFilters)");

        // At rest: both pnr rows now hold the deterministic HMAC token; the AB row is untouched.
        (await RawOrgNrByWatchIdAsync(activeId, ct)).ShouldBe(TokenOf(activePnr));
        (await RawOrgNrByWatchIdAsync(deletedId, ct)).ShouldBe(TokenOf(deletedPnr),
            "the soft-deleted row's plaintext personnummer is overwritten with its token");
        (await RawOrgNrByWatchIdAsync(abId, ct)).ShouldBe(ab, "an AB org.nr is public data — left plaintext");

        // Idempotent by shape: a re-run tokenises nothing (every pnr row is now a 64-char token,
        // length ≠ 10 → the aggregate method no-ops). Exact 0 holds despite the shared DB.
        (await RunBackfillAsync(dryRun: false, ct)).Tokenised.ShouldBe(0,
            "re-running the backfill is a no-op — no row is double-tokenised");
    }

    [Fact]
    public async Task RunAsync_DryRun_ReportsCountsButMutatesNothing()
    {
        var ct = TestContext.Current.CancellationToken;

        // Pre-drain so the dry-run report reflects only this test's fresh plaintext row.
        await RunBackfillAsync(dryRun: false, ct);
        var (watchId, pnr) = await SeedLegacyPlaintextWatchAsync(active: true, pnrShaped: true, ct);

        var counts = await RunBackfillAsync(dryRun: true, ct);

        counts.DryRun.ShouldBeTrue();
        counts.Tokenised.ShouldBe(1, "the dry run REPORTS the one plaintext pnr row it WOULD tokenise");

        // ...but at rest the value is UNCHANGED — a dry run never mutates (STOPP-5: Klas reviews the
        // count delta BEFORE the irreversible destructive run).
        (await RawOrgNrByWatchIdAsync(watchId, ct)).ShouldBe(pnr,
            "dryRun:true must not write — the plaintext personnummer is still at rest, unchanged");
    }

    // ─────────────────────────── Helpers

    // Constructs the job exactly as the Worker host does (scoped IAppDbContext for the id-stream + the
    // root IServiceScopeFactory for per-item scopes) and runs one pass.
    private async Task<CompanyWatchOrgNrTokenBackfillCounts> RunBackfillAsync(bool dryRun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var job = new BackfillCompanyWatchOrgNrTokenJob(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<IProtectedIdentityTokenizer>(),
            new FixedClock(DateTimeOffset.UtcNow),
            Options.Create(new BackfillCompanyWatchOrgNrTokenOptions()),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<BackfillCompanyWatchOrgNrTokenJob>());
        return await job.RunAsync(dryRun, ct);
    }

    // Seeds a LEGACY plaintext watch via CompanyWatch.Follow — bypassing the tokenising executor seam,
    // so the org.nr is stored in the pre-#544 plaintext form (an enskild firma's raw personnummer for
    // pnrShaped:true; a public AB org.nr otherwise). A fresh user each time; soft-deleted when !active.
    private async Task<(Guid WatchId, string OrgNr)> SeedLegacyPlaintextWatchAsync(
        bool active, bool pnrShaped, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        var orgNr = pnrShaped ? UniquePersonnummerShapedOrgNr() : UniqueAbOrgNr();
        var watch = CompanyWatch.Follow(Guid.NewGuid(), OrganizationNumber.Create(orgNr).Value, clock).Value;
        if (!active)
            watch.SoftDelete(clock);
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return (watch.Id.Value, orgNr);
    }

    // Reads organization_number straight from Postgres, PAST the EF value-converter, so the assertion
    // is against the ON-DISK form (a 64-hex token or the 10-digit plaintext).
    private async Task<string?> RawOrgNrByWatchIdAsync(Guid watchId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT organization_number FROM company_watches WHERE id = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = watchId;
        cmd.Parameters.Add(p);
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : raw.ToString();
    }

    private string TokenOf(string orgNr)
    {
        using var scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IProtectedIdentityTokenizer>().Tokenize(orgNr);
    }

    // Third digit 0 → personnummer-shaped (enskild firma / potential personnummer). Unique remainder.
    private static string UniquePersonnummerShapedOrgNr() =>
        "900" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

    // A GUARANTEED non-personnummer-shaped (AB) org.nr — third digit fixed to '2' (legal-entity group
    // numbers are 2–9), so the backfill leaves it plaintext. A "55" + D8 form would NOT guarantee this
    // (zero-padding can put a leading 0/1 in the third position → pnr-shaped → wrongly tokenised).
    private static string UniqueAbOrgNr() =>
        "552" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
