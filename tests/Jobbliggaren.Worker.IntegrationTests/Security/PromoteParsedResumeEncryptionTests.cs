using System.Data.Common;
using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Queries;
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
/// Fas 4 STEG A PR-2 (ADR 0074 Invariant 3) — the deterministic NO-AI promote of a
/// PendingReview <see cref="ParsedResume"/> into a canonical <see cref="Resume"/>, driven by the
/// REAL <see cref="PromoteParsedResumeCommandHandler"/> against REAL Postgres (Testcontainers via
/// <see cref="WorkerTestFixture"/>; InMemory forbidden — the interceptor↔Npgsql materialization
/// order is load-bearing). Mirrors the seeding/DEK mechanics of
/// <see cref="ParsedResumeEncryptionTests"/> + <see cref="ResumeContentEncryptionTests"/>.
///
/// The load-bearing assertions (ADR 0074 Invariant 3 carried through the promote path):
///   1. The handler runs in a warmed-DEK scope and persists a new Resume whose ONE Master
///      <c>resume_versions.content_enc</c> is ciphertext (<c>v1:</c> sentinel, the gap-filled PII
///      marker absent on disk) and round-trips to the gap-filled content on decrypt; the source
///      ParsedResume is <c>Promoted</c> with <c>deleted_at</c> set (CTO DQ7).
///   2. A cross-user promote attempt fails NotFound relationally (no Resume row, ParsedResume
///      untouched).
///   3. (Cheap fail-closed parity, F4-9): reading the promoted ParsedResume's encrypted CV-PII
///      without a warm owner DEK fails-closed (CryptographicException).
///
/// The handler's <see cref="ICurrentUser"/> is substituted so it resolves the seeded owner; the
/// DbContext + clock + failed-access logger + DEK come from the real DI scope.
///
/// SPEC-DRIVEN. RED until the command + handler + Resume.CreateFromParsed + ParsedResume.Promote
/// + ResumeContentMapper ship.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class PromoteParsedResumeEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    // A PII marker that must end up as ciphertext on disk (gap-filled content the user submits).
    private const string GapFilledNameMarker = "PII-PROMOTE-NAMN-F4A-3317";

    // ── Seeding ──────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(userId, "Promote Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static ParsedResumeContent ParsedContent() =>
        new(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: [new ParsedExperience("Backend-utvecklare", "Beta AB", "2021–", "raw entry")]);

    private static ParseConfidence ConfidentConfidence() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["name extracted"]),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, ["1 entries"]),
        ]);

    private async Task<(ParsedResumeId Id, JobSeekerId Owner)> SeedPendingReviewAsync(
        Guid userId, CancellationToken ct)
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
                ParsedContent(), "Anna Andersson\nBackend-utvecklare, Beta AB",
                ConfidentConfidence(), PersonnummerScanOutcome.None, [], clock).Value;
            id = parsed.Id;
            db.ParsedResumes.Add(parsed);
            await db.SaveChangesAsync(ct);
        }

        return (id, seeker.Id);
    }

    // Gap-filled DTO the user submits at promote-time — clean, no personnummer.
    private static ResumeContentDto GapFilledDto() => new(
        new PersonalInfoDto(GapFilledNameMarker, "anna@example.com", "0701234567", "Stockholm"),
        Experiences:
        [
            new ExperienceDto("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster."),
        ],
        Educations:
        [
            new EducationDto("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
        ],
        Skills: [new SkillDto("C#", 8), new SkillDto("PostgreSQL", 5)],
        Summary: "Erfaren backend-utvecklare.");

    private static async Task PrefetchOwnerDekAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek);
    }

    // Build the real handler in a warmed scope, wired to a substituted ICurrentUser for `userId`.
    private static PromoteParsedResumeCommandHandler BuildHandler(IServiceScope scope, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new PromoteParsedResumeCommandHandler(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            currentUser,
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            scope.ServiceProvider.GetRequiredService<IFailedAccessLogger>());
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

    // ── 1. Promote via the real handler → encrypted Resume on disk + Promoted ParsedResume ─

    [Fact]
    public async Task Promote_WithWarmDek_PersistsEncryptedResume_RoundTrips_AndSoftDeletesParsedAsPromoted()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (parsedId, owner) = await SeedPendingReviewAsync(userId, ct);

        Guid newResumeId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, owner, ct);
            var handler = BuildHandler(scope, userId);

            var result = await handler.Handle(
                new PromoteParsedResumeCommand(parsedId.Value, "Mitt importerade CV", GapFilledDto()), ct);

            result.IsSuccess.ShouldBeTrue();
            newResumeId = result.Value;

            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        var resumeId = new ResumeId(newResumeId);

        // Ciphertext on disk: the gap-filled name marker must NOT appear in plaintext.
        using (var verifyScope = _fixture.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contentEnc = await RawScalarAsync(
                verifyDb,
                $"SELECT content_enc FROM resume_versions WHERE resume_id = '{resumeId.Value}'",
                ct);
            contentEnc.ShouldNotBeNull();
            contentEnc.ShouldStartWith("v1:");
            contentEnc.ShouldNotContain(GapFilledNameMarker, Case.Sensitive);

            // The source ParsedResume is Promoted + soft-deleted (CTO DQ7).
            var status = await RawScalarAsync(
                verifyDb, $"SELECT status FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
            status.ShouldBe(ParsedResumeStatus.Promoted.Name);
            var deletedAt = await RawScalarAsync(
                verifyDb, $"SELECT deleted_at FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
            deletedAt.ShouldNotBeNull("ParsedResume must be soft-deleted on promote (CTO DQ7).");
        }

        // Round-trip: the encrypted Master content decrypts to the gap-filled content under a warm DEK.
        using (var readScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(readScope, owner, ct);
            var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var resume = await readDb.Resumes
                .AsNoTracking().Include(r => r.Versions)
                .SingleAsync(r => r.Id == resumeId, ct);

            resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe(GapFilledNameMarker);
            resume.MasterVersion.Content.Experiences.ShouldHaveSingleItem().Company.ShouldBe("Beta AB");
            resume.LatestRole.ShouldBe("Backend-utvecklare");
        }
    }

    // ── 2. Cross-user promote fails NotFound relationally (no Resume, ParsedResume untouched) ─

    [Fact]
    public async Task Promote_CrossUser_FailsNotFound_NoResumePersisted_ParsedResumeUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerUserId = Guid.NewGuid();
        var (parsedId, owner) = await SeedPendingReviewAsync(ownerUserId, ct);

        // A DIFFERENT authenticated user (with their own JobSeeker) attempts the promote.
        var attackerUserId = Guid.NewGuid();
        var attacker = await SeedJobSeekerAsync(attackerUserId, ct);

        Result<Guid> result;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, owner, ct); // even with the owner DEK warm, ownership fails
            var handler = BuildHandler(scope, attackerUserId);
            result = await handler.Handle(
                new PromoteParsedResumeCommand(parsedId.Value, "Stulet CV", GapFilledDto()), ct);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound");

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // No Resume was created for the attacker (the only owner a wrongly-succeeded promote
        // could have produced — the handler resolves jobSeekerId from the caller). Scoped to
        // the attacker's JobSeeker so the shared fixture DB's sibling-test rows don't pollute
        // the count.
        var resumeCount = await RawScalarAsync(
            verifyDb, $"SELECT count(*) FROM resumes WHERE job_seeker_id = '{attacker.Id.Value}'", ct);
        resumeCount.ShouldBe("0");

        // The ParsedResume is untouched: still PendingReview, not soft-deleted.
        var status = await RawScalarAsync(
            verifyDb, $"SELECT status FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
        status.ShouldBe(ParsedResumeStatus.PendingReview.Name);
        var deletedAt = await RawScalarAsync(
            verifyDb, $"SELECT deleted_at FROM parsed_resumes WHERE id = '{parsedId.Value}'", ct);
        deletedAt.ShouldBeNull();
    }

    // ── 3. Fail-closed parity: reading the promoted ParsedResume's CV-PII without a warm DEK ─

    [Fact]
    public async Task Promote_ThenReadPromotedParsedResume_WithoutWarmDek_FailsClosed_CryptographicException()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (parsedId, owner) = await SeedPendingReviewAsync(userId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, owner, ct);
            var handler = BuildHandler(scope, userId);
            var result = await handler.Handle(
                new PromoteParsedResumeCommand(parsedId.Value, "Mitt importerade CV", GapFilledDto()), ct);
            result.IsSuccess.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        // An authed owner scope but with the DEK deliberately NOT warmed: materializing the
        // promoted ParsedResume's encrypted CV-PII (it is retained as Promoted) must fail-closed.
        using var coldScope = _fixture.Services.CreateScope();
        coldScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().SetOwner(owner);
        var coldDb = coldScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Record.ExceptionAsync(async () =>
            await coldDb.ParsedResumes.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(p => p.Id == parsedId, ct));

        ex.ShouldNotBeNull();
        ex.ShouldBeOfType<CryptographicException>(
            "Reading the promoted ParsedResume's CV-PII without a warm owner DEK must fail-closed (Invariant 3).");
    }
}
