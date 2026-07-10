using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes.Files;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5 / ADR 0100) — invariants for the <see cref="ResumeFile"/>
/// aggregate: structural preconditions of <see cref="ResumeFile.CaptureOriginal"/>, the
/// filename personnummer-redaction it OWNS (M-F1 — the unencrypted <c>file_name</c> column
/// never carries a plaintext personnummer, no matter the caller), and that the aggregate
/// only ever holds the opaque sealed bytes it was given (aggregate honesty, CTO Q2 — the
/// structural no-plaintext-member pin lives in the architecture tests).
/// </summary>
public class ResumeFileTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly IDateTimeProvider Clock = new FixedClock(Now);

    private static readonly JobSeekerId Owner = new(Guid.NewGuid());
    private static readonly ParsedResumeId Parsed = new(Guid.NewGuid());
    private static readonly byte[] Sealed = [0x01, 0xAA, 0xBB, 0xCC];

    private static Result<ResumeFile> Capture(
        JobSeekerId? owner = null,
        ParsedResumeId? parsed = null,
        byte[]? sealedContent = null,
        string? contentType = "application/pdf",
        string? fileName = "cv.pdf",
        long byteSize = 1234,
        bool pnrFlagged = false) =>
        ResumeFile.CaptureOriginal(
            owner ?? Owner,
            parsed ?? Parsed,
            sealedContent ?? Sealed,
            contentType,
            fileName,
            byteSize,
            pnrFlagged,
            Clock);

    [Fact]
    public void CaptureOriginal_Valid_SetsAllPropertiesAndStampsClock()
    {
        var result = Capture();

        result.IsSuccess.ShouldBeTrue();
        var file = result.Value;
        file.Id.Value.ShouldNotBe(Guid.Empty);
        file.JobSeekerId.ShouldBe(Owner);
        file.ParsedResumeId.ShouldBe(Parsed);
        file.SealedContent.ShouldBe(Sealed); // exactly the opaque bytes the caller sealed
        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldBe("cv.pdf");
        file.ByteSize.ShouldBe(1234);
        file.PnrFlagged.ShouldBeFalse();
        file.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void CaptureOriginal_TrimsContentTypeAndFileName()
    {
        var result = Capture(contentType: " application/pdf ", fileName: " cv.pdf ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ContentType.ShouldBe("application/pdf");
        result.Value.FileName.ShouldBe("cv.pdf");
    }

    // ── filename personnummer-redaction (M-F1) — the aggregate OWNS this invariant ──

    [Fact]
    public void CaptureOriginal_FileNameWithPersonnummer_IsMaskedAtRest()
    {
        var result = Capture(fileName: "CV_811218-9876.pdf");

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("CV_******-****.pdf"); // digits masked, separators kept
        result.Value.FileName.ShouldNotContain("811218");
    }

    [Fact]
    public void CaptureOriginal_FileNameWithNonPersonnummerDigits_IsLeftIntact()
    {
        // The redactor is date+Luhn-gated — ordinary digits (years, versions) survive.
        var result = Capture(fileName: "CV_2026_v2.pdf");

        result.Value.FileName.ShouldBe("CV_2026_v2.pdf");
    }

    // ── structural preconditions ────────────────────────────────────────────────

    [Fact]
    public void CaptureOriginal_DefaultJobSeekerId_Fails()
    {
        var result = Capture(owner: default(JobSeekerId));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.JobSeekerIdRequired");
    }

    [Fact]
    public void CaptureOriginal_DefaultParsedResumeId_Fails()
    {
        var result = Capture(parsed: default(ParsedResumeId));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.ParsedResumeIdRequired");
    }

    [Fact]
    public void CaptureOriginal_EmptySealedContent_Fails()
    {
        var result = Capture(sealedContent: []);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.SealedContentRequired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CaptureOriginal_MissingContentType_Fails(string? contentType)
    {
        var result = Capture(contentType: contentType);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.ContentTypeRequired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CaptureOriginal_MissingFileName_Fails(string? fileName)
    {
        var result = Capture(fileName: fileName);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.FileNameRequired");
    }

    [Fact]
    public void CaptureOriginal_FileNameOver400Chars_Fails()
    {
        var result = Capture(fileName: new string('a', 397) + ".pdf"); // 401 chars

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.FileNameTooLong");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CaptureOriginal_NonPositiveByteSize_Fails(long byteSize)
    {
        var result = Capture(byteSize: byteSize);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeFile.ByteSizeInvalid");
    }

    [Fact]
    public void CaptureOriginal_PnrFlagged_CarriesFlagAsMetadata()
    {
        // PR-9a's import path always passes false (flagged originals are not captured,
        // M-F5); the aggregate itself carries the flag for the deferred acknowledge-store.
        var result = Capture(pnrFlagged: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PnrFlagged.ShouldBeTrue();
    }
}
