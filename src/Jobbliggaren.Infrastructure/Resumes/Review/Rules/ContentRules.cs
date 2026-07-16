using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4 STEG 9 (F4-9) — Content-category (A) criterion rules. Deterministic, NO AI/LLM;
// thresholds/lists live as F4-7 knowledge-bank DATA (§5). A3 (relevance to a target ad) and
// A5 (career progression, NotAssessedV1) have no rule here — they fall through to the
// engine's honest NotAssessed.

/// <summary>A1 Mätbara resultat (Critical) — quantification in the experience bullets.</summary>
internal sealed class A1MeasurableResultsRule : ICriterionRule
{
    public string CriterionId => "A1";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var bullets = ReviewText.ExperienceBullets(context);
        if (bullets.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "A1", category, ReviewText.NoBulletsReason(context, "mätbarhet"));
        }

        // A measurable result is a digit that is NOT an employment date — the date row itself
        // is not quantification (#487). Dates are masked out of the digit test.
        var quantified = bullets.Where(ReviewText.ContainsMeasurableDigit).ToList();
        if (quantified.Count == 0)
        {
            // First Fail clause: "0 siffror i hela arbetslivserfarenheten".
            return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(
                    context.RawText, bullets[0], "saknar mätbara resultat (inga siffror i erfarenheten)")));
        }

        var missing = bullets.Count - quantified.Count;
        if (missing == 0)
        {
            return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Span(context.RawText, quantified[0], "kvantifierad uppgift")));
        }

        var offending = bullets.First(b => !ReviewText.ContainsMeasurableDigit(b));

        // #489 second Fail clause: ">50 % av punkterna saknar mätbarhet" is a CRITICAL Fail, not a
        // Warn. The ratio is rubric v1.2 DATA (thresholds.failRatio — atsFailSignal "... ELLER
        // >50 % ..."), read fail-loud; the A1 golden drift-guard pins prose↔data agreement (#489).
        if ((double)missing / bullets.Count
            > context.Criterion.RequiredThreshold(RubricThresholdKeys.FailRatio))
        {
            return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(
                    context.RawText, offending, "över hälften av punkterna saknar mätbart resultat")));
        }

        return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Warn,
            ReviewText.Cite(ReviewText.Span(context.RawText, offending, "punkt utan mätbart resultat")));
    }
}

/// <summary>A2 Action verbs (High) — bullets should open with a strong verb from the mapping.</summary>
internal sealed class A2ActionVerbsRule : ICriterionRule
{
    public string CriterionId => "A2";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var bullets = ReviewText.ExperienceBullets(context);
        if (bullets.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "A2", category, ReviewText.NoBulletsReason(context, "handlingsverb"));
        }

        // Strong-verb openers via the F4-2 NLP tier: reduce each mapping verb to its first
        // stemmed lexeme so an inflected bullet opener still matches the preteritum form
        // (analyzer owns lowercase→stopword→stem; language-aware). Weak verbs are multi-word
        // phrases that don't lexemise cleanly, so they keep a word-boundary prefix match.
        var strongOpeners = context.Verbs.StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .Select(v => FirstLexeme(context, v))
            .Where(l => l.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var weakVerbs = context.Verbs.WeakVerbs.Select(w => w.Weak).ToList();

        var strong = 0;
        string? weakOpener = null;
        string? nonStrongOpener = null;
        foreach (var bullet in bullets)
        {
            if (StartsWithStrongVerb(context, bullet, strongOpeners))
            {
                strong++;
            }
            else if (weakVerbs.Any(v => ReviewText.StartsWithWord(bullet, v)))
            {
                weakOpener ??= bullet;
            }
            else
            {
                nonStrongOpener ??= bullet;
            }
        }

        // Pass/Fail ratio bounds are rubric v1.2 DATA (thresholds.passRatio "≥80 %" /
        // thresholds.failRatio "<50 %"), read fail-loud — never a C# literal.
        var ratio = (double)strong / bullets.Count;
        if (ratio >= context.Criterion.RequiredThreshold(RubricThresholdKeys.PassRatio))
        {
            var strongBullet = bullets.First(b => StartsWithStrongVerb(context, b, strongOpeners));
            return CvCriterionVerdict.Assessed("A2", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Span(context.RawText, strongBullet, "inleds med starkt handlingsverb")));
        }

        var cite = weakOpener ?? nonStrongOpener ?? bullets[0];
        var note = weakOpener is not null
            ? "inleds med ett svagt verb (se verb-mappningen)"
            : "inleds inte med ett starkt handlingsverb";
        var verdict = (weakOpener is not null
            || ratio < context.Criterion.RequiredThreshold(RubricThresholdKeys.FailRatio))
            ? CriterionVerdict.Fail : CriterionVerdict.Warn;
        return CvCriterionVerdict.Assessed("A2", category, verdict,
            ReviewText.Cite(ReviewText.Span(context.RawText, cite, note)));
    }

    private static bool StartsWithStrongVerb(
        CriterionEvaluationContext context, string bullet, IReadOnlyCollection<string> strongOpeners)
    {
        var first = FirstLexeme(context, bullet);
        return first.Length > 0 && strongOpeners.Contains(first);
    }

    private static string FirstLexeme(CriterionEvaluationContext context, string text)
    {
        var lexemes = context.Analyzer.ToLexemes(text, context.Language);
        return lexemes.Count > 0 ? lexemes[0] : string.Empty;
    }
}

