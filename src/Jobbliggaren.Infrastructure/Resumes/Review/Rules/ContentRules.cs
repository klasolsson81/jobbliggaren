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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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
            return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Span(
                    context.RawText, bullets[0], "saknar mätbara resultat (inga siffror i erfarenheten)")));
        }

        if (quantified.Count < bullets.Count)
        {
            var offending = bullets.First(b => !ReviewText.ContainsMeasurableDigit(b));
            return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(context.RawText, offending, "punkt utan mätbart resultat")));
        }

        return CvCriterionVerdict.Assessed("A1", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Span(context.RawText, quantified[0], "kvantifierad uppgift")));
    }
}

/// <summary>A2 Action verbs (High) — bullets should open with a strong verb from the mapping.</summary>
internal sealed class A2ActionVerbsRule : ICriterionRule
{
    public string CriterionId => "A2";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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

        var ratio = (double)strong / bullets.Count;
        if (ratio >= 0.8)
        {
            var strongBullet = bullets.First(b => StartsWithStrongVerb(context, b, strongOpeners));
            return CvCriterionVerdict.Assessed("A2", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Span(context.RawText, strongBullet, "inleds med starkt handlingsverb")));
        }

        var cite = weakOpener ?? nonStrongOpener ?? bullets[0];
        var note = weakOpener is not null
            ? "inleds med ett svagt verb (se verb-mappningen)"
            : "inleds inte med ett starkt handlingsverb";
        var verdict = (weakOpener is not null || ratio < 0.5) ? CriterionVerdict.Fail : CriterionVerdict.Warn;
        return CvCriterionVerdict.Assessed("A2", category, verdict,
            ReviewText.Cite(ReviewText.Span(context.RawText, cite, note)));
    }

    private static bool StartsWithStrongVerb(
        CvReviewContext context, string bullet, IReadOnlyCollection<string> strongOpeners)
    {
        var first = FirstLexeme(context, bullet);
        return first.Length > 0 && strongOpeners.Contains(first);
    }

    private static string FirstLexeme(CvReviewContext context, string text)
    {
        var lexemes = context.Analyzer.ToLexemes(text, context.Language);
        return lexemes.Count > 0 ? lexemes[0] : string.Empty;
    }
}

/// <summary>A4 Tidsluckor (Medium) — conditional on parseable periods (V-C); gaps &gt; 6 mån.</summary>
internal sealed class A4GapsRule : ICriterionRule
{
    public string CriterionId => "A4";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var dated = context.DatedExperiences
            .Where(d => d.Parsed)
            .OrderBy(d => d.Start!.Value)
            .ToList();

        if (dated.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "A4", category, "Perioderna kunde inte tolkas till datum — tidsluckor bedöms ej v1.");
        }

        if (dated.Count == 1)
        {
            return CvCriterionVerdict.Assessed("A4", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("En daterad roll; inga tidsluckor att bedöma.")));
        }

        var gaps = new List<string>();
        for (var i = 1; i < dated.Count; i++)
        {
            var prevEnd = dated[i - 1].End!.Value;
            var nextStart = dated[i].Start!.Value;
            var months = ((nextStart.Year - prevEnd.Year) * 12) + (nextStart.Month - prevEnd.Month);
            if (months > 6)
            {
                gaps.Add($"{prevEnd:yyyy-MM} → {nextStart:yyyy-MM} ({months} mån)");
            }
        }

        return gaps.Count == 0
            ? CvCriterionVerdict.Assessed("A4", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Inga oförklarade tidsluckor > 6 mån mellan daterade roller.")))
            : CvCriterionVerdict.Assessed("A4", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural($"Tidslucka(or) > 6 mån: {string.Join("; ", gaps)}.")));
    }
}

/// <summary>A6 Konkretion vs vaghet (High) — concrete artefacts (numbers/named systems) in bullets.</summary>
internal sealed class A6ConcretionRule : ICriterionRule
{
    public string CriterionId => "A6";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var bullets = ReviewText.ExperienceBullets(context);
        if (bullets.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "A6", category, ReviewText.NoBulletsReason(context, "konkretion"));
        }

        var concrete = bullets.Where(IsConcrete).ToList();
        var ratio = (double)concrete.Count / bullets.Count;
        if (ratio >= 0.7)
        {
            return CvCriterionVerdict.Assessed("A6", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Span(context.RawText, concrete[0], "konkret artefakt (siffra/namngivet system)")));
        }

        var vague = bullets.First(b => !IsConcrete(b));
        var verdict = ratio < 0.5 ? CriterionVerdict.Fail : CriterionVerdict.Warn;
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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);
        var lower = prose.ToLowerInvariant();

        var hits = context.Cliches.Entries
            .Where(e => lower.Contains(e.Phrase.ToLowerInvariant(), StringComparison.Ordinal))
            .ToList();

        if (hits.Count == 0)
        {
            return CvCriterionVerdict.Assessed("A7", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Inga klyschor från klyscha-listan funna i profil/erfarenhet.")));
        }

        var verdict = hits.Count >= 3 ? CriterionVerdict.Fail : CriterionVerdict.Warn;
        return CvCriterionVerdict.Assessed("A7", category, verdict,
            ReviewText.Cite(ReviewText.SpanCaseInsensitive(prose, hits[0].Phrase, $"klyscha: \"{hits[0].Phrase}\"")));
    }
}

/// <summary>A8 Profil-/sammanfattningstext (Medium) — present and not overlong.</summary>
internal sealed class A8ProfileRule : ICriterionRule
{
    public string CriterionId => "A8";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var profile = context.Content.Profile;

        if (string.IsNullOrWhiteSpace(profile))
        {
            return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural("Profiltext saknas helt.")));
        }

        var words = profile.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > 100)
        {
            return CvCriterionVerdict.Assessed("A8", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Span(context.RawText, Truncate(profile), $"profiltext är lång ({words} ord)")));
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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var prose = ReviewText.AllProse(context);
        var lower = prose.ToLowerInvariant();

        // Reuse the cliché lexicon as the curated proxy for "unsupported soft phrases" (data, §5).
        var softHits = context.Cliches.Entries
            .Where(e => lower.Contains(e.Phrase.ToLowerInvariant(), StringComparison.Ordinal))
            .ToList();

        if (softHits.Count == 0)
        {
            return CvCriterionVerdict.Assessed("A9", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Inga obestyrkta personlighetsadjektiv funna.")));
        }

        // Backed if the prose carries any concrete example (a number) alongside the soft phrases.
        var verdict = ReviewText.ContainsDigit(prose) ? CriterionVerdict.Warn : CriterionVerdict.Fail;
        var note = verdict == CriterionVerdict.Fail
            ? "personlighetsadjektiv utan konkret exempel"
            : "personlighetsadjektiv – styrk med konkret exempel";
        return CvCriterionVerdict.Assessed("A9", category, verdict,
            ReviewText.Cite(ReviewText.SpanCaseInsensitive(prose, softHits[0].Phrase, note)));
    }
}

/// <summary>A10 Utbildning korrekt (High) — education entries with institution + degree.</summary>
internal sealed class A10EducationRule : ICriterionRule
{
    public string CriterionId => "A10";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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
