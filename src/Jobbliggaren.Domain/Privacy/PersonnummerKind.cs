namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Distinguishes a Swedish personnummer from a samordningsnummer
/// (coordination number). Both are PII the guard must flag for removal
/// (ADR 0074 Invariant 1; CLAUDE.md §5; BUILD §13).
/// </summary>
public enum PersonnummerKind
{
    Personnummer,
    Samordningsnummer,
}
