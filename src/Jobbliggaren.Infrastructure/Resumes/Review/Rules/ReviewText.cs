using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
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
    /// The character cap a cited quote is shortened to before it is flagged as an excerpt
    /// (#1062 B2). <b>Not a rubric threshold</b> and must not be moved into the rubric asset:
    /// it changes no verdict — every rule evaluates the FULL text and only the CITATION is
    /// shortened — so CLAUDE.md §5's "no hardcoded rubric thresholds in C#" does not reach it.
    /// Putting a presentation length into versioned assessment data would misfile it as
    /// something a rubric bump could re-decide. Carried over unchanged from the four
    /// <c>text[..80]</c> call sites this constant replaced.
    /// <para>
    /// ⚠ It IS input to FINDING IDENTITY, and that is the axis "changes no verdict" does not
    /// cover. The quote is <c>FindingTargetFingerprint.BuildCanonicalPayload</c>'s payload, so
    /// changing this cap re-keys every finding cited through <see cref="SpanExcerpt"/> — at an
    /// UNCHANGED rubric version, i.e. with nothing versioning the re-keying. The ledger row
    /// survives (it is keyed on criterionId), but a Resolved row already stamped
    /// <c>StaleAt</c> loses its overlay and the finding returns as actionable.
    /// </para>
    /// </summary>
    public const int ExcerptMaxChars = 80;

    /// <summary>
    /// The scored description bullets across all experience entries — the DESCRIPTION lines,
    /// NOT the whole entry block. A1/A2/A6 must read the description, so on the staging arm
    /// (where <c>Text</c> is the segmenter's verbatim block: header line, period line, then
    /// the description) the header and any pure-period / organisation line are excluded
    /// (#487) — pre-fix the whole block was one "bullet", so A1 counted the employment DATES
    /// as a measurable result and A2 read the job TITLE instead of a verb. On the canonical
    /// arm <c>Text</c> is already the pure description (Fas 4b PR-4, D8).
    /// </summary>
    public static IReadOnlyList<string> ExperienceBullets(CriterionEvaluationContext context)
    {
        var bullets = new List<string>();
        foreach (var experience in context.Content.Experience)
        {
            bullets.AddRange(DescriptionBullets(experience));
        }

        return bullets;
    }

    /// <summary>True if the CV states at least one experience entry (regardless of whether any
    /// carries description bullets) — lets A1/A2/A6 tell "no experience stated" apart from
    /// "experience stated but no scorable description lines" in their honest reason (#487).</summary>
    public static bool HasExperienceEntries(CriterionEvaluationContext context) =>
        context.Content.Experience.Count > 0;

    /// <summary>The honest NotAssessed reason A1/A2/A6 carry when there are no scorable
    /// bullets — distinguishing "no experience stated" from "experience stated but no
    /// description lines to score" (#487; civic Swedish, §10). <paramref name="subject"/> is
    /// the criterion's aspect ("mätbarhet"/"handlingsverb"/"konkretion").</summary>
    public static string NoBulletsReason(CriterionEvaluationContext context, string subject) =>
        HasExperienceEntries(context)
            ? $"Arbetslivserfarenheten saknar beskrivande punkter att bedöma {subject} på."
            : NoExperienceReason(subject);

    /// <summary>The reason a criterion that reads experience carries when the CV states none.</summary>
    public static string NoExperienceReason(string subject) =>
        $"Ingen arbetslivserfarenhet att bedöma {subject} på.";

    /// <summary>A1/A2/A6's NotAssessed verdict when there are no scorable bullets: the kind
    /// travels with <see cref="NoBulletsReason"/> (InsufficientData, #2083).</summary>
    public static CvCriterionVerdict NoBullets(CriterionEvaluationContext context, string subject) =>
        CvCriterionVerdict.NotAssessed(context.Criterion.Id, context.Criterion.Category,
            NotAssessedKind.InsufficientData, NoBulletsReason(context, subject));

    /// <summary>The reason A4, B6 and B7 share when experience entries exist but none of their
    /// periods could be read as a date.</summary>
    public const string UnparsedPeriodsReason = "Perioderna kunde inte tolkas till datum.";

    /// <summary>A4/B6/B7's NotAssessed verdict when no period parses (#2083): the periods are
    /// unreadable, or the CV states no experience at all, told apart as <see cref="NoBulletsReason"/>
    /// does (#487). <paramref name="subject"/> is the criterion's aspect.</summary>
    public static CvCriterionVerdict UnparsedPeriods(CriterionEvaluationContext context, string subject) =>
        CvCriterionVerdict.NotAssessed(context.Criterion.Id, context.Criterion.Category,
            NotAssessedKind.InsufficientData,
            HasExperienceEntries(context) ? UnparsedPeriodsReason : NoExperienceReason(subject));

    /// <summary>
    /// The description bullets of one experience entry in the unified view (Fas 4b PR-4,
    /// ADR 0093 §D8). When <c>Text</c> is the pure description (<c>TextIsDescriptionOnly</c>: a
    /// canonical entry, or a parsed entry whose description the segmenter read), every non-empty
    /// line is a bullet. Otherwise <c>Text</c> is the verbatim block of an entry parsed before the
    /// segmenter read descriptions, and its bullets are <see cref="EntryDescription.Lines"/>, the
    /// function the segmenter stores them with (#2353). Shared with the improve engine's
    /// <c>WeakVerbTransform</c> (via the <see cref="ParsedExperience"/> overload) so the review (A2)
    /// and improve sides score the SAME bullet unit (#487 review side, #534 improve side).
    /// </summary>
    internal static IEnumerable<string> DescriptionBullets(ReviewableExperience experience) =>
        experience.TextIsDescriptionOnly
            ? experience.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
            : EntryDescription.Lines(experience.Text, experience.Organization);

    /// <summary>
    /// The staging-shaped overload the improve engine's <c>WeakVerbTransform</c> scores by
    /// (#534 — the improve flow stays <c>ParsedResume</c>-scoped until PR-7). Maps the entry as the
    /// review does, so the bullet unit stays ONE definition (#487).
    /// </summary>
    internal static IEnumerable<string> DescriptionBullets(ParsedExperience experience) =>
        DescriptionBullets(ReviewableExperience.FromParsed(experience));

    /// <summary>Profile + each experience's description lines joined, for whole-CV prose scans
    /// (lowercased on demand). Both arms scan the same text: the staging block's header, period and
    /// organisation lines are not prose.
    /// <para>NB (#478 Low, v1 scope): a citation resolved against this concatenation carries an offset
    /// into the SYNTHETIC joined string, not into any single <c>RawText</c> field. The verbatim
    /// <c>Quote</c> is the ground truth the UI highlights by; the offset is a positional hint only and
    /// has no UI consumer today, so a precise per-field offset is deferred. What must never happen is a
    /// FABRICATED offset when the quote is absent — see <see cref="Span"/> / <see cref="TextSpan.NotLocated"/>.</para></summary>
    public static string AllProse(CriterionEvaluationContext context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.Content.Profile))
        {
            sb.AppendLine(context.Content.Profile);
        }

        foreach (var experience in context.Content.Experience)
        {
            var text = experience.TextIsDescriptionOnly
                ? experience.Text
                : string.Join('\n', DescriptionBullets(experience));
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The honest NotAssessed reason the whole-CV prose rules (A7/A9/C2/C3/C4/C6/C7) carry when
    /// <see cref="AllProse"/> is empty — no profile text and no experience text to scan. Zero
    /// hits in zero text is not an observation: an affirmative Pass over an empty corpus claims
    /// a PRESENCE of quality never observed, which is ADR 0109's defect class inverted (A8
    /// claimed an ABSENCE it had not observed). The verdicts are withdrawn, never upgraded to
    /// Warn/Fail — the missing CONTENT is A10/B1's subject, and grading emptiness as a language
    /// defect would misreport what was measured. The unclassified <c>Preamble</c> stays out of
    /// the corpus BY DESIGN (ADR 0109) and never rescues these rules.
    /// <para>Deliberately hardcoded C# prose, NOT the rubric asset's <c>NotAssessedReason</c>:
    /// that field is the criterion's single GENERIC reason slot, which would be false for this
    /// specific branch — a BRANCH-specific reason is bespoke C# by house precedent
    /// (<see cref="NoBulletsReason"/>, <see cref="UnparsedPeriodsReason"/>, E2's PR-6b reason).
    /// Civic Swedish (§10). <paramref name="aspect"/> is the criterion's assessed aspect in
    /// definite form ("tonen"/"stavningen"/…).</para>
    /// </summary>
    public static string NoProseReason(string aspect) =>
        $"CV:t saknar profil- och erfarenhetstext, så {aspect} kan inte bedömas.";

    /// <summary>The prose rules' NotAssessed verdict over an empty corpus: the kind travels with
    /// <see cref="NoProseReason"/> (InsufficientData, #2083).</summary>
    public static CvCriterionVerdict NoProse(CriterionEvaluationContext context, string aspect) =>
        CvCriterionVerdict.NotAssessed(context.Criterion.Id, context.Criterion.Category,
            NotAssessedKind.InsufficientData, NoProseReason(aspect));

    public static bool ContainsDigit(string text) => text.Any(char.IsDigit);

    /// <summary>True if <paramref name="text"/> carries a digit that is NOT part of an
    /// employment date/period — dates are masked first (<see cref="DatePatterns.StripDates"/>)
    /// so a date row is never counted as a measurable result / concrete artefact (#487).</summary>
    public static bool ContainsMeasurableDigit(string text) =>
        ContainsDigit(DatePatterns.StripDates(text));

    /// <summary>The bullet from its first word on. A bullet's first word is its first token that
    /// carries a letter or a digit, the token the analyzer gives A2 as the opener, so a lead glyph
    /// is never a word (#2352).</summary>
    public static string FromFirstWord(string bullet)
    {
        for (var i = 0; i < bullet.Length; i++)
        {
            if (char.IsLetterOrDigit(bullet[i]))
            {
                return bullet[i..];
            }
        }

        return string.Empty;
    }

    /// <summary>True if <paramref name="text"/> (lowercased) starts with <paramref name="phrase"/>
    /// on a word boundary (so "ledde" matches "ledde teamet" but not "ledning").</summary>
    public static bool StartsWithWord(string text, string phrase) =>
        StartsWithLoweredWord(text.TrimStart().ToLowerInvariant(), phrase.ToLowerInvariant());

    /// <summary>The <see cref="StartsWithWord"/> test on a text and a phrase that are both already
    /// lower-cased, so a caller testing one text against many phrases lower-cases each once.</summary>
    public static bool StartsWithLoweredWord(string loweredText, string loweredPhrase) =>
        loweredText.StartsWith(loweredPhrase, StringComparison.Ordinal)
        && (loweredText.Length == loweredPhrase.Length || !char.IsLetter(loweredText[loweredPhrase.Length]));

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
    /// <paramref name="source"/> and quotes it verbatim. When the quote is not a substring of the
    /// source, <see cref="TextSpan.Start"/> is <see cref="TextSpan.NotLocated"/> — an honest
    /// "position unknown" rather than a fabricated offset 0 (#478 Low). The <see cref="TextSpan.Quote"/>
    /// stays the verbatim ground truth the UI highlights by.</summary>
    public static TextSpanEvidence Span(string source, string quote, string? note = null)
    {
        var index = string.IsNullOrEmpty(quote)
            ? -1
            : source.IndexOf(quote, StringComparison.Ordinal);
        return new TextSpanEvidence(
            new TextSpan(index >= 0 ? index : TextSpan.NotLocated, quote.Length, quote), note);
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits <see cref="ExcerptMaxChars"/> and
    /// ends on a WORD boundary, plus whether shortening actually happened (#1062 B2). Pre-fix the
    /// four A8 call sites each did <c>text[..80]</c> and cited "Jag ä" — the engine reporting a
    /// fragment it invented as the user's own words, on the Pass path too.
    /// <para>The result is always a PREFIX of <paramref name="text"/> (a cut plus a
    /// <c>TrimEnd</c>), so it stays a verbatim substring of whatever source that text came from
    /// and the located-offset invariant survives. No "…" is appended — see
    /// <see cref="TextSpan.IsExcerpt"/> for why the glyph is the client's.</para>
    /// <para>An unbroken run longer than the cap (no whitespace to cut at) falls back to the hard
    /// cut. That is the one case where a mid-word end is the honest outcome: there is no word
    /// boundary to find, and the alternative — sending the whole run — is the unbounded
    /// PII-bearing quote the cap exists to prevent. That hard cut steps back off a lone
    /// surrogate, for the reason <c>PreambleResidue.Truncate</c> already wrote down: half an
    /// astral character is not text, and this quote reaches
    /// <c>FindingTargetFingerprint.Normalize</c>, whose <c>string.Normalize(FormC)</c> throws on
    /// invalid Unicode. The word-boundary branch cannot split a pair — it always cuts AT a
    /// whitespace index — so the guard is only ever needed here.</para>
    /// </summary>
    public static (string Quote, bool IsExcerpt) Excerpt(string text)
    {
        if (text.Length <= ExcerptMaxChars)
        {
            return (text, false);
        }

        var head = HardCut(text);

        // The character the cap fell ON decides whether `head` already ends a word: if it is
        // whitespace, the cut sits exactly at a boundary and nothing needs backing off.
        var cut = char.IsWhiteSpace(text[ExcerptMaxChars])
            ? head
            : head[..(LastWhitespace(head) is var i and >= 0 ? i : head.Length)];

        var trimmed = cut.TrimEnd();

        // All-whitespace head (or a boundary at index 0): TrimEnd would leave nothing to cite,
        // so keep the hard cut rather than emit an empty quote.
        return (trimmed.Length > 0 ? trimmed : head, true);
    }

    private static string HardCut(string text)
    {
        var head = text[..ExcerptMaxChars];
        return char.IsHighSurrogate(head[^1]) ? head[..^1] : head;
    }

    private static int LastWhitespace(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A text-span citation whose quote is shortened to an excerpt when the cited text exceeds
    /// <see cref="ExcerptMaxChars"/> (#1062 B2) — the ONE home for review-side citation
    /// shortening. Offset resolution is <see cref="Span"/>'s, unchanged: the excerpt is a prefix
    /// of the cited text and therefore still locatable in <paramref name="source"/> — locatable,
    /// not unique. The offset is the FIRST occurrence, exactly as <see cref="Span"/>'s is, and a
    /// shorter needle does not make it more unique.
    /// </summary>
    public static TextSpanEvidence SpanExcerpt(string source, string text, string? note = null)
    {
        var (quote, isExcerpt) = Excerpt(text);
        var index = string.IsNullOrEmpty(quote)
            ? -1
            : source.IndexOf(quote, StringComparison.Ordinal);
        return new TextSpanEvidence(
            new TextSpan(index >= 0 ? index : TextSpan.NotLocated, quote.Length, quote, isExcerpt),
            note);
    }

    /// <summary>A text-span citation for the first WORD-BOUNDED occurrence of
    /// <paramref name="phrase"/> in <paramref name="source"/> (case-insensitive), quoting the
    /// verbatim occurrence. Unlike a plain case-insensitive substring lookup (<see cref="Span"/>
    /// with a lowercased needle) it never cites a mid-word substring — the offset is the same
    /// word-bounded match the rule flagged (#490/#496).</summary>
    public static TextSpanEvidence SpanWord(string source, string phrase, string? note = null)
    {
        var span = WordSpans(source, phrase).FirstOrDefault();
        return span is not null
            ? new TextSpanEvidence(span, note)
            : new TextSpanEvidence(new TextSpan(TextSpan.NotLocated, phrase.Length, phrase), note);
    }

    /// <summary>A non-PII structural observation citation (parity SectionConfidence.Evidence).</summary>
    public static StructuralEvidence Structural(string observation) => new(observation);

    public static IReadOnlyList<CitedEvidence> Cite(CitedEvidence evidence) => [evidence];
}
