using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.JobAds.Abstractions;
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
    private readonly IResumeSegmenter _segmenter = Substitute.For<IResumeSegmenter>();
    private readonly IOccupationCodeDeriver _deriver = Substitute.For<IOccupationCodeDeriver>();
    private readonly Guid _userId = Guid.NewGuid();

    // "%PDF-1.7" — a real PDF magic prefix so CvFileSignature resolves Pdf.
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    public ImportResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private ImportResumeCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _extractor, _segmenter, _deriver);

    private static ImportResumeCommand PdfCommand() =>
        new("cv.pdf", "application/pdf", PdfBytes);

    private async Task<JobSeeker> SeedJobSeekerAsync(Infrastructure.Persistence.AppDbContext db)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seeker;
    }

    private void StubExtractor(string rawText, CvExtractionStatus status) =>
        _extractor.Extract(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>())
            .Returns(new CvExtractionResult(rawText, status));

    private void StubSegmenter(ResumeSegmentationResult result) =>
        _segmenter.Segment(Arg.Any<string>()).Returns(result);

    private void StubDeriver(params OccupationCandidate[] candidates) =>
        _deriver.DeriveManyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(
                new OccupationDerivationResult("title", candidates)));

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
        _extractor.DidNotReceive().Extract(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CvFileKind>());
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var sut = new ImportResumeCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, _extractor, _segmenter, _deriver);

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