/// <summary>A4 Tidsluckor (Medium) — conditional on parseable periods (V-C); gaps &gt; 6 mån.</summary>
internal sealed class A4GapsRule : ICriterionRule
{
    public string CriterionId => "A4";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var dated = context.DatedExperiences
            .Where(d => d.Parsed)
            .OrderBy(d => d.Start!.Value)
            .ToList();

        if (dated.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "A4", category, "Perioderna kunde inte tolkas till datum. Tidsluckor bedöms ej v1.");
        }

        if (dated.Count == 1)
        {
            return CvCriterionVerdict.Assessed("A4", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("En daterad roll; inga tidsluckor att bedöma.")));
        }

        // The gap bound is rubric v1.2 DATA (thresholds.maxGapMonths, atsFailSignal "> 6 mån"),
        // read fail-loud — never a C# literal. Also drives the cited copy below.
        var maxGapMonths = context.Criterion.RequiredThreshold(RubricThresholdKeys.MaxGapMonths);

        var gaps = new List<string>();
        var maxEnd = dated[0].End!.Value;
        for (var i = 1; i < dated.Count; i++)
        {
            var nextStart = dated[i].Start!.Value;

            // Measure the gap from the FURTHEST coverage so far (running max end), NOT the
            // immediately-previous role by start-order — an overlapping/parallel role that ends
            // earlier must not fabricate a gap once a longer role already covers the span (#493).
            var months = ((nextStart.Year - maxEnd.Year) * 12) + (nextStart.Month - maxEnd.Month);
            if (months > maxGapMonths)
            {
                gaps.Add($"{maxEnd:yyyy-MM} → {nextStart:yyyy-MM} ({months} mån)");
            }

            if (dated[i].End!.Value > maxEnd)
            {
                maxEnd = dated[i].End!.Value;
            }
        }

        // #493 part 2: an unparseable period was silently dropped from `dated`, so an apparent gap
        // between two dated roles could actually be filled by the undated role. With incomplete date
        // coverage A4 must not fabricate a Warn — report NotAssessed honestly (§5, parity with the
        // all-unparseable NotAssessed above). A gap-free timeline still passes: an undated role can
        // only add coverage, it cannot open a gap between roles that are already contiguous.
        if (gaps.Count > 0 && context.DatedExperiences.Any(d => !d.Parsed))
        {
            return CvCriterionVerdict.NotAssessed(
                "A4", category,
                "Någon period kunde inte tolkas till datum, så tidsluckor kan inte bedömas säkert.");
        }

        return gaps.Count == 0
            ? CvCriterionVerdict.Assessed("A4", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural(
                    $"Inga oförklarade tidsluckor > {maxGapMonths:0} mån mellan daterade roller.")))
            : CvCriterionVerdict.Assessed("A4", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    $"Tidslucka(or) > {maxGapMonths:0} mån: {string.Join("; ", gaps)}.")));
    }
}

