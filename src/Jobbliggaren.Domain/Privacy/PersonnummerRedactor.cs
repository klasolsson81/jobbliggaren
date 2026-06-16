using System.Text;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Redacts Swedish personal identity numbers from free text (ADR 0074 Invariant 1; CLAUDE.md §5).
/// Reuses the same detection as <see cref="PersonnummerScanner"/> (regex candidates gated by
/// <c>Personnummer.TryParse</c>'s strict date + Luhn check) and replaces each matched span with
/// its <c>Masked</c> form (every digit → <c>*</c>, separators kept) — so only REAL personnummer
/// are touched, never arbitrary digit runs. Deterministic, allocation-light, never throws.
///
/// <para>Used to harden CV-review evidence: a verdict's cited text-span may quote a slice of the
/// user's CV that contains a personnummer (the user wrote it there); this strips it before the
/// evidence can be logged/cached/persisted. Detection runs on the ORIGINAL text directly so the
/// match offsets map correctly (a spaced/OCR-gapped personnummer inside a single quoted snippet
/// is a documented v1 limitation — the import-time guard already flags the CV regardless).</para>
/// </summary>
public static class PersonnummerRedactor
{
    /// <summary>
    /// Returns <paramref name="text"/> with every detected personnummer/samordningsnummer replaced
    /// by its masked form. Returns the input unchanged when there is nothing to redact.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var matches = PersonnummerScanner.Scan(text);
        if (matches.Count == 0)
            return text;

        // Replace right-to-left so each replacement does not shift the offsets of the ones still
        // to come (a masked form may differ in length from the original separator-bearing token).
        var buffer = new StringBuilder(text);
        foreach (var match in matches.OrderByDescending(m => m.StartOffset))
            buffer.Remove(match.StartOffset, match.Length).Insert(match.StartOffset, match.Masked);

        return buffer.ToString();
    }
}
