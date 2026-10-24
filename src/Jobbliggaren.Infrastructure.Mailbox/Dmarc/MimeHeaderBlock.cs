using System.Text;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the three fields report selection reads from one fetched header block, unfolded (RFC 5322
/// §2.2.3) by a single loop over its lines. Nothing recurses and nothing reads past the bytes the fetch cap let in. A
/// block whose terminating blank line is not among those bytes is never read, because a truncated block can lack the
/// very field that decides.
/// </summary>
internal static class MimeHeaderBlock
{
    public static MimeHeaderRead Read(ReadOnlySpan<byte> block)
    {
        byte[]? contentType = null;
        byte[]? contentDisposition = null;
        byte[]? contentTransferEncoding = null;
        var current = Field.None;
        var value = new List<byte>();
        var position = 0;

        while (true)
        {
            var lineFeed = block[position..].IndexOf((byte)'\n');
            if (lineFeed < 0)
                return new MimeHeaderRead.Unterminated();

            var line = block.Slice(position, lineFeed);
            position += lineFeed + 1;
            if (!line.IsEmpty && line[^1] == (byte)'\r')
                line = line[..^1];

            if (!line.IsEmpty && line[0] is (byte)' ' or (byte)'\t')
            {
                if (current != Field.None)
                    value.AddRange(line);
                continue;
            }

            Store(current, value);
            current = Field.None;
            if (line.IsEmpty)
                return new MimeHeaderRead.Complete(new MimeHeaderFields(contentType, contentDisposition, contentTransferEncoding));

            var colon = line.IndexOf((byte)':');
            if (colon <= 0)
                continue;

            var field = FieldOf(line[..colon].TrimEnd(" \t"u8));
            if (field == Field.None)
                continue;
            if (IsSet(field))
                return new MimeHeaderRead.Ambiguous();

            current = field;
            value.Clear();
            value.AddRange(line[(colon + 1)..]);
        }

        bool IsSet(Field field) => field switch
        {
            Field.ContentType => contentType is not null,
            Field.ContentDisposition => contentDisposition is not null,
            _ => contentTransferEncoding is not null,
        };

        void Store(Field field, List<byte> unfolded)
        {
            switch (field)
            {
                case Field.ContentType:
                    contentType = [.. unfolded];
                    break;
                case Field.ContentDisposition:
                    contentDisposition = [.. unfolded];
                    break;
                case Field.ContentTransferEncoding:
                    contentTransferEncoding = [.. unfolded];
                    break;
            }
        }
    }

    private static Field FieldOf(ReadOnlySpan<byte> name) =>
        Ascii.EqualsIgnoreCase(name, "Content-Type"u8) ? Field.ContentType
        : Ascii.EqualsIgnoreCase(name, "Content-Disposition"u8) ? Field.ContentDisposition
        : Ascii.EqualsIgnoreCase(name, "Content-Transfer-Encoding"u8) ? Field.ContentTransferEncoding
        : Field.None;

    private enum Field
    {
        None,
        ContentType,
        ContentDisposition,
        ContentTransferEncoding,
    }
}

/// <summary>The unfolded values of the fields selection reads; <see langword="null"/> where the block has none.</summary>
internal sealed record MimeHeaderFields(byte[]? ContentType, byte[]? ContentDisposition, byte[]? ContentTransferEncoding);

/// <summary>What reading one header block ends in: a closed set.</summary>
internal abstract record MimeHeaderRead
{
    private MimeHeaderRead() { }

    public sealed record Complete(MimeHeaderFields Fields) : MimeHeaderRead;

    /// <summary>The blank line that ends the block is not among the fetched bytes.</summary>
    public sealed record Unterminated : MimeHeaderRead;

    /// <summary>A field selection reads appears twice, so which one a reader would honour is not knowable.</summary>
    public sealed record Ambiguous : MimeHeaderRead;
}