/// <summary>A6 Konkretion vs vaghet (High) — concrete artefacts (numbers/named systems) in bullets.</summary>
internal sealed class A6ConcretionRule : ICriterionRule
{
    public string CriterionId => "A6";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var bullets = ReviewText.ExperienceBullets(context);
        if (bullets.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "A6", category, ReviewText.NoBulletsReason(context, "konkretion"));
        }

        // Pass/Fail ratio bounds are rubric v1.2 DATA (thresholds.passRatio "≥70 %" /
        // thresholds.failRatio ">50 % generiska"), read fail-loud — never a C# literal.
        var concrete = bullets.Where(IsConcrete).ToList();
        var ratio = (double)concrete.Count / bullets.Count;
        if (ratio >= context.Criterion.RequiredThreshold(RubricThresholdKeys.PassRatio))
        {
            return CvCriterionVerdict.Assessed("A6", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Span(context.RawText, concrete[0], "konkret artefakt (siffra/namngivet system)")));
        }

        var vague = bullets.First(b => !IsConcrete(b));
        var verdict = ratio < context.Criterion.RequiredThreshold(RubricThresholdKeys.FailRatio)
            ? CriterionVerdict.Fail : CriterionVerdict.Warn;
        return CvCriterionVerdict.Assessed("A6", category, verdict,
            ReviewText.Cite(ReviewText.Span(context.RawText, vague, "generisk punkt utan konkret artefakt")));
    }

    // Concrete = contains a measurable digit (dates masked — #487) OR a capitalised word past
    // the first (proxy for a named tool/system/company/customer). Deterministic, no curated list.
    private static bool IsConcrete(string bullet)
    {
        if (ReviewText.ContainsMeasurableDigit(bullet))
        {
            return true;
        }

        var words = bullet.Split([' ', ',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 1; i < words.Length; i++)
        {
            if (words[i].Length > 1 && char.IsUpper(words[i][0]))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A7 Anti-klyschor (Medium) — cliché phrases from the F4-7 cliché lexicon.</summary>
internal sealed class A7ClicheRule : ICriterionRule
{
    public string CriterionId => "A7";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        // CV-pivot 2026-07-16: an empty corpus is not a cliché-free corpus (see NoProseReason).
        if (string.IsNullOrWhiteSpace(prose))
        {
            return CvCriterionVerdict.NotAssessed("A7", category, ReviewText.NoProseReason("klyschorna"));
        }

        // A7 owns only the kind==Cliche entries (empty buzzword phrases); the soft-skill
        // adjectives are A9's domain, so one phrase never draws two verdicts (#490). Matched on a
        // WORD BOUNDARY over the raw prose, so "Social" hits "social kompetens" but never
        // "sociala"/"socialsekreterare" and "Flexibel" never "flexibelt" (#490/#496).
        var hits = context.Cliches.Entries
            .Where(e => e.Kind == ClicheKind.Cliche && ReviewText.ContainsWord(prose, e.Phrase))
            .ToList();

        // Thresholds are rubric v1.2 DATA (thresholds.passBelowCount "<2 förekomster" /
        // thresholds.failFromCount "≥3 klyschor"), read fail-loud; the band between them is
        // Warn. Prose↔data agreement pinned by the A7 golden drift-guard (#489).
        if (hits.Count == 0)
        {
            return CvCriterionVerdict.Assessed("A7", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Inga klyschor från klyscha-listan funna i profil/erfarenhet.")));
        }

        var passBelow = context.Criterion.RequiredThreshold(RubricThresholdKeys.PassBelowCount);
        if (hits.Count < passBelow)
        {
            // A single cliché is under the rubric's Pass threshold — cite it so the pass is
            // transparent (§5 explainability), never a hidden flag.
            return CvCriterionVerdict.Assessed("A7", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.SpanWord(prose, hits[0].Phrase,
                    $"enstaka klyscha under gränsen (<{passBelow:0}): \"{hits[0].Phrase}\"")));
        }

        var verdict = hits.Count >= context.Criterion.RequiredThreshold(RubricThresholdKeys.FailFromCount)
            ? CriterionVerdict.Fail : CriterionVerdict.Warn;
        return CvCriterionVerdict.Assessed("A7", category, verdict,
            ReviewText.Cite(ReviewText.SpanWord(prose, hits[0].Phrase, $"klyscha: \"{hits[0].Phrase}\"")));
    }
}

/// <summary>A8 Profil-/sammanfattningstext (Medium) — present, not overlong, not a bare list.</summary>
internal sealed class A8ProfileRule : ICriterionRule
{
    public string CriterionId => "A8";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var profile = context.Content.Profile;

        if (string.IsNullOrWhiteSpace(profile))
        {
            // #844: this Fail used to be UNCONDITIONAL, and it was a lie for every CV that opened
            // with a summary the author never gave a heading. That prose was dropped from the parse
            // artifact entirely, so A8 — which reads the STRUCTURED content, never RawText — told
            // its author, as a hard Fail with structural evidence, that her summary "saknas helt".
            //
            // When the segmenter carried unclassified text down from above the first heading, the
            // absence is NOT observed and the claim cannot be grounded. Withdraw it. We do NOT
            // upgrade to Pass or Warn: either would classify the residue (Pass says it IS a profile;
            // Warn grades her on a missing HEADING, which is B1/D6's subject, not A8's) and the
            // engine never decides what un-headed text is — the user does, in the guide (ADR 0074).
            // Reduced precision is marked "not assessed", never mis-reported (CLAUDE.md §5).
            //
            // Structural reason only: no quote, no CV text. The preamble is the most personnummer-
            // dense region of a CV, and a verdict's reason string is not a PII channel.
            if (!string.IsNullOrWhiteSpace(context.Content.Preamble))
            {
                return CvCriterionVerdict.NotAssessed("A8", category,
                    context.Criterion.NotAssessedReason
                    ?? "Det går inte att avgöra om profiltext saknas.");
            }

            // The preamble was fully accounted for (name, e-mail, phone, ort) or empty — the absence
            // IS observed, and the Fail is earned. This is the arm that must survive: withdrawing it
            // too would delete a working signal, which is a regression dressed as honesty.
            return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural("Profiltext saknas helt.")));
        }

        // The length Fail bound is rubric v1.2 DATA (thresholds.maxWords, atsFailSignal
        // "... ELLER >100 ord ..."), read fail-loud; prose↔data agreement pinned by the A8
        // golden drift-guard (#489 — ">100 ord" is a rubric Fail, not a Warn).
        var maxWords = context.Criterion.RequiredThreshold(RubricThresholdKeys.MaxWords);
        var words = profile.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > maxWords)
        {
            return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(
                    context.RawText, Truncate(profile), $"profiltext är för lång ({words} ord, gränsen är {maxWords:0})")));
        }

        // #489: the rubric's "Objective: To obtain..."-USA-style clause. A Swedish CV profile opening
        // with the English "Objective" heading is the objective-statement anti-pattern the rubric
        // fails (deterministic prefix, never a Swedish false positive; agent review completeness).
        if (profile.TrimStart().StartsWith("Objective", StringComparison.OrdinalIgnoreCase))
        {
            return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(
                    context.RawText, Truncate(profile), "profiltext i \"Objective\"-USA-stil, skriv en kort svensk sammanfattning")));
        }

        // #489: "ren adjektivlista" is a rubric Fail. Detect a bare list DOMINATED by curated
        // soft-skill adjectives (kind==SoftSkill — versioned data, parity A9) with no concrete
        // example: at least two of them AND at least half the words are such adjectives AND no
        // measurable digit (dates masked, #487). The "half the words" dominance is a STRUCTURAL
        // constant (a list, not a sentence), NOT a rubric numeral — so it carries no drift-guard
        // (nothing in the rubric prose to derive it from), unlike MaxWords. v1 limitation (honest,
        // no over-claim): only the curated soft-skill adjectives are recognised — a list of
        // uncurated adjectives is not flagged (deterministic, ADR 0071; a general adjective detector
        // needs POS tagging, v2).
        var softAdjectives = context.Cliches.Entries
            .Count(e => e.Kind == ClicheKind.SoftSkill && ReviewText.ContainsWord(profile, e.Phrase));
        if (softAdjectives >= 2 && softAdjectives * 2 >= words && !ReviewText.ContainsMeasurableDigit(profile))
        {
            return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(
                    context.RawText, Truncate(profile), "profiltext är en ren adjektivlista utan konkret innehåll")));
        }

        return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Span(context.RawText, Truncate(profile), "profiltext finns i rimlig längd")));
    }

    private static string Truncate(string text) => text.Length <= 80 ? text : text[..80];
}

