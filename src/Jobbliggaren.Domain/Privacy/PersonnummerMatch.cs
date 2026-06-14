namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// A single personnummer/samordningsnummer detection within a body of text.
/// Reports WHERE the match is (so callers can redact the source) and WHAT kind,
/// but never retains the raw value — only <see cref="Masked"/>, a redacted form
/// with none of the real digits. This is a deliberate PII-safety boundary: a
/// match result is itself safe to log (ADR 0074 Invariant 1; CLAUDE.md §5).
/// </summary>
public sealed record PersonnummerMatch
{
    private PersonnummerMatch(int startOffset, int length, PersonnummerKind kind, string masked)
    {
        StartOffset = startOffset;
        Length = length;
        Kind = kind;
        Masked = masked;
    }

    /// <summary>Zero-based offset of the matched token in the source text.</summary>
    public int StartOffset { get; }

    /// <summary>Length of the matched token (in chars) in the source text.</summary>
    public int Length { get; }

    /// <summary>Personnummer or samordningsnummer.</summary>
    public PersonnummerKind Kind { get; }

    /// <summary>Redacted form — contains NONE of the real digits.</summary>
    public string Masked { get; }

    // Internal factory for the scanner (GREEN-phase use). Intentionally not a
    // public construction surface — callers obtain matches only via the scanner.
    internal static PersonnummerMatch Create(
        int startOffset,
        int length,
        PersonnummerKind kind,
        string masked)
        => new(startOffset, length, kind, masked);
}
