using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Resumes;

/// <summary>
/// #664 — Testcontainers integration test for <see cref="BackfillParsedResumeSourceFileNameMaskJob"/>
/// against REAL Postgres. This MUST be a real-DB test: the job uses set-based
/// <c>ExecuteUpdateAsync</c> over a DEK-free projection (EF-InMemory supports neither, and the point of
/// the mechanism is that it NEVER materialises the encrypted <c>parsed_resumes</c> aggregate). It
/// proves: (1) the job resolves from the Worker DI chain; (2) a live run masks the real personnummer in
/// <c>source_file_name</c> and leaves clean/already-masked rows untouched (idempotent-by-shape);
/// (3) <c>IgnoreQueryFilters</c> reaches soft-deleted (promoted/discarded) rows (B5); (4) a dry run
/// reports the candidate count but writes nothing.
/// <para>
/// Rows are seeded via RAW SQL (parity <see cref="ParsedResumeRetentionJobIntegrationTests"/>) to
/// bypass the field-encryption write pipeline — the job never reads the CV-PII, so dummy ciphertext
/// suffices; assertions read back only <c>source_file_name</c> (plaintext) via projection, never
/// materialising the encrypted aggregate. The shared "Worker" DB is contamination-prone, so every
/// assertion is scoped to a fresh <c>jobSeekerId</c> and count checks are lower-bounds (other rows in
/// the table carry no personnummer → they are Skipped, never Masked).
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class BackfillParsedResumeSourceFileNameMaskJobIntegrationTests(WorkerTestFixture fixture)
{
    // Synthetic test personnummer (parity PersonnummerRedactorTests): 811218-9876 → ******-****.
    private const string Pnr = "811218-9876";
    private const string PnrMasked = "******-****";

    private readonly WorkerTestFixture _fixture = fixture;

    [Fact]
    public async Task RunAsync_ResolvesFromDI_AndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var job = scope.ServiceProvider
                .GetRequiredService<BackfillParsedResumeSourceFileNameMaskJob>();
            await job.RunAsync(dryRun: true, ct);
        });
    }

    [Fact]
    public async Task RunAsync_Live_MasksPersonnummerFilenames_AndLeavesCleanAndAlreadyMasked()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobSeekerId = Guid.NewGuid();
        Guid pnrRow, cleanRow, alreadyMaskedRow;

        using (var seed = _fixture.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            pnrRow = await SeedRawAsync(db, jobSeekerId, $"CV_{Pnr}.pdf", ParsedResumeStatus.PendingReview, null, ct);
            cleanRow = await SeedRawAsync(db, jobSeekerId, "mitt_cv_2024.pdf", ParsedResumeStatus.PendingReview, null, ct);
            alreadyMaskedRow = await SeedRawAsync(db, jobSeekerId, $"CV_{PnrMasked}.pdf", ParsedResumeStatus.PendingReview, null, ct);
        }

        var counts = await RunAsync(dryRun: false, ct);

        counts.Masked.ShouldBeGreaterThanOrEqualTo(1, "the pnr row must be masked");
        (await NameOf(pnrRow, ct)).ShouldBe($"CV_{PnrMasked}.pdf", "the real personnummer is masked in place");
        (await NameOf(cleanRow, ct)).ShouldBe("mitt_cv_2024.pdf", "a filename with no personnummer is untouched");
        (await NameOf(alreadyMaskedRow, ct)).ShouldBe($"CV_{PnrMasked}.pdf", "an already-masked filename is a no-op");
    }

    [Fact]
    public async Task RunAsync_Live_CoversSoftDeletedRows_TheB5Witness()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobSeekerId = Guid.NewGuid();

        Guid softDeletedPnrRow;
        using (var seed = _fixture.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            // A promoted (soft-deleted, DeletedAt set) row still holds the plaintext filename — it MUST
            // be masked, else residual plaintext personnummer survives for the soft-deleted subset.
            softDeletedPnrRow = await SeedRawAsync(
                db, jobSeekerId, $"CV_{Pnr}.pdf", ParsedResumeStatus.Promoted,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ct);
        }

        var counts = await RunAsync(dryRun: false, ct);

        counts.SoftDeletedMasked.ShouldBeGreaterThanOrEqualTo(1, "IgnoreQueryFilters must reach the soft-deleted row");
        (await NameOf(softDeletedPnrRow, ct)).ShouldBe($"CV_{PnrMasked}.pdf", "a soft-deleted row's personnummer is masked too");
    }

    [Fact]
    public async Task RunAsync_DryRun_ReportsCandidate_ButWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobSeekerId = Guid.NewGuid();

        Guid pnrRow;
        using (var seed = _fixture.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            pnrRow = await SeedRawAsync(db, jobSeekerId, $"CV_{Pnr}.pdf", ParsedResumeStatus.PendingReview, null, ct);
        }

        var counts = await RunAsync(dryRun: true, ct);

        counts.DryRun.ShouldBeTrue();
        counts.Masked.ShouldBeGreaterThanOrEqualTo(1, "the dry run reports the candidate it WOULD mask");
        (await NameOf(pnrRow, ct)).ShouldBe($"CV_{Pnr}.pdf", "a dry run writes NOTHING — the plaintext is still there");
    }

    [Fact]
    public async Task RunAsync_Idempotent_SecondLiveRunMasksTheRowOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobSeekerId = Guid.NewGuid();

        Guid pnrRow;
        using (var seed = _fixture.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            pnrRow = await SeedRawAsync(db, jobSeekerId, $"CV_{Pnr}.pdf", ParsedResumeStatus.PendingReview, null, ct);
        }

        await RunAsync(dryRun: false, ct);
        (await NameOf(pnrRow, ct)).ShouldBe($"CV_{PnrMasked}.pdf");

        // A re-run must find this row already masked (a *-run is not personnummer-shaped) → no-op. We
        // cannot filter the global counts by owner, so we witness idempotency by state: still exactly
        // the masked form, never double-masked or corrupted.
        await RunAsync(dryRun: false, ct);
        (await NameOf(pnrRow, ct)).ShouldBe($"CV_{PnrMasked}.pdf", "a second run leaves the already-masked row exactly as-is");
    }

    private async Task<ParsedResumeSourceFileNameMaskBackfillCounts> RunAsync(bool dryRun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var job = new BackfillParsedResumeSourceFileNameMaskJob(
            db, clock,
            Options.Create(new BackfillParsedResumeSourceFileNameMaskOptions()),
            NullLogger<BackfillParsedResumeSourceFileNameMaskJob>.Instance);
        return await job.RunAsync(dryRun, ct);
    }

    private async Task<string?> NameOf(Guid id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Project the plaintext column only (IgnoreQueryFilters to see soft-deleted) — never
        // materialise the encrypted aggregate (no warmed DEK here).
        return await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Id == new ParsedResumeId(id))
            .Select(p => p.SourceFileName)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<Guid> SeedRawAsync(
        AppDbContext db, Guid jobSeekerId, string sourceFileName, ParsedResumeStatus status,
        DateTimeOffset? deletedAt, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Raw INSERT — bypasses the field-encryption write interceptor (dummy ciphertext; the job never
        // reads raw_text/parsed_content). source_file_name is the plaintext column under test.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO parsed_resumes
                (id, job_seeker_id, source_file_name, source_content_type, detected_language, status,
                 raw_text, parse_confidence, personnummer_scan, occupation_proposals, created_at, updated_at, deleted_at)
            VALUES
                ({id}, {jobSeekerId}, {sourceFileName}, 'application/pdf', 1, {status.Name},
                 'ciphertext', '{{}}'::jsonb, '{{}}'::jsonb, '[]'::jsonb, {createdAt}, {createdAt}, {deletedAt})", ct);
        return id;
    }
}
