using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
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
/// CV-pivot PR 5b (security-bind B1/B4, CTO-bind M-G) — the consent-gated original-file capture
/// driven by the REAL <see cref="ImportResumeCommandHandler"/> with the REAL
/// <c>IBinaryFieldSealer</c> against REAL Postgres (Testcontainers via
/// <see cref="WorkerTestFixture"/>). The parse ports are substituted (their breadth is
/// unit-covered); THIS suite proves what the fake DbContext cannot:
///   1. A consented flagged capture persists the Art. 7(1) evidence pair through the EF mapping
///      (<c>pnr_consent_at</c> / <c>pnr_consent_dialog_version</c> on disk, EF read-back
///      round-trip), the file content on disk is the Form C seal (never the plaintext upload),
///      and the DISTINCT <c>ResumeFile.PnrStorageConsented</c> audit row is in the same database.
///   2. A clean capture persists NULL in both consent columns — the same on-disk state every
///      pre-migration row has, proving null is the correct backfill-free historical default.
/// </summary>
// CA2012: NSubstitute stubbing of ValueTask-returning port members is the known analyzer
// false positive (parity with the handler unit tests).
#pragma warning disable CA2012
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class PnrConsentCaptureEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    // A distinctive plaintext marker INSIDE the upload bytes — the on-disk content must
    // never carry it (Form C seal, not the plaintext).
    private const string PlaintextMarker = "PII-CONSENT-CAPTURE-5B-4419";
    private static readonly byte[] UploadBytes =
        [.. "%PDF-1.7 "u8.ToArray(), .. Encoding.UTF8.GetBytes(PlaintextMarker)];

    private const string FlaggedRawText = "Anna Andersson\nPnr 811218-9876";
    private const string CleanRawText = "Anna Andersson\nanna@example.com";

    private async Task<JobSeeker> SeedJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            userId, "Anna Kontosson", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
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

    // The REAL db + clock + sealer from the warmed scope; the parse ports and audit context
    // are substituted (unit-covered breadth; Worker DI has no HTTP request context).
    private static ImportResumeCommandHandler BuildHandler(
        IServiceScope scope, Guid userId, Guid correlationId, string rawText)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var extractor = Substitute.For<ICvTextExtractor>();
        extractor.Extract(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>(), Arg.Any<CancellationToken>())
            .Returns(new CvExtractionResult(rawText, CvExtractionStatus.Extracted));

        var layoutAnalyzer = Substitute.For<ICvLayoutAnalyzer>();
        layoutAnalyzer.Analyze(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>(), Arg.Any<CancellationToken>())
            .Returns(CvLayoutMetrics.NotApplicable(UploadBytes.Length));

        var segmenter = Substitute.For<IResumeSegmenter>();
        segmenter.Segment(Arg.Any<string>()).Returns(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", null)),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
            ])));

        var deriver = Substitute.For<IOccupationCodeDeriver>();
        var experienceDeriver = Substitute.For<IOccupationExperienceDeriver>();
        experienceDeriver
            .DeriveApproximateYearsAsync(Arg.Any<IReadOnlyList<ParsedExperience>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>()));
        var skillResolver = Substitute.For<ISkillResolver>();
        skillResolver.ResolveDetailed(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.Current.Returns(correlationId);
        var requestContext = Substitute.For<IRequestContextProvider>();
        requestContext.IpAddress.Returns((string?)null);
        requestContext.UserAgent.Returns((string?)null);

        return new ImportResumeCommandHandler(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            currentUser,
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            extractor,
            layoutAnalyzer,
            segmenter,
            deriver,
            experienceDeriver,
            skillResolver,
            scope.ServiceProvider.GetRequiredService<IBinaryFieldSealer>(),
            correlation,
            requestContext);
    }

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

    [Fact]
    public async Task Import_FlaggedWithAcknowledge_PersistsEvidencePair_SealedContent_DistinctAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var seeker = await SeedJobSeekerAsync(userId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var handler = BuildHandler(scope, userId, correlationId, FlaggedRawText);

            var result = await handler.Handle(
                new ImportResumeCommand("cv.pdf", "application/pdf", UploadBytes,
                    PersonnummerAcknowledged: true), ct);

            result.IsSuccess.ShouldBeTrue();
            result.Value.Personnummer.Found.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // EF read-back round-trip: the evidence pair materializes through the new mapping.
        var file = await verifyDb.ResumeFiles.AsNoTracking()
            .SingleAsync(f => f.JobSeekerId == seeker.Id, ct);
        file.PnrFlagged.ShouldBeTrue();
        file.PnrConsentAt.ShouldNotBeNull();
        file.PnrConsentDialogVersion.ShouldBe(PnrConsentDialog.Version);

        // On disk: the evidence columns hold values; the content is the Form C seal, never
        // the plaintext upload (the marker must not appear in the stored bytes).
        var consentAt = await RawScalarAsync(
            verifyDb, $"SELECT pnr_consent_at FROM resume_files WHERE id = '{file.Id.Value}'", ct);
        consentAt.ShouldNotBeNull();
        var dialogVersion = await RawScalarAsync(
            verifyDb,
            $"SELECT pnr_consent_dialog_version FROM resume_files WHERE id = '{file.Id.Value}'", ct);
        dialogVersion.ShouldBe(PnrConsentDialog.Version);
        var markerInContent = await RawScalarAsync(
            verifyDb,
            $"SELECT position('{PlaintextMarker}'::bytea IN content) FROM resume_files WHERE id = '{file.Id.Value}'",
            ct);
        markerInContent.ShouldBe("0"); // Postgres position() = 0 when absent

        // The DISTINCT consent audit row rides the same database, keyed to the FILE aggregate.
        var auditEvent = await RawScalarAsync(
            verifyDb,
            $"SELECT event_type FROM audit_log WHERE aggregate_id = '{file.Id.Value}'", ct);
        auditEvent.ShouldBe(ImportResumeCommand.PnrConsentAuditEventType);
        var auditAggregate = await RawScalarAsync(
            verifyDb,
            $"SELECT aggregate_type FROM audit_log WHERE aggregate_id = '{file.Id.Value}'", ct);
        auditAggregate.ShouldBe("ResumeFile");
    }

    [Fact]
    public async Task Import_CleanFile_PersistsNullConsentColumns_NoConsentAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var seeker = await SeedJobSeekerAsync(userId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var handler = BuildHandler(scope, userId, Guid.NewGuid(), CleanRawText);

            var result = await handler.Handle(
                new ImportResumeCommand("cv.pdf", "application/pdf", UploadBytes), ct);

            result.IsSuccess.ShouldBeTrue();
            result.Value.Personnummer.Found.ShouldBeFalse();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // NULL on disk — the exact state every pre-migration row has (backfill-free default).
        var file = await verifyDb.ResumeFiles.AsNoTracking()
            .SingleAsync(f => f.JobSeekerId == seeker.Id, ct);
        file.PnrFlagged.ShouldBeFalse();
        var consentAt = await RawScalarAsync(
            verifyDb, $"SELECT pnr_consent_at FROM resume_files WHERE id = '{file.Id.Value}'", ct);
        consentAt.ShouldBeNull();
        var dialogVersion = await RawScalarAsync(
            verifyDb,
            $"SELECT pnr_consent_dialog_version FROM resume_files WHERE id = '{file.Id.Value}'", ct);
        dialogVersion.ShouldBeNull();

        var consentAudit = await RawScalarAsync(
            verifyDb,
            $"SELECT count(*) FROM audit_log WHERE event_type = '{ImportResumeCommand.PnrConsentAuditEventType}' AND aggregate_id = '{file.Id.Value}'",
            ct);
        consentAudit.ShouldBe("0");
    }
}
