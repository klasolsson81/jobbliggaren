using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Feedback;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Jobbliggaren.Application.UnitTests.Feedback;

public sealed class FeedbackScreenshotNormalizerTests : IDisposable
{
    private readonly FeedbackScreenshotNormalizer _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sut.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public async Task NormalizeAsync_AStaticAllowedImage_EmitsOneLosslessRgba8Png(string format)
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        var encoded = await FeedbackScreenshotFixtures.EncodeAsync(source, format, Ct);
        using var decodedInput = Image.Load<Rgba32>(encoded);

        var result = await _sut.NormalizeAsync(encoded, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Width.ShouldBe(source.Width);
        result.Value.Height.ShouldBe(source.Height);
        var png = result.Value.Content.ToArray();
        png.Take(8).ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        png[24].ShouldBe((byte)8);
        png[25].ShouldBe((byte)6);
        using var output = Image.Load<Rgba32>(png);
        output.Frames.Count.ShouldBe(1);
        FeedbackScreenshotFixtures.AssertSamePixels(decodedInput, output);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public async Task NormalizeAsync_ExifOrientation_IsAppliedBeforeMetadataIsRemoved(string format)
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        var encoded = await FeedbackScreenshotFixtures.EncodeAsync(source, format, Ct);
        using var expected = Image.Load<Rgba32>(encoded);
        expected.Metadata.ExifProfile.ShouldNotBeNull().TryGetValue(ExifTag.Orientation, out var orientation)
            .ShouldBeTrue();
        orientation.ShouldNotBeNull().Value.ShouldBe((ushort)6);
        expected.Mutate(x => x.AutoOrient());

        var result = await _sut.NormalizeAsync(encoded, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Width.ShouldBe(source.Height);
        result.Value.Height.ShouldBe(source.Width);
        using var output = Image.Load<Rgba32>(result.Value.Content.Span);
        output.Metadata.ExifProfile.ShouldBeNull();
        FeedbackScreenshotFixtures.AssertSamePixels(expected, output);
    }

    [Fact]
    public async Task NormalizeAsync_PngWithProfilesAndText_RemovesEncodedMetadata()
    {
        const string marker = "synthetic-private-screenshot-metadata";
        using var source = FeedbackScreenshotFixtures.Pixels();
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.Artist, marker);
        source.Metadata.IccProfile = new IccProfile();
        source.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(
            $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description xmlns:t='urn:fixture' t:private='{marker}' /></rdf:RDF></x:xmpmeta>"));
        source.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Comment", marker, string.Empty, string.Empty));
        var png = await FeedbackScreenshotFixtures.EncodeAsync(source, "png", Ct);
        using var encodedInput = Image.Load<Rgba32>(png);
        encodedInput.Metadata.ExifProfile.ShouldNotBeNull();
        encodedInput.Metadata.IccProfile.ShouldNotBeNull();
        encodedInput.Metadata.XmpProfile.ShouldNotBeNull();
        encodedInput.Metadata.GetPngMetadata().TextData.ShouldNotBeEmpty();

        var result = await _sut.NormalizeAsync(png, Ct);

        result.IsSuccess.ShouldBeTrue();
        using var output = Image.Load<Rgba32>(result.Value.Content.Span);
        output.Metadata.ExifProfile.ShouldBeNull();
        output.Metadata.IccProfile.ShouldBeNull();
        output.Metadata.XmpProfile.ShouldBeNull();
        output.Metadata.IptcProfile.ShouldBeNull();
        output.Metadata.GetPngMetadata().TextData.ShouldBeEmpty();
        Encoding.UTF8.GetString(result.Value.Content.Span).ShouldNotContain(marker);
        FeedbackScreenshotFixtures.AssertSamePixels(encodedInput, output);
    }

    [Theory]
    [InlineData("exif", false)]
    [InlineData("exif", true)]
    [InlineData("iptc", false)]
    [InlineData("iptc", true)]
    public async Task NormalizeAsync_ExternalPngClientDeclaresMoreLegacyProfileBytesThanItSupplies_IsRefused(
        string profile, bool compressed)
    {
        var source = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var text = Encoding.ASCII.GetBytes("\n" + profile + "\n    1024\n00\n");
        var chunk = FeedbackScreenshotFixtures.MetadataChunk(
            compressed ? "zTXt" : "tEXt", "Raw profile type " + profile, text);
        var png = FeedbackScreenshotFixtures.InsertMetadata(source, chunk);
        png.Length.ShouldBeLessThan(5 * 1024 * 1024);

        var result = await _sut.NormalizeAsync(png, Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(source, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("exif", false)]
    [InlineData("exif", true)]
    [InlineData("iptc", false)]
    [InlineData("iptc", true)]
    public async Task NormalizeAsync_ExternalPngClientWrapsLegacyProfilesInANonLetterChunk_IsRefusedAndReleasesCapacity(
        string profile, bool compressed)
    {
        var source = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var text = Encoding.ASCII.GetBytes("\n" + profile + "\n    1024\n00\n");
        text.Length.ShouldBeLessThan(128);
        var inner = FeedbackScreenshotFixtures.MetadataChunk(
            compressed ? "zTXt" : "tEXt", "Raw profile type " + profile, text);
        BinaryPrimitives.ReadInt32BigEndian(inner.AsSpan(0, 4)).ShouldBe(inner.Length - 12);
        var wrapper = new byte[inner.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(wrapper.AsSpan(0, 4), inner.Length);
        inner.CopyTo(wrapper, 8);
        for (var candidate = 128; candidate <= byte.MaxValue; candidate++)
        {
            wrapper[4] = (byte)candidate;
            FeedbackScreenshotFixtures.UpdateChunkChecksum(wrapper);
            if (BinaryPrimitives.ReadInt32BigEndian(wrapper.AsSpan(wrapper.Length - 4, 4)) < 0)
                break;
        }
        BinaryPrimitives.ReadInt32BigEndian(wrapper.AsSpan(0, 4)).ShouldBe(inner.Length);
        wrapper.AsSpan(4, 4).ToArray().All(value => !char.IsAsciiLetter((char)value)).ShouldBeTrue();
        wrapper.AsSpan(8, inner.Length).SequenceEqual(inner).ShouldBeTrue();
        BinaryPrimitives.ReadInt32BigEndian(wrapper.AsSpan(wrapper.Length - 4, 4)).ShouldBeLessThan(0);
        var png = FeedbackScreenshotFixtures.InsertMetadata(source, wrapper);
        png.Length.ShouldBeLessThan(5 * 1024 * 1024);

        var result = await _sut.NormalizeAsync(png, Ct);

        result.IsFailure.ShouldBeTrue();
        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(source, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalizeAsync_AValidLegacyExifProfile_OrientsPixelsBeforeStrippingMetadata(bool compressed)
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        var encoded = await FeedbackScreenshotFixtures.EncodeAsync(source, "png", Ct);
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)6);
        profile.TryGetValue(ExifTag.Orientation, out var orientation).ShouldBeTrue();
        orientation.ShouldNotBeNull().Value.ShouldBe((ushort)6);
        var profileBytes = profile.ToByteArray().ShouldNotBeNull();
        var legacyBytes = profileBytes.AsSpan().StartsWith("Exif\0\0"u8)
            ? profileBytes : "Exif\0\0"u8.ToArray().Concat(profileBytes).ToArray();
        var text = Encoding.ASCII.GetBytes("\nexif\n    "
            + legacyBytes.Length.ToString(CultureInfo.InvariantCulture) + "\n"
            + Convert.ToHexString(legacyBytes).ToLowerInvariant() + "\n");
        var png = FeedbackScreenshotFixtures.InsertMetadata(encoded,
            FeedbackScreenshotFixtures.MetadataChunk(compressed ? "zTXt" : "tEXt", "Raw profile type exif", text));
        using var expected = source.Clone(x => x.Rotate(RotateMode.Rotate90));

        var result = await _sut.NormalizeAsync(png, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Width.ShouldBe(source.Height);
        result.Value.Height.ShouldBe(source.Width);
        using var output = Image.Load<Rgba32>(result.Value.Content.Span);
        output.Metadata.ExifProfile.ShouldBeNull();
        output.Metadata.GetPngMetadata().TextData.ShouldBeEmpty();
        FeedbackScreenshotFixtures.AssertSamePixels(expected, output);
    }

    [Theory]
    [InlineData("zTXt")]
    [InlineData("iTXt")]
    [InlineData("iCCP")]
    public async Task NormalizeAsync_ExternalPngClientCompressesOneMetadataChunkBeyondFiveMebibytes_IsRefused(string kind)
    {
        var source = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var expanded = new byte[5 * 1024 * 1024 + 1];
        Array.Fill(expanded, (byte)'x');
        if (kind == "iCCP")
        {
            Array.Clear(expanded);
            var profileBytes = new IccProfile().ToByteArray().ShouldNotBeNull();
            BinaryPrimitives.ReadUInt32BigEndian(profileBytes.AsSpan(128, 4)).ShouldBe(0u);
            profileBytes.CopyTo(expanded, 0);
            BinaryPrimitives.WriteUInt32BigEndian(expanded.AsSpan(0, 4), (uint)expanded.Length);
        }
        var png = FeedbackScreenshotFixtures.InsertMetadata(source,
            FeedbackScreenshotFixtures.MetadataChunk(kind, "Fixture expanded metadata", expanded));
        png.Length.ShouldBeLessThan(5 * 1024 * 1024);

        var result = await _sut.NormalizeAsync(png, Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(source, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_ExternalPngClientCompressesMultipleMetadataChunksBeyondTheCombinedBudget_IsRefused()
    {
        var source = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var expanded = new byte[3 * 1024 * 1024];
        Array.Fill(expanded, (byte)'x');
        expanded.Length.ShouldBeLessThan(5 * 1024 * 1024);
        (2 * expanded.Length).ShouldBeGreaterThan(5 * 1024 * 1024);
        var first = FeedbackScreenshotFixtures.MetadataChunk("zTXt", "Fixture metadata one", expanded);
        var second = FeedbackScreenshotFixtures.MetadataChunk("zTXt", "Fixture metadata two", expanded);
        var png = FeedbackScreenshotFixtures.InsertMetadata(source, first, second);
        png.Length.ShouldBeLessThan(5 * 1024 * 1024);

        var result = await _sut.NormalizeAsync(png, Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(source, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_ExternalPngClientCorruptsAnAncillaryChunkChecksum_IsRefused()
    {
        var source = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var chunk = FeedbackScreenshotFixtures.MetadataChunk("zTXt", "Comment", "synthetic metadata"u8);
        chunk[^1] ^= 1;
        var png = FeedbackScreenshotFixtures.InsertMetadata(source, chunk);

        var result = await _sut.NormalizeAsync(png, Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(source, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NormalizeAsync_ExternalPngClientDamagesZlibDataButKeepsThePngChecksumValid_IsRefused(
        bool corruptHeader)
    {
        var source = await FeedbackScreenshotFixtures.PngAsync(Ct);
        const string keyword = "Comment";
        var chunk = FeedbackScreenshotFixtures.MetadataChunk("zTXt", keyword, "synthetic metadata"u8);
        if (corruptHeader)
            chunk[8 + Encoding.Latin1.GetByteCount(keyword) + 2] = 0;
        else
            chunk[^5] ^= 1;
        FeedbackScreenshotFixtures.UpdateChunkChecksum(chunk);
        var png = FeedbackScreenshotFixtures.InsertMetadata(source, chunk);

        var result = await _sut.NormalizeAsync(png, Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(source, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("tEXt")]
    [InlineData("zTXt")]
    [InlineData("iTXt")]
    public async Task NormalizeAsync_ValidTextMetadataWithinTheBudget_PreservesPixelsAndRemovesTheText(string kind)
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        var encoded = await FeedbackScreenshotFixtures.EncodeAsync(source, "png", Ct);
        const string marker = "synthetic-private-text-chunk";
        var png = FeedbackScreenshotFixtures.InsertMetadata(encoded,
            FeedbackScreenshotFixtures.MetadataChunk(kind, "Comment", Encoding.UTF8.GetBytes(marker)));
        using var encodedInput = Image.Load<Rgba32>(png);
        encodedInput.Metadata.GetPngMetadata().TextData.ShouldContain(text => text.Value == marker);

        var result = await _sut.NormalizeAsync(png, Ct);

        result.IsSuccess.ShouldBeTrue();
        using var output = Image.Load<Rgba32>(result.Value.Content.Span);
        output.Metadata.GetPngMetadata().TextData.ShouldBeEmpty();
        Encoding.UTF8.GetString(result.Value.Content.Span).ShouldNotContain(marker);
        FeedbackScreenshotFixtures.AssertSamePixels(source, output);
    }

    [Fact]
    public async Task NormalizeAsync_JpegWithIptc_RemovesTheCaption()
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        source.Metadata.IptcProfile = new IptcProfile();
        source.Metadata.IptcProfile.SetValue(IptcTag.Caption, "synthetic-private-caption");
        var jpeg = await FeedbackScreenshotFixtures.EncodeAsync(source, "jpeg", Ct);
        using var encodedInput = Image.Load<Rgba32>(jpeg);
        encodedInput.Metadata.IptcProfile.ShouldNotBeNull();

        var result = await _sut.NormalizeAsync(jpeg, Ct);

        result.IsSuccess.ShouldBeTrue();
        using var output = Image.Load<Rgba32>(result.Value.Content.Span);
        output.Metadata.IptcProfile.ShouldBeNull();
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task NormalizeAsync_AnAnimatedAllowedFormat_IsRefused(string format)
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        using var second = FeedbackScreenshotFixtures.Pixels();
        second[0, 0] = new Rgba32(255, 0, 255);
        source.Frames.AddFrame(second.Frames.RootFrame);
        var animated = await FeedbackScreenshotFixtures.EncodeAsync(source, format, Ct);
        using var encodedInput = Image.Load<Rgba32>(animated);
        encodedInput.Frames.Count.ShouldBe(2);

        var result = await _sut.NormalizeAsync(animated, Ct);

        AssertInvalid(result.IsFailure, result.Error);
    }

    [Theory]
    [InlineData("bmp")]
    [InlineData("gif")]
    public async Task NormalizeAsync_AValidUnsupportedFormat_IsRefused(string format)
    {
        using var source = FeedbackScreenshotFixtures.Pixels();
        var encoded = await FeedbackScreenshotFixtures.EncodeAsync(source, format, Ct);

        var result = await _sut.NormalizeAsync(encoded, Ct);

        AssertInvalid(result.IsFailure, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script /></svg>")]
    public async Task NormalizeAsync_WithoutAllowedMagicBytes_IsRefused(string content)
    {
        var result = await _sut.NormalizeAsync(Encoding.UTF8.GetBytes(content), Ct);

        AssertInvalid(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task NormalizeAsync_TruncatedImage_IsRefusedAndReleasesCapacity()
    {
        var png = await FeedbackScreenshotFixtures.PngAsync(Ct);

        var result = await _sut.NormalizeAsync(png.AsMemory(0, png.Length / 2), Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(png, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_CorruptedPngChunkChecksum_IsRefused()
    {
        var png = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var offset = 8;
        while (!png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            offset += BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4)) + 12;
        var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
        png[offset + 8 + length] ^= 1;

        var result = await _sut.NormalizeAsync(png, Ct);

        AssertInvalid(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task NormalizeAsync_InputOverFiveMebibytes_IsRefused()
    {
        var png = await FeedbackScreenshotFixtures.PngAsync(Ct);
        var oversized = new byte[5 * 1024 * 1024 + 1];
        png.CopyTo(oversized, 0);

        var result = await _sut.NormalizeAsync(oversized, Ct);

        AssertInvalid(result.IsFailure, result.Error);
    }

    [Theory]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public async Task NormalizeAsync_PixelCountAtOrBeyondSixteenMillion_EnforcesTheBoundary(
        int height, bool accepted)
    {
        using var source = new Image<Rgba32>(4000, height, new Rgba32(20, 40, 60, 255));
        var png = await FeedbackScreenshotFixtures.EncodeAsync(source, "png", Ct);
        png.Length.ShouldBeLessThan(5 * 1024 * 1024);

        var result = await _sut.NormalizeAsync(png, Ct);

        result.IsSuccess.ShouldBe(accepted);
        if (accepted)
        {
            result.Value.Width.ShouldBe(4000);
            result.Value.Height.ShouldBe(height);
        }
        else
            AssertInvalid(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task NormalizeAsync_FourSimultaneousRealDecodes_AdmitsOneAndRefusesThreeWithoutQueuing()
    {
        var ct = Ct;
        byte[] png;
        using (var source = new Image<Rgba32>(4000, 4000, new Rgba32(20, 40, 60, 255)))
            png = await FeedbackScreenshotFixtures.EncodeAsync(source, "png", ct);
        png.Length.ShouldBeLessThanOrEqualTo(5 * 1024 * 1024);

        using var start = new Barrier(4);
        var operations = Enumerable.Range(0, 4).Select(_ =>
            Task.Factory.StartNew(() =>
            {
                start.SignalAndWait(ct);
                return _sut.NormalizeAsync(png, ct);
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()).ToArray();

        var results = await Task.WhenAll(operations);

        results.Count(result => result.IsSuccess).ShouldBe(1);
        var accepted = results.Single(result => result.IsSuccess).Value;
        accepted.Width.ShouldBe(4000);
        accepted.Height.ShouldBe(4000);
        var refused = results.Where(result => result.IsFailure).ToArray();
        refused.Length.ShouldBe(3);
        foreach (var result in refused)
        {
            result.Error.Kind.ShouldBe(ErrorKind.Conflict);
            result.Error.Code.ShouldBe("Feedback.ScreenshotBusy");
        }
        (await _sut.NormalizeAsync(await FeedbackScreenshotFixtures.PngAsync(ct), ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_AJpegWhoseLosslessPngExceedsFiveMebibytes_IsRefused()
    {
        using var source = new Image<Rgba32>(1536, 1536);
        var random = new Random(1979);
        source.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            }
        });
        var jpeg = await FeedbackScreenshotFixtures.EncodeAsync(source, "jpeg", Ct);
        jpeg.Length.ShouldBeLessThan(5 * 1024 * 1024);
        using var decoded = Image.Load<Rgba32>(jpeg);
        var expanded = await FeedbackScreenshotFixtures.EncodeAsync(decoded, "png", Ct);
        expanded.Length.ShouldBeGreaterThan(5 * 1024 * 1024);

        var result = await _sut.NormalizeAsync(jpeg, Ct);

        AssertInvalid(result.IsFailure, result.Error);
        (await _sut.NormalizeAsync(await FeedbackScreenshotFixtures.PngAsync(Ct), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_CancelledToken_ThrowsCancellationAndReleasesCapacity()
    {
        var png = await FeedbackScreenshotFixtures.PngAsync(Ct);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await _sut.NormalizeAsync(png, cancelled.Token));

        (await _sut.NormalizeAsync(png, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_AfterDisposal_RefusesFurtherUse()
    {
        var png = await FeedbackScreenshotFixtures.PngAsync(Ct);
        _sut.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () => await _sut.NormalizeAsync(png, Ct));
    }

    private static void AssertInvalid(bool failed, DomainError error)
    {
        failed.ShouldBeTrue();
        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Code.ShouldBe("Feedback.ScreenshotInvalid");
    }
}

internal static class FeedbackScreenshotFixtures
{
    public static Image<Rgba32> Pixels()
    {
        var image = new Image<Rgba32>(2, 3);
        image[0, 0] = new Rgba32(255, 0, 0, 255);
        image[1, 0] = new Rgba32(0, 255, 0, 255);
        image[0, 1] = new Rgba32(0, 0, 255, 255);
        image[1, 1] = new Rgba32(120, 30, 90, 128);
        image[0, 2] = new Rgba32(20, 40, 60, 255);
        image[1, 2] = new Rgba32(240, 210, 180, 255);
        return image;
    }

    public static async Task<byte[]> PngAsync(CancellationToken ct)
    {
        using var image = Pixels();
        return await EncodeAsync(image, "png", ct);
    }

    public static async Task<byte[]> EncodeAsync(Image<Rgba32> image, string format, CancellationToken ct)
    {
        IImageEncoder encoder = format switch
        {
            "png" => new PngEncoder { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8 },
            "jpeg" => new JpegEncoder { Quality = 95 },
            "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
            "bmp" => new BmpEncoder(),
            "gif" => new GifEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        using var output = new MemoryStream();
        await image.SaveAsync(output, encoder, ct);
        return output.ToArray();
    }

    // External PNG clients can write ancillary chunks and ImageMagick-compatible legacy profiles.
    // Only the public normalizer decodes refused fixtures; declared legacy lengths stay deliberately small.
    public static byte[] MetadataChunk(string kind, string keyword, ReadOnlySpan<byte> expanded)
    {
        using var data = new MemoryStream();
        data.Write(Encoding.Latin1.GetBytes(keyword));
        data.WriteByte(0);
        switch (kind)
        {
            case "tEXt":
                data.Write(expanded);
                break;
            case "zTXt":
            case "iCCP":
                data.WriteByte(0);
                WriteCompressed(data, expanded);
                break;
            case "iTXt":
                data.Write([1, 0, 0, 0]);
                WriteCompressed(data, expanded);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        var payload = data.ToArray();
        var chunk = new byte[payload.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), payload.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        UpdateChunkChecksum(chunk);
        return chunk;
    }

    public static void UpdateChunkChecksum(byte[] chunk)
    {
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(chunk.AsSpan(0, 4));
        var crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, payloadLength + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
    }

    public static byte[] InsertMetadata(byte[] encodedPng, params byte[][] chunks)
    {
        encodedPng.AsSpan(12, 4).SequenceEqual("IHDR"u8).ShouldBeTrue();
        var afterHeader = 8 + BinaryPrimitives.ReadInt32BigEndian(encodedPng.AsSpan(8, 4)) + 12;
        using var png = new MemoryStream();
        png.Write(encodedPng.AsSpan(0, afterHeader));
        foreach (var chunk in chunks)
            png.Write(chunk);
        png.Write(encodedPng.AsSpan(afterHeader));
        return png.ToArray();
    }

    private static void WriteCompressed(Stream output, ReadOnlySpan<byte> expanded)
    {
        using var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
        zlib.Write(expanded);
    }

    public static void AssertSamePixels(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        actual.Width.ShouldBe(expected.Width);
        actual.Height.ShouldBe(expected.Height);
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
                actual[x, y].ShouldBe(expected[x, y]);
    }
}
