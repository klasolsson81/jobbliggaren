using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

// #352 — deterministic regression guard for MalformedJsonbSeedTestBase. It pins the invariant
// that DISPOSE (not just Initialize) actually clears the toxic table, so a future copy-paste
// that omits exit-cleanup is caught by CI here — NOT by an order-dependent flake in a neighbour
// (e.g. JobAdMatchDetailEndpointTests' broad JobSeekers.ToListAsync). The proof does NOT rely on
// xUnit cross-class ordering: it drives a private base subclass's lifecycle explicitly within one
// test and asserts the post-dispose state directly.
[Collection("Api")]
public sealed class MalformedSeedIsolationTests(ApiFactory factory)
    : MalformedJsonbSeedTestBase(factory)
{
    // #352 (code-reviewer): the guard itself derives from the base so its own toxic seed is
    // cleared on class exit (and entry) even if an assertion below regresses and throws BEFORE
    // the explicit inner-fixture DisposeAsync — the isolation guard must never become the
    // polluter. The inner JobSeekerSeedFixture lifecycle is still driven manually below; that is
    // the unit under test.
    protected override IReadOnlyList<string> TablesToClear => ["job_seekers"];

    // A concrete subclass standing in for any real malformed-jsonb seeder. job_seekers is the
    // table the broad-load hazard reads (match_preferences VO converter materializes per row).
    private sealed class JobSeekerSeedFixture(ApiFactory factory) : MalformedJsonbSeedTestBase(factory)
    {
        protected override IReadOnlyList<string> TablesToClear => ["job_seekers"];
    }

    [Fact]
    public async Task DisposeAsync_ShouldClearToxicRows_SoABroadLoadSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new JobSeekerSeedFixture(Factory);

        // Initialize() clears any inherited state; then seed a valid seeker and overwrite its
        // match_preferences with a KNOWN-TOXIC value the EF VO converter cannot read back
        // (ExperienceYears:"five" — a string in the numeric key, same fail-closed shape the
        // MatchPreferences back-compat suite seeds). Before the fix, a broad load would throw.
        await ((IAsyncLifetime)fixture).InitializeAsync();
        var seekerId = await SeedToxicJobSeekerAsync(ct);

        // A broad load now MUST throw (proves the toxic row is genuinely unreadable, so the
        // guard is meaningful — a DELETE that bypasses the converter is the only safe removal).
        await Should.ThrowAsync<Exception>(() => LoadAllJobSeekersAsync(ct));

        // Drive dispose explicitly — this is the invariant under test.
        await ((IAsyncLifetime)fixture).DisposeAsync();

        // After dispose the toxic row is gone, so the same broad load succeeds and is empty.
        var afterDispose = await LoadAllJobSeekersAsync(ct);
        afterDispose.ShouldBeEmpty();

        // Defensive: the seeded id is specifically absent (not merely "no rows by accident").
        afterDispose.ShouldNotContain(js => js.Id == seekerId);
    }

    // Seeds a valid JobSeeker via the aggregate (EF fills all required columns), then mutates
    // ONLY match_preferences with raw jsonb to a value the converter fails to read — reusing the
    // raw-seed idiom from MatchPreferencesJsonbBackcompatTests.
    private async Task<JobSeekerId> SeedToxicJobSeekerAsync(CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Toxic Seed", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE job_seekers SET match_preferences = @json::jsonb WHERE id = @id";
        cmd.Parameters.AddWithValue("json", """{"ExperienceYears":"five"}""");
        cmd.Parameters.AddWithValue("id", seeker.Id.Value);
        await cmd.ExecuteNonQueryAsync(ct);
        await conn.CloseAsync();
        return seeker.Id;
    }

    private async Task<List<JobSeeker>> LoadAllJobSeekersAsync(CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().ToListAsync(ct);
    }
}
