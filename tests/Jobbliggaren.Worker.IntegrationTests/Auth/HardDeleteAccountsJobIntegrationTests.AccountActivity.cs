using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Auth;

public partial class HardDeleteAccountsJobIntegrationTests
{
    /// <summary>
    /// #2161 (ADR 0172 D9) — <c>account_activity</c> is outside <c>AccountHardDeleteCascadeFitnessTests</c>, which
    /// partitions aggregate roots, so this is its oracle: the record the production recorder wrote while the account
    /// could sign in is gone with the profile row, by the database cascade, and another account's record stays. The
    /// count reads the table by the erased profile's id, so a record left behind without its profile is counted too.
    /// </summary>
    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToAccountActivity()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var deletedAt = now.AddDays(-(RestoreWindowDays + 1));
        var recordedAt = deletedAt.AddDays(-2);

        var erasedUser = await SeedActiveAccountAsync(ct, registeredAt: recordedAt.AddDays(-1));
        var keptUser = await SeedActiveAccountAsync(ct, registeredAt: recordedAt.AddDays(-1));
        await RecordLoginAsync(erasedUser, recordedAt, ct);
        await RecordLoginAsync(keptUser, recordedAt, ct);
        var erased = await JobSeekerIdOfAsync(erasedUser, ct);
        var kept = await JobSeekerIdOfAsync(keptUser, ct);
        await SoftDeleteAsync(erasedUser, deletedAt, ct);
        (await CountActivityAsync(erased, ct)).ShouldBe(1, "precondition: the record exists before the hard delete");

        await RunJobAsync(now, ct);

        (await CountActivityAsync(erased, ct)).ShouldBe(0, "the record goes with its profile row (Art. 17, ADR 0172 D2)");
        (await CountActivityAsync(kept, ct)).ShouldBe(1, "another account's record is untouched");
    }

    private async Task RecordLoginAsync(Guid userId, DateTimeOffset at, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var recorder = new SqlAccountActivityRecorder(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), new AccountActivityClaims(), new FixedClock(at),
            NullLogger<SqlAccountActivityRecorder>.Instance);
        await recorder.RecordLoginAsync(userId, ct);
    }

    /// <summary>The account holder's own deletion: <see cref="JobSeeker.SoftDelete"/> under the lifecycle scope.</summary>
    private async Task SoftDeleteAsync(Guid userId, DateTimeOffset deletedAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([userId], lifecycle: true, ct);
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.SoftDelete(new FixedClock(deletedAt));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<int> CountActivityAsync(JobSeekerId jobSeekerId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::int FROM account_activity WHERE job_seeker_id = @id", connection);
        command.Parameters.AddWithValue("id", jobSeekerId.Value);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }
}
