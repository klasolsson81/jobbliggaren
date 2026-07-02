using System.Text;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

/// <summary>
/// Shared, deterministic text helpers + cited-evidence builders for the F4-9 criterion
/// rules. No knowledge-bank data lives here (that stays in the F4-7 assets, §5); these are
/// pure string utilities + the two evidence channels (text-span vs structural).
/// </summary>
internal static class ReviewText
{
    /// <summary>
    /// The scored description bullets across all experience entries — the DESCRIPTION lines,
    /// NOT the whole entry block. Each entry's <see cref="ParsedExperience.RawText"/> is the
    /// verbatim block the segmenter emits (a header line with the title/organisation, the
    /// period on its own line, then the description). A1/A2/A6 must read the description, so
    /// the header line and any pure-period / organisation line are excluded (#487) — pre-fix
    /// the whole block was one "bullet", so A1 counted the employment DATES as a measurable
    /// result and A2 read the job TITLE instead of a verb.
    /// </summary>
    public static IReadOnlyList<string> ExperienceBullets(CvReviewContext context)
    {
        var bullets = new List<string>();
        foreach (var experience in context.Content.Experience)
        {
            bullets.AddRange(DescriptionLines(experience));
        }

        return bullets;
    }

    /// <summary>True if the CV states at least one experience entry (regardless of whether any
    /// carries description bullets) — lets A1/A2/A6 tell "no experience stated" apart from
    /// "experience stated but no scorable description lines" in their honest reason (#487).</summary>
    public static bool HasExperienceEntries(CvReviewContext context) =>
        context.Content.Experience.Count > 0;

    /// <summary>The honest NotAssessed reason A1/A2/A6 carry when there are no scorable
    /// bullets — distinguishing "no experience stated" from "experience stated but no
    /// description lines to score" (#487; civic Swedish, §10). <paramref name="subject"/> is
    /// the criterion's aspect ("mätbarhet"/"handlingsverb"/"konkretion").</summary>
    public static string NoBulletsReason(CvReviewContext context, string subject) =>
        HasExperienceEntries(context)
            ? $"Arbetslivserfarenheten saknar beskrivande punkter att bedöma {subject} på."
            : $"Ingen arbetslivserfarenhet att bedöma {subject} på.";

    // The description lines of one experience entry: its RawText lines, EXCLUDING the header
    // line (title/organisation — always the first line the segmenter emits) and any line that
    // is purely the period or the organisation on its own line (the "Title\nCompany\nDates"
    // layout). Reuses the already-structured ParsedExperience fields rather than re-guessing.
    private static IEnumerable<string> DescriptionLines(ParsedExperience experience)
    {
        var lines = experience.RawText
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        // lines[0] is the header (title/organisation, possibly with a trailing period the
        // segmenter recovered separately) — never a description bullet.
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];

            // A line that is purely the period ("2013–2021", "01/2022 – nuvarande") is not a bullet.
            if (PeriodParser.TryParse(line, out _, out _, out _))
            {
                continue;
            }

            // The organisation on its own line (the "Title\nCompany\nDates" layout) is not a bullet.
            if (!string.IsNullOrWhiteSpace(experience.Organization)
                && string.Equals(line, experience.Organization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return line;
        }
    }

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

    /// <summary>True if <paramref name="text"/> carries a digit that is NOT part of an
    /// employment date/period — dates are masked first (<see cref="DatePatterns.StripDates"/>)
    /// so a date row is never counted as a measurable result / concrete artefact (#487).</summary>
    public static bool ContainsMeasurableDigit(string text) =>
        ContainsDigit(DatePatterns.StripDates(text));

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
