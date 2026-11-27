namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// What a PDF's glyph geometry says about one line of <see cref="CvExtractionResult.RawText"/>
/// (#2353). Held in memory at import and never persisted. A line the extractor did not measure, such
/// as any DOCX line or a line on a PDF page it could not align, is <see cref="Unmeasured"/>.
/// </summary>
/// <param name="FirstSegmentLength">The trimmed length of the line's first physical segment when the
/// extractor joined a layout wrap into the line; null when the line is one physical line.</param>
/// <param name="Left">Where the line starts on its page, in PDF points; null when unmeasured.</param>
/// <param name="SpaceWidth">The line's own smallest gap between words, in PDF points; null when
/// unmeasured or when the line holds one word.</param>
public readonly record struct CvLineProvenance(int? FirstSegmentLength, double? Left, double? SpaceWidth)
{
    public static CvLineProvenance Unmeasured => default;

    /// <summary>The extractor joined at least one layout wrap into this line.</summary>
    public bool Joined => FirstSegmentLength is not null;

    /// <summary>The extractor measured where this line starts.</summary>
    public bool Measured => Left is not null;
}
