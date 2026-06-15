namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Why a parse fell back to the manual path (OQ5 — "a manual fallback path",
/// CLAUDE.md §5: never a silent guess). Persisted with the artifact so the UX can
/// explain the degradation rather than blame the user. Carries no PII.
/// </summary>
public enum ParseFallbackReason
{
    /// <summary>No fallback — the parse produced usable structure.</summary>
    None,

    /// <summary>Text extraction failed (corrupt/encrypted file, library error).</summary>
    ExtractionFailed,

    /// <summary>Text was extracted but no recognised section headings were found.</summary>
    NoSectionsDetected,

    /// <summary>The extracted text looks mis-encoded (high replacement-char density).</summary>
    EncodingSuspect,

    /// <summary>The document appears to be a scanned image with no text layer.</summary>
    ScannedImageNoText,
}
