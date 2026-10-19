using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #1663 PR 2 (ADR 0175 D5, D6) — Testcontainers persistence for <see cref="CompanyWatchCriterionAdHit"/> and the
/// criterion's scan state, against real Postgres: what InMemory cannot see is the foreign key's cascade, the
/// database defaults on the bell's flags, and the outcome stored by name.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchCriterionAdHitPersistenceTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;
    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 3, 55, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hit_round_trips_with_the_owner_taken_from_its_criterion_and_both_flags_false()
    {
        var owner = Guid.NewGuid();
        var (criterion, hit) = await SeedCriterionWithHitAsync(owner);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.CompanyWatchCriterionAdHits.AsNoTracking().SingleAsync(h => h.Id == hit.Id, Ct);

        reloaded.UserId.ShouldBe(owner);
        reloaded.CompanyWatchCriterionId.ShouldBe(criterion.Id);
        reloaded.JobAdId.ShouldBe(hit.JobAdId);
        reloaded.CreatedAt.ShouldBe(T0);
        reloaded.ReadInNotificationFeed.ShouldBeFalse();
        reloaded.DismissedFromNotificationFeed.ShouldBeFalse();
    }

    [Fact]
    public async Task DeletingTheCriterion_CascadesItsAdHits_AtTheDatabase()
    {
        // DPIA §D9 M-D8: the criterion's hard delete (DeleteCompanyWatchCriterionCommandHandler's tracked Remove)
        // touches no hit; the foreign key alone takes them, so a hit cannot outlive its criterion (Art. 5(1)(e)).
        var (criterion, _) = await SeedCriterionWithHitAsync(Guid.NewGuid());

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CompanyWatchCriteria.Remove(await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterion.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        using var readScope = _fixture.Services.CreateScope();
        var reader = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await reader.CompanyWatchCriterionAdHits.CountAsync(h => h.CompanyWatchCriterionId == criterion.Id, Ct))
            .ShouldBe(0);
    }

    [Fact]
    public async Task RecordScan_persists_the_outcome_by_name_and_the_watermark_without_touching_updated_at()
    {
        var (criterion, _) = await SeedCriterionWithHitAsync(Guid.NewGuid());
        var night = new FixedClock(T0.AddDays(1));

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterion.Id, Ct);
            tracked.RecordScan(CompanyWatchCriterionScanOutcome.TooBroad, night.UtcNow.AddMinutes(-2), night);
            await db.SaveChangesAsync(Ct);
        }

        using var readScope = _fixture.Services.CreateScope();
        var reader = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await reader.CompanyWatchCriteria.AsNoTracking().SingleAsync(c => c.Id == criterion.Id, Ct);
        reloaded.LastScanOutcome.ShouldBe(CompanyWatchCriterionScanOutcome.TooBroad);
        reloaded.ScannedThrough.ShouldBe(night.UtcNow.AddMinutes(-2));
        reloaded.UpdatedAt.ShouldBe(T0, "the materialisation sweep keys on updated_at; a scan must not move it");
        reloaded.CriteriaChangedAt.ShouldBe(T0);
        (await ReadOutcomeTextAsync(reader, criterion.Id)).ShouldBe("TooBroad");
    }

    private async Task<(CompanyWatchCriterion Criterion, CompanyWatchCriterionAdHit Hit)> SeedCriterionWithHitAsync(
        Guid owner)
    {
        var clock = new FixedClock(T0);
        var criterion = CompanyWatchCriterion.Create(
            owner, CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value, null, clock).Value;
        var hit = CompanyWatchCriterionAdHit.Create(criterion, JobAdId.New(), clock).Value;
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CompanyWatchCriteria.Add(criterion);
        db.CompanyWatchCriterionAdHits.Add(hit);
        await db.SaveChangesAsync(Ct);
        return (criterion, hit);
    }

    private static async Task<string?> ReadOutcomeTextAsync(AppDbContext db, CompanyWatchCriterionId id)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT last_scan_outcome FROM company_watch_criteria WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id.Value);
        return (string?)await command.ExecuteScalarAsync(Ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
