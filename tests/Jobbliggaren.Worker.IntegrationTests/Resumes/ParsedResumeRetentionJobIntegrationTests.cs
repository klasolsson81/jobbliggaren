using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Resumes.Jobs.ParsedResumeRetention;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Resumes;

/// <summary>
/// TD-111 — Testcontainers integration test for <see cref="ParsedResumeRetentionJob"/> +
/// <see cref="ParsedResumeRetentionWorker"/> against REAL Postgres. This MUST be a real-DB test:
/// the job uses set-based <c>ExecuteDeleteAsync</c> (EF-InMemory does not support it), and the
/// retention sweep targets the encrypted <c>parsed_resumes</c> aggregate that cannot be
/// materialised DEK-free. It proves: (1) the Worker DI chain resolves the new job (TD-103 —
/// ValidateOnBuild=false → first surfaces at invocation); (2) the 3-arm sweep deletes matured
/// Discarded/Promoted (DeletedAt &lt; now-30d) + abandoned PendingReview (CreatedAt &lt; now-90d,
/// DeletedAt null) and leaves fresh/immature rows; (3) <c>p.Status == ParsedResumeStatus.X</c> +
/// <c>IgnoreQueryFilters</c> + the set-delete round-trip against real Postgres.
/// <para>
/// Rows are seeded via RAW SQL (not the domain) to bypass the write-side field-encryption pipeline
/// — the retention delete never reads the CV-PII, so dummy ciphertext suffices; the assertion reads
/// back only <c>Id</c> via projection (never materialising the encrypted aggregate). No FK on
/// <c>job_seeker_id</c> (ADR 0011 soft-reference), so no JobSeeker seed is needed.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ParsedResumeRetentionJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    [Fact]
    public async Task RunAsync_ResolvesFromDI_AndDoesNotThrow_WhenNothingToPrune()
    {
        var ct = TestContext.Current.CancellationToken;
        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<ParsedResumeRetentionWorker>();
            await worker.RunAsync(ct);
        });
    }

    [Fact]
    public async Task RunAsync_PrunesMaturedRows_KeepsFreshAndImmature()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobSeekerId = Guid.NewGuid(); // no FK — soft-reference (ADR 0011)
        Guid[] deletedIds;
        Guid[] keptIds;

        // Seed relative to the SAME clock the job uses, so the test is deterministic regardless of
        // whether the fixture's IDateTimeProvider is real or fixed.
        using (var seedScope = _fixture.Services.CreateScope())
        {
            var now = seedScope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // DELETED (matured): Discarded/Promoted DeletedAt < now-30d; PendingReview CreatedAt < now-90d.
            var discardedOld = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Discarded, now.AddDays(-100), now.AddDays(-40), ct);
            var promotedOld = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Promoted, now.AddDays(-100), now.AddDays(-40), ct);
            var pendingAbandoned = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.PendingReview, now.AddDays(-100), null, ct);
            // A Discarded row aged 60d (between the 30d and 90d windows) MUST be pruned — proves the
            // Discarded arm uses the 30d cutoff, not the 90d pendingCutoff (guards the window coupling).
            var discardedMidWindow = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Discarded, now.AddDays(-100), now.AddDays(-60), ct);

            // KEPT (within window / fresh):
            var discardedFresh = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Discarded, now.AddDays(-20), now.AddDays(-10), ct);
            var promotedFresh = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Promoted, now.AddDays(-20), now.AddDays(-10), ct);
            var pendingFresh = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.PendingReview, now.AddDays(-10), null, ct);
            // A soft-deleted PendingReview (DeletedAt set, CreatedAt fresh) MUST survive — proves the
            // PendingReview arm's `DeletedAt == null` guard (an already-discarded/promoted-then... row
            // with status still PendingReview is not the abandoned-upload case the 90d arm targets).
            var pendingSoftDeleted = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.PendingReview, now.AddDays(-10), now.AddDays(-40), ct);

            deletedIds = [discardedOld, promotedOld, pendingAbandoned, discardedMidWindow];
            keptIds = [discardedFresh, promotedFresh, pendingFresh, pendingSoftDeleted];
        }

        using (var runScope = _fixture.Services.CreateScope())
        {
            var worker = runScope.ServiceProvider.GetRequiredService<ParsedResumeRetentionWorker>();
            await worker.RunAsync(ct);
        }

        using (var assertScope = _fixture.Services.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Project Id only (IgnoreQueryFilters to see soft-deleted) — never materialise the
            // encrypted aggregate (no warmed DEK here).
            var surviving = await db.ParsedResumes
                .IgnoreQueryFilters()
                .Where(p => p.JobSeekerId == new Jobbliggaren.Domain.JobSeekers.JobSeekerId(jobSeekerId))
                .Select(p => p.Id.Value)
                .ToListAsync(ct);

            foreach (var deleted in deletedIds)
                surviving.ShouldNotContain(deleted, "matured staging row must be pruned");
            foreach (var kept in keptIds)
                surviving.ShouldContain(kept, "fresh/immature staging row must survive");
        }
    }

    [Fact]
    public async Task RunAsync_StrictCutoff_KeepsRowExactlyAt30d_PrunesOneSecondPast()
    {
        // Pins the strict `<` boundary deterministically: the functional test seeds with ±10d
        // margin (the shared fixture uses a real wall-clock), so this test instantiates the job
        // DIRECTLY with a FROZEN clock + the fixture's AppDbContext to seed exactly at the cutoff.
        var ct = TestContext.Current.CancellationToken;
        var jobSeekerId = Guid.NewGuid();
        var fixedNow = new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
        Guid atBoundary, justPast;

        using (var seedScope = _fixture.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            // DeletedAt == cutoff (now-30d): strict `<` KEEPS it.
            atBoundary = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Discarded, fixedNow.AddDays(-31), fixedNow.AddDays(-30), ct);
            // DeletedAt one second past the cutoff: PRUNED.
            justPast = await SeedRawAsync(db, jobSeekerId, ParsedResumeStatus.Discarded, fixedNow.AddDays(-31), fixedNow.AddDays(-30).AddSeconds(-1), ct);
        }

        using (var runScope = _fixture.Services.CreateScope())
        {
            var db = runScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new ParsedResumeRetentionJob(db, new FixedClock(fixedNow), NullLogger<ParsedResumeRetentionJob>.Instance);
            await job.RunAsync(ct);
        }

        using (var assertScope = _fixture.Services.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var surviving = await db.ParsedResumes
                .IgnoreQueryFilters()
                .Where(p => p.JobSeekerId == new Jobbliggaren.Domain.JobSeekers.JobSeekerId(jobSeekerId))
                .Select(p => p.Id.Value)
                .ToListAsync(ct);

            surviving.ShouldContain(atBoundary, "a row aged EXACTLY 30d is kept (strict <, not <=)");
            surviving.ShouldNotContain(justPast, "a row one second past 30d is pruned");
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static async Task<Guid> SeedRawAsync(
        AppDbContext db, Guid jobSeekerId, ParsedResumeStatus status,
        DateTimeOffset createdAt, DateTimeOffset? deletedAt, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        // Raw INSERT — bypasses the field-encryption write interceptor (dummy ciphertext; the
        // delete never reads it). skill_proposals defaults '[]'; parsed_content_enc nullable; xmin system.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO parsed_resumes
                (id, job_seeker_id, source_file_name, source_content_type, detected_language, status,
                 raw_text, parse_confidence, personnummer_scan, occupation_proposals, created_at, updated_at, deleted_at)
            VALUES
                ({id}, {jobSeekerId}, 'cv.pdf', 'application/pdf', 1, {status.Name},
                 'ciphertext', '{{}}'::jsonb, '{{}}'::jsonb, '[]'::jsonb, {createdAt}, {createdAt}, {deletedAt})", ct);
        return id;
    }
}
