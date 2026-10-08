using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public sealed class FeedbackScreenshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static FeedbackSubmission Submission() => FeedbackSubmission.Submit(
        JobSeekerId.New(), Guid.NewGuid(), FeedbackPage.Jobs, FeedbackRating.Create(4).Value,
        null, ReportedClientContext.Empty, null, Now).Value;

    [Fact]
    public void AttachTo_NormalizedPng_CopiesTheSubmissionIdentityAndRetentionInstant()
    {
        var submission = Submission();
        var png = EncodeOnePixelPng();

        var result = FeedbackScreenshot.AttachTo(submission, png, 1, 1);

        result.IsSuccess.ShouldBeTrue();
        var screenshot = result.Value;
        screenshot.Id.Value.ShouldNotBe(Guid.Empty);
        screenshot.SubmissionId.ShouldBe(submission.Id);
        screenshot.JobSeekerId.ShouldBe(submission.JobSeekerId);
        screenshot.SubmittedAt.ShouldBe(submission.SubmittedAt);
        screenshot.Width.ShouldBe(1);
        screenshot.Height.ShouldBe(1);
        screenshot.Content.ToArray().ShouldBe(png);
        screenshot.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void AttachTo_CallerChangesTheInputBuffer_PreservesItsOwnedContent()
    {
        var png = EncodeOnePixelPng();
        var expected = png.ToArray();
        var screenshot = FeedbackScreenshot.AttachTo(Submission(), png, 1, 1).Value;

        Array.Fill(png, (byte)0);

        screenshot.Content.ToArray().ShouldBe(expected);
    }

    [Fact]
    public void AttachTo_EmptyContent_IsRefused()
    {
        var result = FeedbackScreenshot.AttachTo(Submission(), ReadOnlyMemory<byte>.Empty, 1, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.ScreenshotInvalid");
    }

    [Fact]
    public void AttachTo_ContentOverFiveMebibytes_IsRefused()
    {
        var result = FeedbackScreenshot.AttachTo(Submission(), new byte[5 * 1024 * 1024 + 1], 1, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.ScreenshotInvalid");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(4000, 4001)]
    [InlineData(int.MaxValue, 2)]
    public void AttachTo_InvalidOrOversizedDimensions_IsRefused(int width, int height)
    {
        var result = FeedbackScreenshot.AttachTo(Submission(), EncodeOnePixelPng(), width, height);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.ScreenshotInvalid");
    }

    [Fact]
    public void Serialize_AndToString_DoNotExposeImageContent()
    {
        var png = EncodeOnePixelPng();
        var screenshot = FeedbackScreenshot.AttachTo(Submission(), png, 1, 1).Value;

        var json = JsonSerializer.Serialize(screenshot);
        var printed = screenshot.ToString();

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("Content", out _).ShouldBeFalse();
        json.ShouldNotContain(Convert.ToBase64String(png));
        printed.ShouldNotContain(Convert.ToBase64String(png));
    }

    // A complete RGBA8 PNG made by a fixture encoder, rather than a signature-only placeholder.
    // FeedbackScreenshotNormalizerTests pins that the real writer emits the same accepted format.
    private static byte[] EncodeOnePixelPng()
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(png, "IHDR"u8, [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write([0, 20, 40, 60, 255]);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(kind);
        output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in kind.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        output.Write(word);
    }
}
