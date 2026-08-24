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
            bullets.AddRange(DescriptionLines(experience));
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
            : $"Ingen arbetslivserfarenhet att bedöma {subject} på.";

    /// <summary>
    /// The description lines of one experience entry in the unified view (Fas 4b PR-4,
    /// ADR 0093 §D8). A canonical entry's <c>Text</c> IS the pure description
    /// (<c>TextIsDescriptionOnly</c>) — every non-empty line is a bullet. A staging
    /// entry's <c>Text</c> is the segmenter's verbatim block, so the header line
    /// (title/organisation — always the first line the segmenter emits) and any line
    /// that is purely the period or the organisation on its own line (the
    /// "Title\nCompany\nDates" layout) are excluded (#487). Shared with the improve
    /// engine's <c>WeakVerbTransform</c> (via the <see cref="ParsedExperience"/>
    /// overload) so the review (A2) and improve sides score the SAME bullet unit
    /// (#487 review side, #534 improve side).
    /// </summary>
    internal static IEnumerable<string> DescriptionLines(ReviewableExperience experience)
    {
        var lines = experience.Text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (experience.TextIsDescriptionOnly)
        {
            foreach (var line in lines)
            {
                yield return line;
            }

            yield break;
        }

        // lines[0] is the header (title/organisation, possibly with a trailing period the
        // segmenter recovered separately) — never a description bullet.
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];

            // A line that is purely the period ("2013–2021", "01/2022 – nuvarande") is not a bullet.
            //
            // A UNION of two predicates, and it is deliberately not one (#1060 β-3 follow-up).
            // NEITHER SUBSUMES THE OTHER — each reaches forms the other declines, in BOTH
            // directions. The axes are not restated here: they live behind ONE pointer,
            // DatePatterns.IsDateOnlyLine's docblock, which in turn names
            // DatePatternsDateOnlyLineTests as the adjudicating InlineData. A prose copy of a list
            // the date-model widening will change would rot in a file that widening does not
            // otherwise touch. No total is published in either home: the count is emergent from two
            // independently written grammars.
            //
            // Replacing rather than adding is therefore a suppression regression in one direction
            // or the other, and it releases the line into this method's consumers —
            // ExperienceBullets below; StructureRules' B5, which reads this method but nulls any
            // marker whose remainder PeriodParser parses, so it acts on neither released form; and
            // WeakVerbTransform's bullet unit, which IS this method (measured in
            // ReviewTextPeriodLineUnionTests). What that transform then does is DECLINE to propose:
            // it proposes only for a bullet OPENING with a drop-in-safe weak verb from the
            // KnowledgeBank mapping (WeakVerbTransform.cs:46-51), and no date row opens with one
            // (naming an opening GLYPH instead would be wrong for the leading-separator direction).
            // That the transform DECLINES is read from its firing condition, not run — same
            // provenance as the A1/A2/A6 clause below. What IS read directly is that its bullet
            // unit is this method: WeakVerbTransform.cs:34 iterates DescriptionLines — read at
            // that line, not measured by a test that calls Propose. Offered, not
            // acted on — stating it as "not released into WeakVerbTransform" would over-correct in
            // the other direction.
            //
            // What ExperienceBullets' criteria (A1/A2/A6) do with a released row — score it and
            // CITE it as though it were prose — WAS derived from reading the rules and is now
            // MEASURED (#1060 road 3, (S1)). On the three-line layout, before the widening, all
            // three cited the user's date row; on "2020/01 – 2024/12" A1 returned an affirmative
            // Pass noting "kvantifierad uppgift" — the product asserting she had quantified a
            // result out of her employment dates. The run agreed with the derivation and sharpened
            // it. DateModelWideningReviewSideTests is the adjudicator; do not restate this as
            // derived.
            if (PeriodParser.TryParse(line, out _, out _, out _) || DatePatterns.IsDateOnlyLine(line))
            {
                continue;
            }

            // The organisation on its own line (the "Title\nCompany\nDates" layout) is not a bullet.
            if (!string.IsNullOrWhiteSpace(experience.Organization)
                && string.Equals(line, experience.Organization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The β-3 residual this union closes, kept because it records WHY the union exists.
            // The two suppressions above used to overlap: a period line PeriodParser rejects was
            // often still caught by the organization-equality test, because the segmenter had put
            // that very line in the organization slot. β-3 stopped that fabrication, so the overlap
            // went with it and a period line with a LEADING separator ("– 2020 – 2024") escaped
            // both — and was yielded as a description bullet for the review criteria to score.
            // Closed by the DatePatterns disjunct above, which suppresses it structurally rather
            // than as a side effect of a defect.
            //
            // WHAT REMAINED WHEN THE UNION LANDED, kept as the record of a prediction that came
            // TRUE and was then closed: a date line DatePatterns did not MODEL ("jan 2020 – dec
            // 2024", "2020 – 2024 (heltid)", "2020/01 – 2024/12", "2020 –") was not reduced, so
            // IsDateOnlyLine declined it exactly as PeriodParser did. The promotion FACTORED today's
            // model into a shared home; it did not widen it, and sharing a predicate does not extend
            // one.
            //
            // THE DATE MODEL HAS SINCE BEEN WIDENED (#1060 road 3) AND THREE OF THE FOUR ARE NOW
            // SUPPRESSED HERE. The month-name POINT form went into DatePatterns.DateRange — and,
            // because that regex's match value is what ExtractPeriod STORES, into PeriodParser's
            // point grammar with it, from a month-word home the two types share. The two LINE forms
            // went into DatePatterns.IsIgnorableTail, deliberately outside the match value. So this
            // call site suppresses those three through BOTH disjuncts for the point form and through
            // the DatePatterns disjunct alone for the line forms.
            //
            // THE FOURTH, YYYY/MM ("2020/01 – 2024/12"), WAS ADDED, DECLINED (round 5) AND IS NOW
            // SUPPRESSED THROUGH THE DatePatterns DISJUNCT ALONE (ADR 0136): it collided with the
            // Swedish läsår notation and a mixed-endpoint form of it regressed a working origin/main
            // degradation into an unparseable stored value, so the VALUE grammar still declines it
            // and PeriodParser still refuses it. The LINE grammar reads it, which is why this
            // disjunct fires and the other does not — pinned in
            // ReviewTextPeriodLineUnionTests.DescriptionLines_SuppressesTheSlashDateRow_…, because a
            // union that started passing through BOTH halves would be a different change wearing
            // this one's result. The union is otherwise unchanged and is still a union: neither
            // predicate subsumes the other, and DatePatternsDateOnlyLineTests owns the axis list.
            //
            // TWO DEFERRALS, AND THE ORDER WAS LOAD-BEARING — the promotion FIRST, the widening
            // SECOND. Not a preference, and the argument was about the TWO-LINE layout specifically:
            // there the date row is Lines[1] and therefore the organisation candidate, so the
            // segmenter fabricated it into Organization and the equality test above fired on it.
            // Widening the date model FIRST would have made Organization correctly null, stopped
            // that test firing, and — without this union already in place — released the line into
            // ExperienceBullets. That would have traded a fabricated employer for a criterion citing
            // the user's date row as prose: two CLAUDE.md §5 CV-engine classes, not a fix. With the
            // union here first, the widening extended a real suppression instead of removing an
            // accidental one, which is what happened.
            //
            // THE QUALIFIER WAS LOAD-BEARING TOO, and it was measured rather than assumed. On the
            // THREE-LINE "Title / Company / Dates" layout the employer is real, nothing fabricates
            // the date row, and neither half of this union modelled those four forms — so the row
            // REACHED the bullet scorer, and A1/A2/A6 scored and cited it. Saying "those four forms
            // are suppressed today" without the layout qualifier would have been a claim sized
            // against one layout and read as a claim about the class. That hole is now closed and
            // pinned at both altitudes: the bullet unit in
            // ReviewTextPeriodLineUnionTests.DescriptionLines_ShouldNowSuppressTheDateRow_…, and the
            // verdicts in DateModelWideningReviewSideTests (senior-cto-advisor bind 2026-08-02 §2,
            // qualified by test-writer's measurement the same day, closed by road 3).

            yield return line;
        }
    }

    /// <summary>
    /// The staging-shaped overload the improve engine's <c>WeakVerbTransform</c> scores by
    /// (#534 — the improve flow stays <c>ParsedResume</c>-scoped until PR-7). Delegates to
    /// the unified-view logic above so the bullet unit stays ONE definition (#487).
    /// </summary>
    internal static IEnumerable<string> DescriptionLines(ParsedExperience experience) =>
        DescriptionLines(new ReviewableExperience(
            experience.Title, experience.Organization, experience.Period,
            null, null, experience.RawText, TextIsDescriptionOnly: false));

    /// <summary>Profile + all experience text joined, for whole-CV prose scans (lowercased on demand).
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
            if (!string.IsNullOrWhiteSpace(experience.Text))
            {
                sb.AppendLine(experience.Text);
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
    /// (<see cref="NoBulletsReason"/>, A4's unparseable-period reasons, E2's PR-6b reason).
    /// Civic Swedish (§10). <paramref name="aspect"/> is the criterion's assessed aspect in
    /// definite form ("tonen"/"stavningen"/…).</para>
    /// </summary>
    public static string NoProseReason(string aspect) =>
        $"CV:t saknar profil- och erfarenhetstext, så {aspect} kan inte bedömas.";

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
