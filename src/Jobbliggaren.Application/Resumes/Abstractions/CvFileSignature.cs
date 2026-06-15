namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// Deterministic, defence-in-depth resolution of a CV upload's <see cref="CvFileKind"/>
/// from its magic bytes AND declared content-type (F4-8, security-auditor scope). The
/// magic bytes are authoritative — a renamed <c>.exe</c> (or any non-PDF/DOCX payload)
/// is rejected regardless of a spoofable declared MIME, and a declared content-type that
/// contradicts the magic bytes is rejected as a mismatch.
/// </summary>
public static class CvFileSignature
{
    public const string PdfContentType = "application/pdf";

    public const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // "%PDF"
    private static ReadOnlySpan<byte> PdfMagic => [0x25, 0x50, 0x44, 0x46];

    // "PK\x03\x04" — DOCX is an OPC (zip) container.
    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Resolves the file kind from <paramref name="bytes"/> + <paramref name="contentType"/>.
    /// Returns <c>false</c> (unsupported/mismatch) when the magic bytes match no
    /// supported format, or when the declared content-type explicitly names the OTHER
    /// supported format. An empty/unknown content-type is tolerated (the magic bytes
    /// decide).
    /// </summary>
    public static bool TryResolve(string? contentType, ReadOnlySpan<byte> bytes, out CvFileKind kind)
    {
        if (bytes.StartsWith(PdfMagic))
        {
            kind = CvFileKind.Pdf;
            return ContentTypeAllowsOrUnknown(contentType, PdfContentType);
        }

        if (bytes.StartsWith(ZipMagic))
        {
            kind = CvFileKind.Docx;
            return ContentTypeAllowsOrUnknown(contentType, DocxContentType);
        }

        kind = default;
        return false;
    }

    // Allows an empty/unknown/generic content-type (the magic bytes are authoritative),
    // or one that matches the format the magic bytes already identified. A content-type
    // that names a different supported format is a mismatch → reject.
    private static bool ContentTypeAllowsOrUnknown(string? contentType, string expected)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return true;

        // Strip parameters (e.g. "; charset=utf-8") and normalize.
        var separatorIndex = contentType.IndexOf(';');
        var mediaType = (separatorIndex >= 0 ? contentType[..separatorIndex] : contentType)
            .Trim();

        if (mediaType.Length == 0
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mediaType.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
