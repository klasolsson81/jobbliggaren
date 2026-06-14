namespace Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

/// <summary>
/// Natural language a text-analysis operation targets. The local NLP tier is
/// language-aware by contract (Fas 4 STEG 2 / F4-2, ADR 0074): Swedish CVs and
/// the Swedish Platsbanken corpus are the matching baseline, but English-language
/// CVs are common in Sweden and must be analysable. F4-2 implements
/// <see cref="Swedish"/> only; <see cref="English"/> is part of the contract but
/// its implementation path is wired at F4-8/9 (CV import/parse + review, where the
/// document language becomes concrete). Requesting an unimplemented language fails
/// fast (<see cref="System.NotSupportedException"/>) and is reported
/// "not assessed v1" (CLAUDE.md §5 honest-data), never silently mis-handled.
/// </summary>
public enum TextLanguage
{
    Swedish,
    English,
}
