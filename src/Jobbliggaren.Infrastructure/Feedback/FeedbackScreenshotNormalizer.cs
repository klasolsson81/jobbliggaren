using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Jobbliggaren.Infrastructure.Feedback;

public sealed class FeedbackScreenshotNormalizer : IFeedbackScreenshotNormalizer, IDisposable
{
    private readonly SemaphoreSlim _decode = new(1, 1);
    private readonly SixLabors.ImageSharp.Configuration _configuration = new(
        new PngConfigurationModule(), new JpegConfigurationModule(), new WebpConfigurationModule())
    {
        MaxDegreeOfParallelism = 1,
        MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            MaximumPoolSizeMegabytes = 32,
            AllocationLimitMegabytes = 128,
            SingleBufferAllocationLimitMegabytes = 64,
            AccumulativeAllocationLimitMegabytes = 256,
        }),
    };

    public async Task<Result<NormalizedFeedbackScreenshot>> NormalizeAsync(
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.IsEmpty || content.Length > FeedbackScreenshot.MaxContentBytes || !HasSupportedSignature(content.Span))
            return InvalidScreenshot();

        if (!await _decode.WaitAsync(0, cancellationToken))
            return Result.Failure<NormalizedFeedbackScreenshot>(DomainError.Conflict(
                "Feedback.ScreenshotBusy", "En annan skärmbild behandlas. Försök igen om en stund."));

        try
        {
            if (!await PngMetadataBudget.IsSafeAsync(content, cancellationToken))
                return InvalidScreenshot();

            using var source = new MemoryStream(content.ToArray(), writable: false);
            var identifyOptions = new DecoderOptions
            {
                Configuration = _configuration,
                MaxFrames = 2,
                SkipMetadata = true,
                SegmentIntegrityHandling = SegmentIntegrityHandling.Strict,
            };
            var info = await Image.IdentifyAsync(identifyOptions, source, cancellationToken);
            if (info.Width <= 0 || info.Height <= 0
                || (long)info.Width * info.Height > FeedbackScreenshot.MaxPixels || info.FrameCount > 1)
                return InvalidScreenshot();

            source.Position = 0;
            var decodeOptions = new DecoderOptions
            {
                Configuration = _configuration,
                MaxFrames = 1,
                SegmentIntegrityHandling = SegmentIntegrityHandling.Strict,
                ColorProfileHandling = ColorProfileHandling.Preserve,
            };
            using var image = await Image.LoadAsync<Rgba32>(decodeOptions, source, cancellationToken);
            if (image.Frames.Count != 1 || (long)image.Width * image.Height > FeedbackScreenshot.MaxPixels)
                return InvalidScreenshot();

            cancellationToken.ThrowIfCancellationRequested();
            image.Mutate(context => context.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            using var output = new BoundedPngStream();
            await image.SaveAsync(output, new PngEncoder
            {
                ColorType = PngColorType.RgbWithAlpha,
                BitDepth = PngBitDepth.Bit8,
                SkipMetadata = true,
            }, cancellationToken);
            return Result.Success(new NormalizedFeedbackScreenshot(output.ToArray(), image.Width, image.Height));
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException
            or InvalidMemoryOperationException or InvalidDataException or IOException)
        {
            return InvalidScreenshot();
        }
        finally
        {
            _decode.Release();
        }
    }

    public void Dispose()
    {
        _decode.Dispose();
        _configuration.MemoryAllocator.ReleaseRetainedResources();
    }

    private static bool HasSupportedSignature(ReadOnlySpan<byte> content) =>
        content.StartsWith<byte>([137, 80, 78, 71, 13, 10, 26, 10])
        || content.StartsWith<byte>([255, 216, 255])
        || (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8));

    private static Result<NormalizedFeedbackScreenshot> InvalidScreenshot() =>
        Result.Failure<NormalizedFeedbackScreenshot>(DomainError.Validation(
            "Feedback.ScreenshotInvalid", "Skärmbilden kunde inte läsas. Använd en PNG-, JPEG- eller WebP-bild med högst 16 miljoner pixlar och 5 MiB."));

    private sealed class BoundedPngStream : MemoryStream
    {
        public BoundedPngStream() : base(FeedbackScreenshot.MaxContentBytes) { }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityFor(1);
            base.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            EnsureCapacityFor(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureCapacityFor(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void SetLength(long value)
        {
            if (value > FeedbackScreenshot.MaxContentBytes)
                throw new IOException("The normalized screenshot exceeds the byte limit.");
            base.SetLength(value);
        }

        private void EnsureCapacityFor(int count)
        {
            if (Position + count > FeedbackScreenshot.MaxContentBytes)
                throw new IOException("The normalized screenshot exceeds the byte limit.");
        }
    }
}
