using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.ImportResume;

// Fas 4 STEG 8 (F4-8, ADR 0074) — the import/parse orchestration handler. THIN: the
// Infrastructure ports (ICvTextExtractor, IResumeSegmenter, IOccupationCodeDeriver) do
// the heavy lifting and are NSubstitute-mocked. The handler's own logic under test:
// the file-format gate, the personnummer guard call-site (scan on the RAW text BEFORE
// persist), the extraction→Failed-confidence fallback, the SSYK call-site (only when a
// title exists), and the response mapping. The PERSISTED RawText is the ORIGINAL
// extracted text — never the personnummer-normalized scan-copy.
//
// CA2012: NSubstitute stubbing of ValueTask-returning port members is a known
// analyzer false positive (the call is intercepted to register Returns, never
// consumed). Suppression scoped to mock setup (parity DeriveOccupationCodesQueryHandlerTests).
#pragma warning disable CA2012
public class ImportResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvTextExtractor _extractor = Substitute.For<ICvTextExtractor>();
    // Fas 4b PR-6b — the handler gained ICvLayoutAnalyzer (inserted BETWEEN extractor and
    // segmenter). Default stub returns NotApplicable so the general cases are unaffected; the
    // dedicated wiring test below re-stubs it to prove the metrics flow into the ParsedResume.
    private readonly ICvLayoutAnalyzer _layoutAnalyzer = Substitute.For<ICvLayoutAnalyzer>();
    private readonly IResumeSegmenter _segmenter = Substitute.For<IResumeSegmenter>();
    private readonly IOccupationCodeDeriver _deriver = Substitute.For<IOccupationCodeDeriver>();
    private readonly IOccupationExperienceDeriver _experienceDeriver =
        Substitute.For<IOccupationExperienceDeriver>();
    private readonly ISkillResolver _skillResolver = Substitute.For<ISkillResolver>();
    // Fas 4b PR-9a — the handler gained IBinaryFieldSealer (explicit Form C seal of the
    // original bytes, CTO Q2). Faked: the real sealer needs a warmed scoped DEK cache
    // (Infrastructure); the handler contract under test is the capture GATE + wiring.
    private readonly IBinaryFieldSealer _sealer = Substitute.For<IBinaryFieldSealer>();
    // CV-pivot 5b — the handler gained the audit providers for the IN-HANDLER consent audit
    // row (M-D: the blanket AuditBehavior slot is already "Resume.Imported").
    private readonly ICorrelationIdProvider _correlationId = Substitute.For<ICorrelationIdProvider>();
    private readonly IRequestContextProvider _requestContext = Substitute.For<IRequestContextProvider>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _correlationGuid = Guid.NewGuid();

    // "%PDF-1.7" — a real PDF magic prefix so CvFileSignature resolves Pdf.
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    // The fake sealer's opaque "ciphertext" — distinct from PdfBytes so the tests can
    // assert the aggregate stored the SEALED content, never the plaintext upload.
    private static readonly byte[] SealedBytes = [0x01, 0xAA, 0xBB, 0xCC, 0xDD];

    public ImportResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Default: the skill resolver resolves nothing (most cases assert occupation behaviour).
        // ADR 0079 STEG 3 added ISkillResolver as the handler's 7th dependency.
        _skillResolver.ResolveDetailed(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        // Default: no per-occupation experience attributed (most cases assert other behaviour).
        // ADR 0079-amendment (exp-per-occ PR-2) added IOccupationExperienceDeriver.
        _experienceDeriver
            .DeriveApproximateYearsAsync(Arg.Any<IReadOnlyList<ParsedExperience>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyDictionary<string, int>>(
                new Dictionary<string, int>()));
        // Default: geometry not analysable (harmless for the general cases — LayoutMetrics is not
        // inspected there). The wiring test re-stubs this to an Analyzed value (last-wins).
        _layoutAnalyzer.Analyze(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>(), Arg.Any<CancellationToken>())
            .Returns(CvLayoutMetrics.NotApplicable(0));
        // Default: the sealer returns a fixed opaque envelope (the capture tests assert the
        // aggregate stores exactly this, never the plaintext bytes).
        _sealer.Seal(Arg.Any<ReadOnlyMemory<byte>>()).Returns(SealedBytes);
        _correlationId.Current.Returns(_correlationGuid);
        _requestContext.IpAddress.Returns("203.0.113.7");
        _requestContext.UserAgent.Returns("test-agent");
    }

    private ImportResumeCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _extractor, _layoutAnalyzer, _segmenter,
            _deriver, _experienceDeriver, _skillResolver, _sealer, _correlationId, _requestContext);

    private static ImportResumeCommand PdfCommand(
        string fileName = "cv.pdf", bool personnummerAcknowledged = false) =>
        new(fileName, "application/pdf", PdfBytes, personnummerAcknowledged);

    private async Task<JobSeeker> SeedJobSeekerAsync(Infrastructure.Persistence.AppDbContext db)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seeker;
    }

    private void StubExtractor(string rawText, CvExtractionStatus status) =>
        _extractor.Extract(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>(), Arg.Any<CancellationToken>())
            .Returns(new CvExtractionResult(rawText, status));

    private void StubSegmenter(ResumeSegmentationResult result) =>
        _segmenter.Segment(Arg.Any<string>()).Returns(result);

    private void StubDeriver(params OccupationCandidate[] candidates) =>
        _deriver.DeriveManyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(
                new OccupationDerivationResult("title", candidates)));

    private void StubSkillResolver(params ResolvedSkill[] resolved) =>
        _skillResolver.ResolveDetailed(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(resolved);

    private void StubExperienceYears(params (string ConceptId, int Years)[] years) =>
        _experienceDeriver
            .DeriveApproximateYearsAsync(Arg.Any<IReadOnlyList<ParsedExperience>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyDictionary<string, int>>(
                years.ToDictionary(y => y.ConceptId, y => y.Years, StringComparer.Ordinal)));

    private static ResumeSegmentationResult ConfidentSegmentation(
        string? experienceTitle = "Backend-utvecklare") =>
        new(
            new ParsedResumeContent(
                new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", null),
                experience: experienceTitle is null
                    ? []
                    : [new ParsedExperience(experienceTitle, "Acme AB", "2021–2024", "raw entry")]),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ]));

    // ===============================================================
    // Happy path
    // ===============================================================

    [Fact]
    public async Task Handle_ValidPdf_AddsParsedResume_ReturnsSuccessWithMappedResponse()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nanna@example.com", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver(new OccupationCandidate(
            "q8wL_kdi_WaW", "Systemutvecklare",
            OccupationMatchKind.ExactOccupationName, "Backend-utvecklare"));

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        result.Value.ParsedResumeId.ShouldBe(added.Id.Value);
        result.Value.DetectedLanguage.ShouldBe(ResumeLanguage.Sv.Name);
        result.Value.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident.ToString());
        result.Value.Confidence.RequiresManualReview.ShouldBeFalse();
        result.Value.Personnummer.Found.ShouldBeFalse();
        result.Value.OccupationProposal.Count.ShouldBe(1);
        result.Value.OccupationProposal[0].OccupationGroupLabel.ShouldBe("Systemutvecklare");
    }

    [Fact]
    public async Task Handle_ValidPdf_PassesTheAnalyzersLayoutMetricsIntoTheCreatedParsedResume()
    {
        // Fas 4b PR-6b wiring: the handler runs ICvLayoutAnalyzer on the SAME bytes and passes
        // the resulting metrics into ParsedResume.Create. Stub a distinctive Analyzed value and
        // assert it is carried verbatim onto the persisted aggregate (CvLayoutMetrics is a record
        // → value equality). This is the seam that proves the analyzer is wired at all.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nanna@example.com", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver();

        var metrics = CvLayoutMetrics.Analyzed(fileSizeBytes: 1234, pageCount: 2, minMarginPoints: 40.0);
        _layoutAnalyzer.Analyze(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>(), Arg.Any<CancellationToken>())
            .Returns(metrics);

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.LayoutMetrics.ShouldBe(metrics);
        // And the analyzer was invoked on the imported PDF bytes as the resolved Pdf kind.
        _layoutAnalyzer.Received(1).Analyze(
            Arg.Any<ReadOnlyMemory<byte>>(), CvFileKind.Pdf, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersistedRawText_IsOriginalExtractedText_NotTheNormalizedScanCopy()
    {
        // The scan-copy bridges the spaced personnummer gap; the PERSISTED RawText must
        // remain the ORIGINAL extracted text with the space intact (Invariant 2 fidelity).
        const string original = "Anna Andersson\nPnr 811218 9876\nBackend-utvecklare";
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor(original, CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver();

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.RawText.ShouldBe(original);
        added.RawText.ShouldContain("811218 9876"); // space preserved, not joined
    }

    // ===============================================================
    // Personnummer guard call-site (scan precedes persist; flagged but persisted)
    // ===============================================================

    [Fact]
    public async Task Handle_SpacedPersonnummerInExtractedText_IsFlagged_AndArtifactStillPersists()
    {
        // The spaced form is bridged on the scan-copy by the normalizer, so the guard
        // flags it (Found=true) — and the artifact persists with the flag (Decision 3(i)).
        const string original = "Anna Andersson\nPnr 811218 9876";
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor(original, CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Personnummer.Found.ShouldBeTrue();
        result.Value.Personnummer.Count.ShouldBe(1);

        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.Personnummer.Found.ShouldBeTrue();
    }

    // ===============================================================
    // Original-file capture (Fas 4b PR-9a, ADR 0100 — DPIA M-F1/M-F5; consent path
    // CV-pivot 5b, DPIA §7 Beslut 2(c)). The handler captures the SEALED original as a
    // ResumeFile when the body scan is clean OR the user acknowledged the flagged
    // storage; flagged-without-acknowledge is never stored and the sealer is never
    // even invoked (fail-closed M-F5 — byte-parity with the pre-5b posture, pin M1).
    // ===============================================================

    [Fact]
    public async Task Handle_CleanBody_CapturesSealedOriginal_WithCanonicalMimeAndParsedCoupling()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nanna@example.com", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null)); // no SSYK derivation in play

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var parsed = db.ParsedResumes.Local.ShouldHaveSingleItem();
        var file = db.ResumeFiles.Local.ShouldHaveSingleItem();

        // The aggregate stores the SEALER's opaque output — never the plaintext upload.
        file.SealedContent.ShouldBe(SealedBytes);
        file.SealedContent.ShouldNotBe(PdfBytes);
        _sealer.Received(1).Seal(Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(PdfBytes)));

        // Canonical MIME from the magic-byte-resolved kind — not the client-declared string.
        file.ContentType.ShouldBe(CvFileSignature.PdfContentType);
        file.FileName.ShouldBe("cv.pdf");
        file.ByteSize.ShouldBe(PdfBytes.Length);
        file.PnrFlagged.ShouldBeFalse(); // clean body, no acknowledge in play → unflagged
        file.ParsedResumeId.ShouldBe(parsed.Id); // retention-coupling key (M-F3)
        file.JobSeekerId.ShouldBe(seeker.Id);
    }

    [Fact]
    public async Task Handle_PersonnummerInBody_DoesNotCaptureOriginal_AndNeverCallsSealer()
    {
        // M1 (5b security-bind B4): a body-flagged original WITHOUT the acknowledge stays
        // byte-identically fail-closed to the pre-5b posture — no capture, no sealer call,
        // no consent audit row. The consent path exists (test below) but demands the flag.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nPnr 811218 9876", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(); // the parse artifact itself still persists (flagged)
        db.ParsedResumes.Local.ShouldHaveSingleItem();
        db.ResumeFiles.Local.ShouldBeEmpty();
        db.AuditLogEntries.Local.ShouldBeEmpty();
        _sealer.DidNotReceive().Seal(Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Fact]
    public async Task Handle_PersonnummerInBody_WithAcknowledge_CapturesFlaggedOriginal_WithConsentEvidence()
    {
        // M2 (5b): the acknowledged re-POST captures the flagged original with the FULL
        // Art. 7(1) evidence pair — the server scan drives the flag, the stamps ride with it.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nPnr 811218 9876", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db)
            .Handle(PdfCommand(personnummerAcknowledged: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var parsed = db.ParsedResumes.Local.ShouldHaveSingleItem();
        var file = db.ResumeFiles.Local.ShouldHaveSingleItem();
        file.PnrFlagged.ShouldBeTrue();
        file.PnrConsentAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
        file.PnrConsentDialogVersion.ShouldBe(PnrConsentDialog.Version);
        file.SealedContent.ShouldBe(SealedBytes); // sealed, never plaintext
        file.ParsedResumeId.ShouldBe(parsed.Id);
        file.JobSeekerId.ShouldBe(seeker.Id);
    }

    [Fact]
    public async Task Handle_ConsentedCapture_WritesDistinctPiiFreeAuditRow_InSameUnitOfWork()
    {
        // M6 (5b security-bind B4/M-D): the consented capture writes the DISTINCT
        // ResumeFile.PnrStorageConsented row — aggregate = the FILE, ids/IP/UA/timestamp
        // only, no payload (structurally PII-free: never the pnr, filename, or content).
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nPnr 811218 9876", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db)
            .Handle(PdfCommand(personnummerAcknowledged: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var file = db.ResumeFiles.Local.ShouldHaveSingleItem();
        var audit = db.AuditLogEntries.Local.ShouldHaveSingleItem();
        audit.EventType.ShouldBe(ImportResumeCommand.PnrConsentAuditEventType);
        audit.AggregateType.ShouldBe("ResumeFile");
        audit.AggregateId.ShouldBe(file.Id.Value);
        audit.UserId.ShouldBe(_userId);
        audit.CorrelationId.ShouldBe(_correlationGuid);
        audit.Payload.ShouldBeNull(); // PII-free by construction — no content field exists
    }

    [Fact]
    public async Task Handle_CleanBody_WithAcknowledge_FlagIsInert_NoConsentEvidence_NoAuditRow()
    {
        // M-A inertness: a blindly-set acknowledge on a CLEAN upload is a no-op — the file
        // captures as a normal clean file with NO stamps and NO consent audit row (the stamp
        // is driven by the server's scan, never the client flag; premature consent defeated).
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nanna@example.com", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db)
            .Handle(PdfCommand(personnummerAcknowledged: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var file = db.ResumeFiles.Local.ShouldHaveSingleItem();
        file.PnrFlagged.ShouldBeFalse();
        file.PnrConsentAt.ShouldBeNull();
        file.PnrConsentDialogVersion.ShouldBeNull();
        db.AuditLogEntries.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ConsentedCapture_WhenCaptureOriginalFails_ReturnsFailure_NothingAdded()
    {
        // Structural atomicity holds on the NEW branch too: a CaptureOriginal failure on the
        // consented path returns before ANY Add — no parsed row, no file, no audit row can
        // outlive the failure through the unconditional UnitOfWork save. Forced via a
        // misbehaving sealer (empty envelope) — the one refusal reachable past the parse.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nPnr 811218 9876", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));
        _sealer.Seal(Arg.Any<ReadOnlyMemory<byte>>()).Returns([]);

        var result = await CreateSut(db)
            .Handle(PdfCommand(personnummerAcknowledged: true), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.SealedContentRequired");
        db.ParsedResumes.Local.ShouldBeEmpty();
        db.ResumeFiles.Local.ShouldBeEmpty();
        db.AuditLogEntries.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_PersonnummerInFileNameOnly_StillCaptures_WithRedactedFileName()
    {
        // A filename-only hit does not block capture (the BYTES are clean; parity with the
        // promote gate) — but the stored file_name column must carry the MASKED form, never
        // a plaintext personnummer (M-F1, aggregate-owned redaction).
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db)
            .Handle(PdfCommand("CV_811218-9876.pdf"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var file = db.ResumeFiles.Local.ShouldHaveSingleItem();
        file.FileName.ShouldNotContain("811218");
        file.FileName.ShouldBe("CV_******-****.pdf"); // digit-masked, separators kept
    }

    [Fact]
    public async Task Handle_ValidDocx_CapturesWithCanonicalDocxMime()
    {
        // The canonical MIME follows the RESOLVED kind. A DOCX upload (zip magic) with the
        // proper declared type must store the canonical OPC content-type.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        byte[] docxBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00];
        var command = new ImportResumeCommand(
            "cv.docx", CvFileSignature.DocxContentType, docxBytes);

        var result = await CreateSut(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ResumeFiles.Local.ShouldHaveSingleItem()
            .ContentType.ShouldBe(CvFileSignature.DocxContentType);
    }

    // ===============================================================
    // Filename personnummer guard (#426, defense-in-depth) — the guard also runs over
    // command.FileName; a filename hit sets FoundInFileName WITHOUT setting the body
    // Found (so it does not block promotion; B4 surfaces it as a Warn).
    // ===============================================================

    [Fact]
    public async Task Handle_PersonnummerInFileName_BodyClean_FlagsFoundInFileName_ButNotFound()
    {
        // The CV body is clean but the FILENAME carries a Luhn-valid personnummer
        // ("CV_811218-9876.pdf"). The import guard must flag FoundInFileName (so B4 warns and
        // the user is prompted to rename) WITHOUT setting Found — a filename hit never reaches
        // the canonical Resume, so it must not block promotion.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nBackend-utvecklare", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db).Handle(
            PdfCommand("CV_811218-9876.pdf"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.Personnummer.FoundInFileName.ShouldBeTrue();
        added.Personnummer.Found.ShouldBeFalse(); // body clean → promotion not blocked
        added.Personnummer.Count.ShouldBe(0); // filename is a flag, never folded into the count
    }

    [Fact]
    public async Task Handle_SpacedPersonnummerInFileName_IsBridgedAndFlagged()
    {
        // Same Normalize→Scan path as the body (#427 reuse): an OCR/spaced-gapped rename like
        // "CV 811218 9876.pdf" is bridged too, so the filename flag is set.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        await CreateSut(db).Handle(PdfCommand("CV 811218 9876.pdf"), CancellationToken.None);

        db.ParsedResumes.Local.ShouldHaveSingleItem().Personnummer.FoundInFileName.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_CleanFileName_AndCleanBody_LeavesFoundInFileNameFalse()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nBackend-utvecklare", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        await CreateSut(db).Handle(PdfCommand("CV_Anna_Andersson.pdf"), CancellationToken.None);

        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.Personnummer.FoundInFileName.ShouldBeFalse();
        added.Personnummer.Found.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_LuhnInvalidNumberInFileName_LeavesFoundInFileNameFalse()
    {
        // Pins the safety claim on the new filename surface: Personnummer.TryParse (date+Luhn)
        // stays the ONLY authority, so a pnr-SHAPED but checksum-invalid number in the filename
        // ("CV_811218-9875.pdf" - last digit off by one) must NOT be flagged. Guards against a
        // future loosening of the filename path to a naive regex without the Luhn gate.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson\nBackend-utvecklare", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        await CreateSut(db).Handle(PdfCommand("CV_811218-9875.pdf"), CancellationToken.None);

        db.ParsedResumes.Local.ShouldHaveSingleItem().Personnummer.FoundInFileName.ShouldBeFalse();
    }

    // ===============================================================
    // SSYK derivation call-site (F4-3): only when a title exists
    // ===============================================================

    [Fact]
    public async Task Handle_HasOccupationSources_CallsDeriveManyOnceIncludingThem()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation(experienceTitle: "Systemutvecklare"));
        StubDeriver();

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        // Tier 1 multi-signal: the union over the CV's occupation-bearing strings (here the
        // single experience Title + its Organization), via DeriveManyAsync — not the old
        // single-Experience[0].Title path.
        await _deriver.Received(1).DeriveManyAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.Contains("Systemutvecklare")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DerivesFromEducationDegreeBeforeWorkHistory()
    {
        // Klas 2026-06-21: current studies are the desired-occupation signal (a career-changer's
        // CV says "Plasman — Operatör" but they want "Systemutvecklare"). The handler must feed
        // the education Degree BEFORE the work history so it ranks first in the proposals.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Klas", "klas@example.com", null, null),
                experience: [new ParsedExperience("Plasman", "Operatör", "2005–nu", "raw")],
                education: [new ParsedEducation("NBI", "Systemutvecklare .NET", "Pågående", "raw")]),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
                new SectionConfidence(ParsedSectionKind.Education, SectionConfidenceLevel.Confident, []),
            ])));
        StubDeriver();

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        await _deriver.Received(1).DeriveManyAsync(
            Arg.Is<IReadOnlyList<string>>(s =>
                s.Contains("Systemutvecklare .NET") && s.Contains("Operatör") &&
                s.ToList().IndexOf("Systemutvecklare .NET") < s.ToList().IndexOf("Operatör")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepeatedOrganization_IsDistinctDeduplicated_InDerivationSources()
    {
        // BuildDerivationSources distinct (Ordinal ignore-case, priority-preserving):
        // the same Organization repeated across several experience entries is fed to the
        // deriver ONCE — a CV with "Acme AB" in five roles must not pass it five times.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Anna", "anna@example.com", null, null),
                experience:
                [
                    new ParsedExperience("Systemutvecklare", "Acme AB", "2023", "raw"),
                    new ParsedExperience("Systemutvecklare", "Acme AB", "2022", "raw"),
                    new ParsedExperience("Systemutvecklare", "acme ab", "2021", "raw"),
                ]),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ])));
        StubDeriver();

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        await _deriver.Received(1).DeriveManyAsync(
            Arg.Is<IReadOnlyList<string>>(s =>
                // "Systemutvecklare" once + "Acme AB" once (case-insensitive distinct) = 2.
                s.Count == 2 &&
                s.Count(x => x.Equals("Systemutvecklare", StringComparison.Ordinal)) == 1 &&
                s.Count(x => x.Equals("Acme AB", StringComparison.OrdinalIgnoreCase)) == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VeryLongCv_CapsDerivationSourcesAtMax()
    {
        // BuildDerivationSources MaxDerivationSources cap (= 40): a long CV cannot fan
        // out the in-memory taxonomy scan. 30 experience entries × 2 distinct fields each
        // (Title + Organization) = 60 candidate sources; the handler caps the list at 40.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        var experience = Enumerable.Range(0, 30)
            .Select(i => new ParsedExperience($"Roll {i}", $"Företag {i}", "2020", "raw"))
            .ToList();
        StubSegmenter(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Anna", "anna@example.com", null, null),
                experience: experience),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ])));
        StubDeriver();

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        await _deriver.Received(1).DeriveManyAsync(
            Arg.Is<IReadOnlyList<string>>(s => s.Count == 40),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoOccupationSources_DoesNotCallDeriver_ProposalsEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        // No experience and no education → no occupation-bearing strings → deriver not called.
        StubSegmenter(ConfidentSegmentation(experienceTitle: null));

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        await _deriver.DidNotReceive().DeriveManyAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        result.Value.OccupationProposal.ShouldBeEmpty();
        db.ParsedResumes.Local.ShouldHaveSingleItem().OccupationProposals.ShouldBeEmpty();
    }

    // ===============================================================
    // Per-occupation experience attribution call-site (ADR 0079-amendment, exp-per-occ PR-2):
    // the attributor's per-group ~years are joined onto the matching ProposedOccupation.
    // ===============================================================

    [Fact]
    public async Task Handle_AttributesApproximateYears_OntoMatchingProposal()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver(new OccupationCandidate(
            "q8wL_kdi_WaW", "Systemutvecklare", OccupationMatchKind.ExactOccupationName, "Backend-utvecklare"));
        StubExperienceYears(("q8wL_kdi_WaW", 5));

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        var proposal = added.OccupationProposals.ShouldHaveSingleItem();
        proposal.ConceptId.ShouldBe("q8wL_kdi_WaW");
        proposal.ApproximateYears.ShouldBe(5);
    }

    [Fact]
    public async Task Handle_ProposalWithNoAttributedYears_HasNullApproximateYears()
    {
        // The attributor returns nothing for this group (education-sourced / unparseable period):
        // the proposal carries null — honest "not stated", never a fabricated number (§5).
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver(new OccupationCandidate(
            "q8wL_kdi_WaW", "Systemutvecklare", OccupationMatchKind.ExactOccupationName, "Backend-utvecklare"));
        // _experienceDeriver default-stubbed to an empty dict in the ctor.

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var proposal = db.ParsedResumes.Local.ShouldHaveSingleItem()
            .OccupationProposals.ShouldHaveSingleItem();
        proposal.ApproximateYears.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_AttributesYears_OnlyToTheMatchingGroup_OthersNull()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver(
            new OccupationCandidate(
                "q8wL_kdi_WaW", "Systemutvecklare", OccupationMatchKind.ExactOccupationName, "x"),
            new OccupationCandidate(
                "a1B2_c3D4_e5F", "Mjukvaruutvecklare", OccupationMatchKind.StemmedTokenOverlap, "y"));
        StubExperienceYears(("q8wL_kdi_WaW", 7)); // only the first group has attributed years

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        var proposals = db.ParsedResumes.Local.ShouldHaveSingleItem().OccupationProposals;
        proposals.Single(p => p.ConceptId == "q8wL_kdi_WaW").ApproximateYears.ShouldBe(7);
        proposals.Single(p => p.ConceptId == "a1B2_c3D4_e5F").ApproximateYears.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_PassesOnlyExperienceEntries_ToTheExperienceDeriver()
    {
        // Education periods are study years, not work experience — the handler attributes over
        // content.Experience only (the deriver's input type ParsedExperience excludes education).
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Klas", "klas@example.com", null, null),
                experience: [new ParsedExperience("Operatör", "Plast AB", "2005–nu", "raw")],
                education: [new ParsedEducation("NBI", "Systemutvecklare .NET", "Pågående", "raw")]),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ])));
        StubDeriver();

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        await _experienceDeriver.Received(1).DeriveApproximateYearsAsync(
            Arg.Is<IReadOnlyList<ParsedExperience>>(e =>
                e.Count == 1 && e[0].Title == "Operatör"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EducationSourcedGroup_GetsNullYears_WhileExperienceGroupGetsAttributed()
    {
        // OUTCOME proof of education-exclusion (not just argument-shape spying): the union pass
        // surfaces BOTH an experience-sourced group (G_exp) and an education-sourced group (G_edu),
        // but the attributor — which only ever sees content.Experience — returns years for G_exp
        // only. The persisted proposals must therefore carry G_exp's years and G_edu's honest null.
        const string expGroup = "q8wL_kdi_WaW";
        const string eduGroup = "a1B2_c3D4_e5F";
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Klas", "klas@example.com", null, null),
                experience: [new ParsedExperience("Operatör", "Plast AB", "2005–2020", "raw")],
                education: [new ParsedEducation("NBI", "Systemutvecklare .NET", "Pågående", "raw")]),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
                new SectionConfidence(ParsedSectionKind.Education, SectionConfidenceLevel.Confident, []),
            ])));
        StubDeriver(
            new OccupationCandidate(expGroup, "Operatörer", OccupationMatchKind.ExactOccupationName, "Operatör"),
            new OccupationCandidate(eduGroup, "Systemutvecklare", OccupationMatchKind.ExactOccupationName, "Systemutvecklare .NET"));
        StubExperienceYears((expGroup, 15)); // education group is absent (attributor never sees it)

        await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        var proposals = db.ParsedResumes.Local.ShouldHaveSingleItem().OccupationProposals;
        proposals.Single(p => p.ConceptId == expGroup).ApproximateYears.ShouldBe(15);
        proposals.Single(p => p.ConceptId == eduGroup).ApproximateYears.ShouldBeNull();
    }

    // ===============================================================
    // Skill resolution call-site (ADR 0079 STEG 3): the CV's content.Skills are
    // resolved at import and carried as ProposedSkill on the persisted artifact.
    // ===============================================================

    [Fact]
    public async Task Handle_ResolvableSkills_AreResolvedAtImport_AndPersistedAsSkillProposals()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("Anna Andersson", CvExtractionStatus.Extracted);
        // ConfidentSegmentation's content carries no skills; segment with explicit skills so
        // the handler has something to resolve. The resolver is a fake — this asserts the
        // CALL-SITE (resolve content.Skills → ProposedSkill onto the aggregate), not the real
        // taxonomy (that is SkillResolverIntegrationTests).
        StubSegmenter(new ResumeSegmentationResult(
            new ParsedResumeContent(
                new ParsedContact("Anna", "anna@example.com", null, null),
                experience: [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021", "raw")],
                skills: ["C#", "PostgreSQL"]),
            ResumeLanguage.Sv,
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ])));
        StubDeriver();
        StubSkillResolver(
            new ResolvedSkill("k1A2_b3C4_d5E", "C#"),
            new ResolvedSkill("m6N7_o8P9_q0R", "PostgreSQL"));

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The handler resolves the CV's claimed skill names exactly once, over content.Skills.
        _skillResolver.Received(1).ResolveDetailed(
            Arg.Is<IEnumerable<string>>(s => s.Contains("C#") && s.Contains("PostgreSQL")),
            Arg.Any<CancellationToken>());

        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.SkillProposals.Count.ShouldBe(2);
        added.SkillProposals[0].ConceptId.ShouldBe("k1A2_b3C4_d5E");
        added.SkillProposals[0].Label.ShouldBe("C#");
        added.SkillProposals[1].ConceptId.ShouldBe("m6N7_o8P9_q0R");
    }

    [Fact]
    public async Task Handle_NoResolvableSkills_PersistsEmptySkillProposals()
    {
        // Fail-closed: a CV whose skills the taxonomy does not carry (resolver returns empty)
        // persists with no skill proposals — never throws, never blocks the import.
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor("text", CvExtractionStatus.Extracted);
        StubSegmenter(ConfidentSegmentation());
        StubDeriver();
        // _skillResolver default-stubbed to [] in the ctor — assert the empty-result branch.

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ParsedResumes.Local.ShouldHaveSingleItem().SkillProposals.ShouldBeEmpty();
    }

    // ===============================================================
    // Extraction fallbacks (OQ5) — still persists with Failed confidence
    // ===============================================================

    [Fact]
    public async Task Handle_NoTextLayer_FailedScannedImageConfidence_EmptyContent_StillPersists()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor(string.Empty, CvExtractionStatus.NoTextLayer);

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Failed.ToString());
        result.Value.Confidence.Fallback.ShouldBe(ParseFallbackReason.ScannedImageNoText.ToString());
        var added = db.ParsedResumes.Local.ShouldHaveSingleItem();
        added.Content.Experience.ShouldBeEmpty();
        // The segmenter is never invoked when there is no usable text.
        _segmenter.DidNotReceive().Segment(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_EmptyExtraction_FailedExtractionFailedConfidence_StillPersists()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        StubExtractor(string.Empty, CvExtractionStatus.Empty);

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Failed.ToString());
        result.Value.Confidence.Fallback.ShouldBe(ParseFallbackReason.ExtractionFailed.ToString());
        db.ParsedResumes.Local.ShouldHaveSingleItem();
    }

    // ===============================================================
    // Format gate / auth / jobseeker resolution
    // ===============================================================

    [Fact]
    public async Task Handle_UnsupportedFileFormat_ReturnsFailure_NothingAdded()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db);
        var command = new ImportResumeCommand(
            "cv.bin", "application/octet-stream", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var result = await CreateSut(db).Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.UnsupportedFileFormat");
        db.ParsedResumes.Local.ShouldBeEmpty();
        // No port is touched once the format gate rejects.
        _extractor.DidNotReceive().Extract(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var sut = new ImportResumeCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, _extractor, _layoutAnalyzer, _segmenter,
            _deriver, _experienceDeriver, _skillResolver, _sealer, _correlationId, _requestContext);

        await Should.ThrowAsync<UnauthorizedException>(
            () => sut.Handle(PdfCommand(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_JobSeekerNotFound_ReturnsNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create(); // no JobSeeker seeded

        var result = await CreateSut(db).Handle(PdfCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
        db.ParsedResumes.Local.ShouldBeEmpty();
    }
}
