namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// One <see cref="CvLineProvenance"/> per line of <see cref="CvExtractionResult.RawText"/>, indexed
/// as the text's lines split on <c>'\n'</c> (#2353). The extractor builds it in the same pass as the
/// text; the import hands it to the segmenter. It is CV-derived, held in memory only, never
/// persisted and never logged (ADR 0074 Invariant 3).
/// </summary>
public sealed class CvTextProvenance
{
    private readonly CvLineProvenance[] _lines;

    private CvTextProvenance(CvLineProvenance[] lines) => _lines = lines;

    /// <summary>No line is measured.</summary>
    public static CvTextProvenance None { get; } = new([]);

    /// <summary>The provenance of <paramref name="rawText"/>, one entry per line.</summary>
    /// <exception cref="ArgumentException">The entries do not match the text's line count.</exception>
    public static CvTextProvenance For(string rawText, IReadOnlyList<CvLineProvenance> lines)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentNullException.ThrowIfNull(lines);

        var lineCount = rawText.Split('\n').Length;
        if (lines.Count != lineCount)
        {
            throw new ArgumentException(
                $"The provenance has {lines.Count} lines and the text has {lineCount}.", nameof(lines));
        }

        return new CvTextProvenance([.. lines]);
    }

    /// <summary>The number of lines this provenance indexes; 0 for <see cref="None"/>.</summary>
    public int LineCount => _lines.Length;

    /// <summary>The line's provenance, or <see cref="CvLineProvenance.Unmeasured"/> outside the indexed lines.</summary>
    public CvLineProvenance this[int line] =>
        line >= 0 && line < _lines.Length ? _lines[line] : CvLineProvenance.Unmeasured;
}
