using System.Data.Common;
using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// CV-pivot PR 5a (CTO-bind 2026-07-17) — the auto-promote path driven by the REAL
/// <see cref="AutoPromoteParsedResumeCommandHandler"/> against REAL Postgres (Testcontainers
/// via <see cref="WorkerTestFixture"/>; InMemory forbidden — the interceptor↔Npgsql
/// materialization order is load-bearing). Mirrors the seeding/DEK mechanics of
/// <see cref="PromoteParsedResumeEncryptionTests"/>; the breadth of the clean-predicate and
/// mapping table is unit-covered — THIS suite proves what InMemory cannot:
///   1. The handler DECRYPTS the parse's Form-B content shadow under a warm owner DEK,
///      projects it, and persists a new Resume whose Master <c>content_enc</c> is ciphertext
///      (<c>v1:</c> sentinel, the profile PII marker absent on disk) that round-trips to the
///      verbatim content — with the ACCOUNT display name, never the file's contact name; the
///      source ParsedResume is <c>Promoted</c> + soft-deleted, and the distinct Art. 22
///      audit row (<c>Resume.AutoPromotedFromParsed</c>) is in the same database.
///   2. A LeftPending outcome persists NOTHING relationally: no resume row, the artifact
///      still <c>PendingReview</c> with <c>deleted_at</c> NULL, and no audit row — proven
///      on-disk, not just on the tracker.
/// </summary>
// CA2012: stubbing the ValueTask-returning ReconcileAsync is the known NSubstitute analyzer
// false positive (parity with the handler unit tests).
#pragma warning disable CA2012
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class AutoPromoteParsedResumeEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    // PII markers that must end up as ciphertext on disk. The profile marker rides the
    // parse's content shadow into the promoted Master's Summary; the file-name marker is
    // the contact name the canonical CV must NEVER carry.
    private const string ProfileMarker = "PII-AUTOPROMOTE-PROFIL-5A-7731";
    private const string ParsedContactName = "Fil Namnsson";
    private const string AccountDisplayName = "Anna Kontosson";

    // ── Seeding ──────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            userId, AccountDisplayName, new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static ParsedResumeContent CleanParsedContent(string? preamble = null) => new(
        new ParsedContact(ParsedContactName, "fil@example.com", "070-1234567", "Stockholm"),
        profile: ProfileMarker,
        experience: [new ParsedExperience("Backend-utvecklare", "Beta AB", "2019–2022", "raw entry")],
        preamble: preamble);

    private static ParseConfidence ConfidentConfidence() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["name extracted"]),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, ["1 entries"]),
        ]);

    private async Task<(ParsedResumeId Id, JobSeekerId Owner)> SeedPendingReviewAsync(
        Guid userId, CancellationToken ct, string? preamble = null)
    {
        var seeker = await SeedJobSeekerAsync(userId, ct);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        ParsedResumeId id;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var parsed = ParsedResume.Create(
                seeker.Id, "anna-cv.pdf", "application/pdf", ResumeLanguage.Sv,
                CleanParsedContent(preamble), $"{ParsedContactName}\nBackend-utvecklare, Beta AB",
                ConfidentConfidence(), PersonnummerScanOutcome.None, [], clock).Value;
            id = parsed.Id;
            db.ParsedResumes.Add(parsed);
            await db.SaveChangesAsync(ct);
        }

        return (id, seeker.Id);
    }

    private static async Task PrefetchOwnerDekAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek);
    }

    // Build the real handler in a warmed scope. ICurrentUser is substituted for `userId`;
    // the reconciler is a no-op substitute (this suite proves ENCRYPTION + relational
    // outcomes, not reconcile behavior — unit-covered); the audit context providers are
    // substituted (Worker DI has no HTTP request context) with fixed values the audit-row
    // assertion can read back.
    private static AutoPromoteParsedResumeCommandHandler BuildHandler(
        IServiceScope scope, Guid userId, Guid correlationId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var reconciler = Substitute.For<IResumeReviewReconciler>();
        reconciler.ReconcileAsync(
                Arg.Any<Resume>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.Current.Returns(correlationId);
        var requestContext = Substitute.For<IRequestContextProvider>();
        requestContext.IpAddress.Returns((string?)null);
        requestContext.UserAgent.Returns((string?)null);

        return new AutoPromoteParsedResumeCommandHandler(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            currentUser,
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            scope.ServiceProvider.GetRequiredService<IFailedAccessLogger>(),
            reconciler,
            correlation,
            requestContext);
    }

    // Raw column read past EF (bypasses the decrypt interceptor) — proves on-disk state.
    private static async Task<string?> RawScalarAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : raw.ToString();
    }

    // ── 1. Clean parse → encrypted Resume, account name, Promoted artifact, distinct audit ─

    [Fact]
    public async Task AutoPromote_CleanConfidentParse_PersistsEncryptedResume_AccountNamed_AuditedDistinctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var (parsedId, owner) = await SeedPendingReviewAsync(userId, ct);

        Guid newResumeId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, owner, ct);
            var handler = BuildHandler(scope, userId, correlationId);

            var result = await handler.Handle(
                new AutoPromoteParsedResumeCommand(parsedId.Value), ct);

            result.IsSuccess.ShouldBeTrue();
            newResumeId = result.Value
                .ShouldBeOfType<AutoPromoteOutcome.Promoted>().ResumeId;

            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        var resumeId = new ResumeId(newResumeId);

        using (var verifyScope = _fixture.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Ciphertext on disk: the parse-carried profile marker must NOT appear in plaintext.
            var contentEnc = await RawScalarAsync(
                verifyDb,
                $"SELECT content_enc FROM resume_versions WHERE resume_id = '{resumeId.Value}'",
                ct);
            contentEnc.ShouldNotBeNull();
            contentEnc.ShouldStartWith("v1:");
            contentEnc.ShouldNotContain(ProfileMarker, Case.Sensitive);

            // The source ParsedResume is Promoted + soft-deleted (CTO DQ7).
            var status = await RawScalarAsync(
                verifyDb, $"SELECT status FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
            status.ShouldBe(ParsedResumeStatus.Promoted.Name);
            var deletedAt = await RawScalarAsync(
                verifyDb, $"SELECT deleted_at FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
            deletedAt.ShouldNotBeNull();

            // The Art. 22 audit row rides the SAME transaction, with the DISTINCT event type
            // (machine-verbatim provenance, never the human-curated Resume.PromotedFromParsed).
            var auditEvent = await RawScalarAsync(
                verifyDb,
                $"SELECT event_type FROM audit_log WHERE aggregate_id = '{resumeId.Value}'",
                ct);
            auditEvent.ShouldBe(AutoPromoteParsedResumeCommand.AuditEventType);
        }

        // Round-trip under a warm DEK: verbatim content, ACCOUNT-named — never the file's name.
        using (var readScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(readScope, owner, ct);
            var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var resume = await readDb.Resumes
                .AsNoTracking().Include(r => r.Versions)
                .SingleAsync(r => r.Id == resumeId, ct);

            resume.Name.ShouldBe(AccountDisplayName);
            var content = resume.MasterVersion.Content;
            content.PersonalInfo.FullName.ShouldBe(AccountDisplayName);
            content.PersonalInfo.FullName.ShouldNotBe(ParsedContactName);
            content.Summary.ShouldBe(ProfileMarker);
            var exp = content.Experiences.ShouldHaveSingleItem();
            exp.Company.ShouldBe("Beta AB");
            exp.StartDate.ShouldBeNull();
            exp.RawPeriod.ShouldBe("2019–2022");
        }
    }

    // ── 2. LeftPending persists NOTHING — proven relationally, not on the tracker ─

    [Fact]
    public async Task AutoPromote_PreambleCarryingParse_LeftPending_NothingPersisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (parsedId, owner) = await SeedPendingReviewAsync(
            userId, ct, preamble: "Driven utvecklare nära produktionen.");

        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, owner, ct);
            var handler = BuildHandler(scope, userId, Guid.NewGuid());

            var result = await handler.Handle(
                new AutoPromoteParsedResumeCommand(parsedId.Value), ct);

            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBeOfType<AutoPromoteOutcome.LeftPending>()
                .Reason.ShouldBe(AutoPromoteBlockReason.UnclassifiedPreamble);

            // The unconditional UnitOfWork save runs in production regardless of outcome —
            // run it here too and prove it is a relational no-op.
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var resumeCount = await RawScalarAsync(
            verifyDb,
            $"SELECT count(*) FROM resumes r JOIN parsed_resumes p ON r.job_seeker_id = p.job_seeker_id WHERE p.id = '{parsedId.Value}'",
            ct);
        resumeCount.ShouldBe("0");

        var status = await RawScalarAsync(
            verifyDb, $"SELECT status FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
        status.ShouldBe(ParsedResumeStatus.PendingReview.Name);
        var deletedAt = await RawScalarAsync(
            verifyDb, $"SELECT deleted_at FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
        deletedAt.ShouldBeNull();

        var auditCount = await RawScalarAsync(
            verifyDb,
            $"SELECT count(*) FROM audit_log WHERE event_type = '{AutoPromoteParsedResumeCommand.AuditEventType}' AND user_id = '{userId}'",
            ct);
        auditCount.ShouldBe("0");
    }
}
