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

    // ── Word-boundary phrase matching on RAW prose (NOT lexemes) ──────────
    // A shared, hand-rolled boundary helper for the cliché/soft-skill rules + the cliché
    // transform (#490/#496). It matches a phrase anywhere in the prose but only on a WORD
    // boundary, so "Social" hits "social kompetens" yet never "sociala"/"socialsekreterare",
    // and "Flexibel" never "flexibelt". It runs on the raw prose (not the analyzer's lexeme
    // stream) so the matched offset is verbatim and can ground a cited span (Invariant 2 —
    // the lexeme stream drops the original offsets the evidence must quote).

    /// <summary>True if <paramref name="phrase"/> occurs in <paramref name="source"/> on a word
    /// boundary (case-insensitive). Boundary = the char immediately before/after the phrase is not
    /// a letter; <see cref="char.IsLetter(char)"/> is Unicode-aware, so åäö count as word
    /// characters and only a genuine standalone occurrence matches.</summary>
    public static bool ContainsWord(string source, string phrase) =>
        WordBoundaryIndex(source, phrase, 0) >= 0;

    /// <summary>Every non-overlapping word-bounded, case-insensitive occurrence of
    /// <paramref name="phrase"/> in <paramref name="source"/>, left to right (deterministic —
    /// #496). Each span quotes the verbatim (original-case) occurrence for a grounded citation.</summary>
    public static IEnumerable<TextSpan> WordSpans(string source, string phrase)
    {
        var from = 0;
        while (true)
        {
            var index = WordBoundaryIndex(source, phrase, from);
            if (index < 0)
            {
                yield break;
            }

            yield return new TextSpan(index, phrase.Length, source.Substring(index, phrase.Length));
            from = index + phrase.Length;
        }
    }

    // The index of the first word-bounded, case-insensitive occurrence of `phrase` at or after
    // `startFrom`, or -1. Case folding via OrdinalIgnoreCase resolves å/Å, ä/Ä, ö/Ö (simple 1:1
    // mappings); the flanks are tested with char.IsLetter so åäö bound a word like any letter.
    private static int WordBoundaryIndex(string source, string phrase, int startFrom)
    {
        if (string.IsNullOrEmpty(phrase) || startFrom > source.Length)
        {
            return -1;
        }

        var index = source.IndexOf(phrase, startFrom, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var after = index + phrase.Length;
            var leftIsBoundary = index == 0 || !char.IsLetter(source[index - 1]);
            var rightIsBoundary = after == source.Length || !char.IsLetter(source[after]);
            if (leftIsBoundary && rightIsBoundary)
            {
                return index;
            }

            index = source.IndexOf(phrase, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return -1;
    }

    /// <summary>Splits prose into sentence-ish segments on terminal punctuation (.!?) and line
    /// breaks, so a criterion can ask "is there a concrete example NEXT TO this phrase" (same
    /// sentence) instead of anywhere in the CV (#490). Blank segments are dropped; order kept.</summary>
    public static IReadOnlyList<string> Sentences(string prose) =>
        prose
            .Split(['.', '!', '?', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>True if some sentence that contains <paramref name="phrase"/> (word-bounded) also
    /// carries a MEASURABLE digit (employment dates masked, #487) — the soft skill is backed by a
    /// concrete example sitting beside it, not merely by a date elsewhere in the CV (#490: the old
    /// "any digit anywhere" check left A9's Fail branch effectively dead).</summary>
    public static bool HasMeasurableExampleNear(string prose, string phrase) =>
        Sentences(prose).Any(s => ContainsWord(s, phrase) && ContainsMeasurableDigit(s));

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

    /// <summary>A text-span citation for the first WORD-BOUNDED occurrence of
    /// <paramref name="phrase"/> in <paramref name="source"/> (case-insensitive), quoting the
    /// verbatim occurrence. Unlike <see cref="SpanCaseInsensitive"/> it never cites a mid-word
    /// substring — the offset is the same word-bounded match the rule flagged (#490/#496).</summary>
    public static TextSpanEvidence SpanWord(string source, string phrase, string? note = null)
    {
        var span = WordSpans(source, phrase).FirstOrDefault();
        return span is not null
            ? new TextSpanEvidence(span, note)
            : new TextSpanEvidence(new TextSpan(0, phrase.Length, phrase), note);
    }

    /// <summary>A non-PII structural observation citation (parity SectionConfidence.Evidence).</summary>
    public static StructuralEvidence Structural(string observation) => new(observation);

    public static IReadOnlyList<CitedEvidence> Cite(CitedEvidence evidence) => [evidence];
}
