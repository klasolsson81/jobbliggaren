using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Auth;

/// <summary>
/// #1746 (test-writer Minor 6): <c>docs/runbooks/account-deletion.md</c> §4.3 step 2, the operator's deletion on a
/// mailed request, run against the collection's Postgres: the deletion trigger and the account's external logins in
/// one statement (security-auditor V1). The drill carries its own copy of the statement, never the runbook's text, and
/// <c>AccountDeletionRunbookParityTests</c> requires the copy to be the runbook's but for its two placeholders.
/// </summary>
[Collection("Worker")]
public class AccountDeletionOperatorPathDrillTests(WorkerTestFixture fixture)
{
    internal const string Statement = """
        WITH deleted AS (
            UPDATE job_seekers SET deleted_at = NOW()
            WHERE id = @jobSeekerId AND deleted_at IS NULL
              AND EXISTS (SELECT 1 FROM identity."AspNetUsers" u
                          WHERE u.id = job_seekers.user_id AND u.normalized_email = upper(@adress))
            RETURNING user_id
        ), unlinked AS (
            DELETE FROM identity."AspNetUserLogins" l USING deleted d
            WHERE l.user_id = d.user_id RETURNING l.user_id
        )
        SELECT d.user_id, (SELECT count(*) FROM unlinked) AS links_removed FROM deleted d;
        """;

    [Fact]
    public async Task The_statement_sets_the_trigger_and_erases_the_accounts_logins_once_and_for_it_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, jobSeekerId, address) = await SeedActiveAccountAsync(ct);
        var (bystanderId, _, _) = await SeedActiveAccountAsync(ct);
        await LinkEveryKnownProviderAsync(userId, ct);
        await LinkEveryKnownProviderAsync(bystanderId, ct);

        (await RunAsync(jobSeekerId, address, ct))
            .ShouldHaveSingleItem().ShouldBe((userId, (long)ExternalProviderKey.Known.Count));

        (await DeletedAtAsync(jobSeekerId, ct)).ShouldNotBeNull();
        (await LinksOfAsync(userId, ct)).ShouldBe(0);
        (await LinksOfAsync(bystanderId, ct)).ShouldBe(ExternalProviderKey.Known.Count);

        // Already deleted: no row back and nothing changed, as the runbook's "0 rader" says.
        (await RunAsync(jobSeekerId, address, ct)).ShouldBeEmpty();
        (await LinksOfAsync(bystanderId, ct)).ShouldBe(ExternalProviderKey.Known.Count);
    }

    [Fact]
    public async Task The_statement_changes_nothing_when_the_address_is_not_the_accounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, jobSeekerId, _) = await SeedActiveAccountAsync(ct);
        await LinkEveryKnownProviderAsync(userId, ct);

        (await RunAsync(jobSeekerId, $"someone-else-{Guid.NewGuid():N}@test.local", ct)).ShouldBeEmpty();

        (await DeletedAtAsync(jobSeekerId, ct)).ShouldBeNull();
        (await LinksOfAsync(userId, ct)).ShouldBe(ExternalProviderKey.Known.Count);
    }

    private async Task<List<(Guid UserId, long LinksRemoved)>> RunAsync(
        JobSeekerId jobSeekerId, string address, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(Statement, connection);
        command.Parameters.AddWithValue("jobSeekerId", jobSeekerId.Value);
        command.Parameters.AddWithValue("adress", address);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<(Guid, long)>();
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetGuid(0), reader.GetInt64(1)));
        return rows;
    }

    private async Task<(Guid UserId, JobSeekerId JobSeekerId, string Address)> SeedActiveAccountAsync(
        CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var address = $"drill-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = address, Email = address };
        (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var seeker = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock);
        seeker.IsSuccess.ShouldBeTrue();
        db.JobSeekers.Add(seeker.Value);
        await db.SaveChangesAsync(ct);

        return (user.Id, seeker.Value.Id, address);
    }

    // The production writer, as a login with each provider writes it; over Known, so a later provider is covered too.
    private async Task LinkEveryKnownProviderAsync(Guid userId, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var store = new IdentityExternalLoginStore(
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<IDbExceptionInspector>());
        foreach (var provider in ExternalProviderKey.Known)
        {
            var subject = ExternalSubject.TryCreate(Guid.NewGuid().ToString("N"))!.Value;
            (await store.LinkAsync(userId, provider, subject, ct)).ShouldBe(ExternalLinkResult.Linked);
        }
    }

    private async Task<int> LinksOfAsync(Guid userId, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .AsNoTracking()
            .CountAsync(login => login.UserId == userId, ct);
    }

    private async Task<DateTimeOffset?> DeletedAtAsync(JobSeekerId jobSeekerId, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(js => js.Id == jobSeekerId, ct))
            .DeletedAt;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
