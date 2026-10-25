using Jobbliggaren.Application.Admin.Email.Dmarc;
using MimeKit;
using MimeKit.Encodings;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — which part of a message can be an aggregate report, decided from header fields alone. Each
/// field value reaches MimeKit's <c>TryParse</c> on its own; no MimeKit structure parser ever sees a message. A file
/// name is read only to match its extension and goes nowhere else.
/// </summary>
internal static class ReportPartRules
{
    public static ParsedHeader Parse(MimeHeaderFields fields)
    {
        var (state, type) = fields.ContentType switch
        {
            null => (TypeState.Absent, null),
            var value when ContentType.TryParse(value, out var parsed) => (TypeState.Valid, parsed),
            _ => (TypeState.Invalid, (ContentType?)null),
        };
        var disposition = fields.ContentDisposition is { } field && ContentDisposition.TryParse(field, out var parsedDisposition)
            ? parsedDisposition
            : null;
        return new ParsedHeader(state, type, disposition, TransferEncodingOf(fields.ContentTransferEncoding));
    }

    /// <summary>A failure report (RFC 9991): never fetched further, whatever its parts hold.</summary>
    public static bool IsForensic(ParsedHeader header) =>
        header.Type is { } type
        && type.IsMimeType("multipart", "report")
        && type.Parameters.TryGetValue("report-type", out string? reportType)
        && string.Equals(reportType, "feedback-report", StringComparison.OrdinalIgnoreCase);

    public static bool IsMultipart(ParsedHeader header) =>
        header.Type is { } type && string.Equals(type.MediaType, "multipart", StringComparison.OrdinalIgnoreCase);

    public static bool IsDigest(ParsedHeader header) => header.Type is { } type && type.IsMimeType("multipart", "digest");

    /// <summary>
    /// How a part is packed if it can be the report, else <see langword="null"/>. The media type decides where it is a
    /// report type; otherwise the file name's extension does. A nested multipart or an embedded message is never a
    /// candidate, and neither is a part whose Content-Type is present but unreadable. Inside a digest a part without a
    /// Content-Type is an embedded message (RFC 2046 §5.1.5).
    /// </summary>
    public static DmarcReportPartKind? KindOf(ParsedHeader header, bool insideDigest)
    {
        if (header.State == TypeState.Invalid || (header.State == TypeState.Absent && insideDigest))
            return null;

        var type = header.Type;
        if (type is not null
            && (string.Equals(type.MediaType, "multipart", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.MediaType, "message", StringComparison.OrdinalIgnoreCase)))
            return null;

        return KindOfMediaType(type) ?? KindOfName(header.Disposition?.FileName ?? type?.Name);
    }

    public static byte[] Decode(byte[] content, TransferEncoding encoding) => encoding switch
    {
        TransferEncoding.Identity => content,
        TransferEncoding.Base64 => Run(new Base64Decoder(), content),
        TransferEncoding.QuotedPrintable => Run(new QuotedPrintableDecoder(), content),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Only a decodable encoding reaches the decoder."),
    };

    private static DmarcReportPartKind? KindOfMediaType(ContentType? type) => type switch
    {
        null => null,
        _ when type.IsMimeType("application", "gzip") || type.IsMimeType("application", "x-gzip") => DmarcReportPartKind.Gzip,
        _ when type.IsMimeType("application", "zip") || type.IsMimeType("application", "x-zip-compressed") => DmarcReportPartKind.Zip,
        _ when type.IsMimeType("text", "xml") || type.IsMimeType("application", "xml") => DmarcReportPartKind.Xml,
        _ => null,
    };

    private static DmarcReportPartKind? KindOfName(string? name) => name switch
    {
        null => null,
        _ when name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) => DmarcReportPartKind.Gzip,
        _ when name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) => DmarcReportPartKind.Zip,
        _ when name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) => DmarcReportPartKind.Xml,
        _ => null,
    };

    private static TransferEncoding TransferEncodingOf(byte[]? value)
    {
        if (value is null)
            return TransferEncoding.Identity;

        var token = System.Text.Encoding.ASCII.GetString(value).Trim(' ', '\t');
        return token.ToLowerInvariant() switch
        {
            "7bit" or "8bit" or "binary" => TransferEncoding.Identity,
            "base64" => TransferEncoding.Base64,
            "quoted-printable" => TransferEncoding.QuotedPrintable,
            _ => TransferEncoding.Unsupported,
        };
    }

    private static byte[] Run(IMimeDecoder decoder, byte[] content)
    {
        var output = new byte[decoder.EstimateOutputLength(content.Length)];
        var length = decoder.Decode(content, 0, content.Length, output);
        return output[..length];
    }
}

/// <summary>
/// One part's header as selection needs it. A class and not a record, so no generated <c>ToString</c> can carry the
/// file name anywhere.
/// </summary>
internal sealed class ParsedHeader(TypeState state, ContentType? type, ContentDisposition? disposition, TransferEncoding encoding)
{
    public TypeState State { get; } = state;

    public ContentType? Type { get; } = type;

    public ContentDisposition? Disposition { get; } = disposition;

    public TransferEncoding Encoding { get; } = encoding;
}

internal enum TypeState
{
    /// <summary>No Content-Type field: the RFC 2045 default, or inside a digest an embedded message.</summary>
    Absent,
    Invalid,
    Valid,
}

internal enum TransferEncoding
{
    Identity,
    Base64,
    QuotedPrintable,
    Unsupported,
}
