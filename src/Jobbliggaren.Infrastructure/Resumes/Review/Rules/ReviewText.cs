using System.Text;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

/// <summary>
/// Shared, deterministic text helpers + cited-evidence builders for the F4-9 criterion
/// rules. No knowledge-bank data lives here (that stays in the F4-7 assets, §5); these are
/// pure string utilities + the two evidence channels (text-span vs structural).
/// </summary>
internal static class ReviewText
{
    /// <summary>Each experience entry's verbatim text (trimmed, non-empty) — the "bullets".</summary>
    public static IReadOnlyList<string> ExperienceBullets(CvReviewContext context) =>
        context.Content.Experience
            .Select(e => e.RawText?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>Profile + all experience text joined, for whole-CV prose scans (lowercased on demand).</summary>
    public static string AllProse(CvReviewContext context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.Content.Profile))
        {
            sb.AppendLine(context.Content.Profile);
        }

        foreach (var experience in context.Content.Experience)
        {
            if (!string.IsNullOrWhiteSpace(experience.RawText))
            {
                sb.AppendLine(experience.RawText);
            }
        }

        return sb.ToString();
    }

    public static bool ContainsDigit(string text) => text.Any(char.IsDigit);

    /// <summary>True if <paramref name="text"/> (lowercased) starts with <paramref name="phrase"/>
    /// on a word boundary (so "ledde" matches "ledde teamet" but not "ledning").</summary>
    public static bool StartsWithWord(string text, string phrase)
    {
        var t = text.TrimStart().ToLowerInvariant();
        var p = phrase.ToLowerInvariant();
        if (!t.StartsWith(p, StringComparison.Ordinal))
        {
            return false;
        }

        return t.Length == p.Length || !char.IsLetter(t[p.Length]);
    }

    // ── Cited-evidence builders (Invariant 2) ────────────────────────────

    /// <summary>A text-span citation: resolves the offset of <paramref name="quote"/> within
    /// <paramref name="source"/> (best-effort; 0 if not found) and quotes it verbatim.</summary>
    public static TextSpanEvidence Span(string source, string quote, string? note = null)
    {
        var index = string.IsNullOrEmpty(quote)
            ? -1
            : source.IndexOf(quote, StringComparison.Ordinal);
        return new TextSpanEvidence(new TextSpan(index >= 0 ? index : 0, quote.Length, quote), note);
    }

    /// <summary>A text-span citation that locates <paramref name="phrase"/> case-insensitively
    /// in <paramref name="source"/> and quotes the verbatim (original-case) occurrence.</summary>
    public static TextSpanEvidence SpanCaseInsensitive(string source, string phrase, string? note = null)
    {
        var index = string.IsNullOrEmpty(phrase)
            ? -1
            : source.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return new TextSpanEvidence(new TextSpan(0, phrase.Length, phrase), note);
        }

        var quote = source.Substring(index, phrase.Length);
        return new TextSpanEvidence(new TextSpan(index, phrase.Length, quote), note);
    }

    /// <summary>A non-PII structural observation citation (parity SectionConfidence.Evidence).</summary>
    public static StructuralEvidence Structural(string observation) => new(observation);

    public static IReadOnlyList<CitedEvidence> Cite(CitedEvidence evidence) => [evidence];
}
