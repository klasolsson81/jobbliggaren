using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Infrastructure.Feedback;

internal static class PngMetadataBudget
{
    public static async Task<bool> IsSafeAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (!content.Span.StartsWith<byte>([137, 80, 78, 71, 13, 10, 26, 10]))
            return true;

        var used = 0;
        var offset = 8;
        while (offset < content.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (content.Length - offset < 12)
                return false;
            var length = BinaryPrimitives.ReadUInt32BigEndian(content.Span.Slice(offset, 4));
            if (length > int.MaxValue || (long)offset + length + 12 > content.Length)
                return false;
            if (!ValidChunkType(content.Span.Slice(offset + 4, 4)))
                return false;
            var kind = KindOf(content.Span.Slice(offset + 4, 4));
            var data = content.Slice(offset + 8, (int)length);
            var next = offset + (int)length + 12;
            if (kind == ChunkKind.End)
                return length == 0 && next == content.Length;
            if (kind != ChunkKind.Pixels)
            {
                var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(content.Span.Slice(next - 4, 4));
                if (!ValidCrc(content.Span.Slice(offset + 4, (int)length + 4), expectedCrc, cancellationToken))
                    return false;

                if (kind is ChunkKind.Text or ChunkKind.CompressedText or ChunkKind.InternationalText or ChunkKind.Icc)
                {
                    var textStart = TextStart(data.Span, kind, out var keyLength, out var compressed);
                    if (textStart < 0 || !Charge(ref used, textStart))
                        return false;
                    var text = data[textStart..];
                    if (compressed)
                    {
                        var expanded = await ExpandAsync(text, FeedbackScreenshot.MaxContentBytes - used, cancellationToken);
                        if (expanded is null)
                            return false;
                        text = expanded;
                    }
                    if (!Charge(ref used, text.Length))
                        return false;
                    if (kind != ChunkKind.Icc && !ValidLegacyProfile(
                        data.Span[..keyLength], text.Span, ref used, cancellationToken))
                        return false;
                }
                else if (!Charge(ref used, data.Length))
                    return false;
            }
            offset = next;
        }
        return false;
    }

    private static bool ValidChunkType(ReadOnlySpan<byte> type)
    {
        foreach (var value in type)
            if (!char.IsAsciiLetter((char)value))
                return false;
        return true;
    }

    private static ChunkKind KindOf(ReadOnlySpan<byte> type) =>
        type.SequenceEqual("IEND"u8) ? ChunkKind.End :
        type.SequenceEqual("IHDR"u8) || type.SequenceEqual("IDAT"u8) || type.SequenceEqual("fdAT"u8) ? ChunkKind.Pixels :
        type.SequenceEqual("tEXt"u8) ? ChunkKind.Text :
        type.SequenceEqual("zTXt"u8) ? ChunkKind.CompressedText :
        type.SequenceEqual("iTXt"u8) ? ChunkKind.InternationalText :
        type.SequenceEqual("iCCP"u8) ? ChunkKind.Icc : ChunkKind.Other;

    private static int TextStart(ReadOnlySpan<byte> data, ChunkKind kind, out int keyLength, out bool compressed)
    {
        keyLength = data.IndexOf((byte)0);
        compressed = kind is ChunkKind.CompressedText or ChunkKind.Icc;
        if (keyLength is < 1 or > 79)
            return -1;
        var start = keyLength + 1;
        if (kind == ChunkKind.Text)
            return start;
        if (kind is ChunkKind.CompressedText or ChunkKind.Icc)
            return start < data.Length && data[start] == 0 ? start + 1 : -1;
        if (data.Length - start < 4 || data[start] is not (0 or 1) || data[start + 1] != 0)
            return -1;
        compressed = data[start] == 1;
        start += 2;
        for (var field = 0; field < 2; field++)
        {
            var end = data[start..].IndexOf((byte)0);
            if (end < 0)
                return -1;
            start += end + 1;
        }
        return start;
    }

    private static async Task<byte[]?> ExpandAsync(
        ReadOnlyMemory<byte> compressed, int maximum, CancellationToken cancellationToken)
    {
        using var source = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream(Math.Min(8192, maximum));
        var buffer = new byte[8192];
        for (; ; )
        {
            var count = await zlib.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                return output.ToArray();
            var required = output.Length + count;
            if (required > maximum)
                return null;
            if (required > output.Capacity)
                output.Capacity = (int)Math.Min(maximum, Math.Max(required, (long)output.Capacity * 2));
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static bool ValidLegacyProfile(
        ReadOnlySpan<byte> keyword, ReadOnlySpan<byte> bytes, ref int used, CancellationToken cancellationToken)
    {
        var exif = AsciiEquals(keyword, "Raw profile type exif"u8);
        var iptc = AsciiEquals(keyword, "Raw profile type iptc"u8);
        if (!exif && !iptc)
            return true;
        var text = Encoding.Latin1.GetString(bytes).AsSpan().TrimStart();
        if (text.Length < 4 || !text[..4].Equals(exif ? "exif" : "iptc", StringComparison.OrdinalIgnoreCase))
            return false;
        if (exif)
            text = text[4..].TrimStart();
        else
        {
            var headerEnd = text.IndexOf('\n');
            if (headerEnd < 0)
                return false;
            text = text[(headerEnd + 1)..].TrimStart();
        }
        var lengthEnd = text.IndexOf('\n');
        if (lengthEnd < 0 || !int.TryParse(text[..lengthEnd], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var declared) || declared < (exif ? 6 : 1)
            || declared > FeedbackScreenshot.MaxContentBytes)
            return false;
        text = text[(lengthEnd + 1)..];
        var hexDigits = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 8191) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var character = text[index];
            if (char.IsAsciiHexDigit(character))
                hexDigits++;
            else if (!char.IsWhiteSpace(character))
                return false;
        }
        return hexDigits == (long)declared * 2 && Charge(ref used, declared);
    }

    private static bool AsciiEquals(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length != second.Length)
            return false;
        for (var index = 0; index < first.Length; index++)
        {
            var left = first[index];
            var right = second[index];
            if (left is >= (byte)'A' and <= (byte)'Z') left += 32;
            if (right is >= (byte)'A' and <= (byte)'Z') right += 32;
            if (left != right)
                return false;
        }
        return true;
    }

    private static bool Charge(ref int used, int bytes)
    {
        if (bytes > FeedbackScreenshot.MaxContentBytes - used)
            return false;
        used += bytes;
        return true;
    }

    private static bool ValidCrc(ReadOnlySpan<byte> bytes, uint expected, CancellationToken cancellationToken)
    {
        var crc = uint.MaxValue;
        for (var index = 0; index < bytes.Length; index++)
        {
            if ((index & 8191) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            crc ^= bytes[index];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
        }
        return ~crc == expected;
    }

    private enum ChunkKind { Pixels, End, Text, CompressedText, InternationalText, Icc, Other }
}