/// <summary>A9 Soft skills underbyggda (Low) — soft phrases backed by a concrete example.</summary>
internal sealed class A9SoftSkillsRule : ICriterionRule
{
    public string CriterionId => "A9";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);

        // CV-pivot 2026-07-16: an empty corpus backs no soft-skill claim either way (NoProseReason).
        if (string.IsNullOrWhiteSpace(prose))
        {
            return CvCriterionVerdict.NotAssessed(
                "A9", category, ReviewText.NoProseReason("personlighetsadjektiven"));
        }

        // A9's curated sub-list is the kind==SoftSkill entries only (bare personality adjectives) —
        // a cliché-kind phrase belongs to A7, so one phrase never draws two verdicts (#490). Matched
        // on a word boundary so "Social" never hits "sociala"/"socialsekreterare" (#490/#496).
        var softHits = context.Cliches.Entries
            .Where(e => e.Kind == ClicheKind.SoftSkill && ReviewText.ContainsWord(prose, e.Phrase))
            .ToList();

        if (softHits.Count == 0)
        {
            return CvCriterionVerdict.Assessed("A9", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Inga obestyrkta personlighetsadjektiv funna.")));
        }

        // "Backed" now means a MEASURABLE example sits in the SAME sentence as the adjective (#490),
        // not merely any digit somewhere in the CV — an employment date always satisfied the old
        // any-digit check, so the Fail branch was effectively dead. Dates are masked (#487).
        var unsupported = softHits
            .Where(e => !ReviewText.HasMeasurableExampleNear(prose, e.Phrase))
            .ToList();

        if (unsupported.Count == 0)
        {
            return CvCriterionVerdict.Assessed("A9", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural(
                    "Personlighetsadjektiv styrks med konkret exempel i samma mening.")));
        }

        // An unsupported adjective LIST is the rubric's Fail case ("adjektivlista utan
        // exempel"); a single unbacked adjective is a Warn (advise a concrete example). The
        // list floor is rubric v1.2 DATA (thresholds.failFromCount), read fail-loud.
        var verdict = unsupported.Count
            >= context.Criterion.RequiredThreshold(RubricThresholdKeys.FailFromCount)
            ? CriterionVerdict.Fail : CriterionVerdict.Warn;
        var note = verdict == CriterionVerdict.Fail
            ? "personlighetsadjektiv utan konkret exempel"
            : "personlighetsadjektiv – styrk med konkret exempel";
        return CvCriterionVerdict.Assessed("A9", category, verdict,
            ReviewText.Cite(ReviewText.SpanWord(prose, unsupported[0].Phrase, note)));
    }
}

/// <summary>A10 Utbildning korrekt (High) — education entries with institution + degree.</summary>
internal sealed class A10EducationRule : ICriterionRule
{
    public string CriterionId => "A10";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var education = context.Content.Education;

        if (education.Count == 0)
        {
            return CvCriterionVerdict.Assessed("A10", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural("Ingen utbildning angiven.")));
        }

        var incomplete = education
            .Count(e => string.IsNullOrWhiteSpace(e.Institution) || string.IsNullOrWhiteSpace(e.Degree));

        return incomplete == 0
            ? CvCriterionVerdict.Assessed("A10", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural($"{education.Count} utbildningspost(er) med lärosäte och examen.")))
            : CvCriterionVerdict.Assessed("A10", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    $"{incomplete} av {education.Count} utbildningsposter saknar lärosäte/examen.")));
    }
}
