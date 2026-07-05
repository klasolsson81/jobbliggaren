using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Improvement.FrameApply;

/// <summary>
/// One review finding resolved against live canonical content (Fas 4b PR-7, #656):
/// the Fail/Warn <see cref="Verdict"/>, the single content <see cref="Line"/> its cited
/// quote lives in (the frame rewrite's Before), and the SERVER-computed
/// <see cref="Fingerprint"/> for the finding as it stands right now.
/// </summary>
public sealed record LocatedFinding(CvCriterionVerdict Verdict, string Line, string Fingerprint);

/// <summary>
/// The pure, side-effect-free compose core the preview query and the apply command SHARE
/// (Fas 4b PR-7, #656; CTO D-G — one compose kernel, two pipelines). No I/O, no clock, no
/// DbContext: the callers recompute the review server-side (ADR 0074 — client-submitted
/// text is forbidden) and hand the result here.
/// <para><see cref="ResolveFinding"/> proves a finding is actionable (Fail/Warn with a
/// text span), unchanged (the client's echoed fingerprint equals the freshly computed one
/// — ADR 0093 §D2's optimistic guard, surfaced as Conflict/409 "CV changed, re-review"),
/// and locatable (exactly which content line carries the cited quote — deterministic
/// search order: Summary lines, then experience descriptions in order, then dynamic
/// section entry lines; the prose surfaces A1/A2/C3 cite). <see cref="ApplyToContent"/>
/// swaps exactly that line, returning a NEW immutable <see cref="ResumeContent"/>.</para>
/// </summary>
public static class FrameApplyComposer
{
    /// <summary>
    /// The invariant-b membership closure BOTH handlers consume (code review Minor 2 —
    /// one builder, one version guard): every strong verb across the mapping's groups,
    /// case-folded. The catalog's verbMappingVersion is loader-pinned to the mapping's
    /// version in production (PR-5 equality pin); re-checking here means a wiring drift
    /// fails loud on BOTH surfaces — so "a preview that succeeds is an apply that will
    /// succeed" holds for the verb invariant too, instead of preview passing and apply
    /// throwing.
    /// </summary>
    public static HashSet<string> BuildStrongVerbSet(VerbMapping mapping, FrameCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(catalog);

        if (!string.Equals(mapping.Version, catalog.VerbMappingVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Frame catalog pins verb-mapping v{catalog.VerbMappingVersion} but the loaded " +
                $"mapping is v{mapping.Version} — the ADR 0093 §D2 verb invariant is version-bound.");
        }

        return mapping.StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The apply-side resolve: verifies the client's echoed <paramref name="clientFingerprint"/>
    /// against the freshly computed one BEFORE locating (a stale finding must 409 rather than
    /// rewrite a moved target — ADR 0093 §D2).
    /// </summary>
    public static Result<LocatedFinding> ResolveFinding(
        CvReviewResult review, string criterionId, string clientFingerprint, ResumeContent content)
    {
        var resolved = ResolveFinding(review, criterionId, content);
        if (resolved.IsFailure)
        {
            return resolved;
        }

        if (!string.Equals(resolved.Value.Fingerprint, clientFingerprint, StringComparison.Ordinal))
        {
            return Result.Failure<LocatedFinding>(DomainError.Conflict(
                "Resume.FindingChanged",
                "CV-innehållet har ändrats sedan granskningen. Granska på nytt och försök igen."));
        }

        return resolved;
    }

    /// <summary>
    /// The preview-side resolve: MINTS the server fingerprint the client later echoes to
    /// apply (the preview is the honest source of that token — never client-derived,
    /// ADR 0074 Invariant 2).
    /// </summary>
    public static Result<LocatedFinding> ResolveFinding(
        CvReviewResult review, string criterionId, ResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(content);

        var verdict = review.Verdicts.FirstOrDefault(v =>
            string.Equals(v.CriterionId, criterionId, StringComparison.Ordinal));
        if (verdict is null)
        {
            return Result.Failure<LocatedFinding>(
                DomainError.NotFound("RubricCriterion", criterionId));
        }

        // Only a Fail/Warn with a cited text span is frame-rewritable — a Pass has nothing
        // to fix, and a structural-only verdict (e.g. an absence finding) has no line.
        if (verdict.Verdict is not (CriterionVerdict.Fail or CriterionVerdict.Warn))
        {
            return NotActionable();
        }

        var quote = verdict.Evidence
            .OfType<TextSpanEvidence>()
            .Select(e => e.Span.Quote)
            .FirstOrDefault(q => !string.IsNullOrWhiteSpace(q));
        if (quote is null)
        {
            return NotActionable();
        }

        var line = ContentLines(content)
            .FirstOrDefault(l => l.Contains(quote, StringComparison.Ordinal));
        if (line is null)
        {
            return Result.Failure<LocatedFinding>(DomainError.Conflict(
                "Resume.FindingLineNotFound",
                "Den citerade raden finns inte längre i CV:t. Granska på nytt och försök igen."));
        }

        var fingerprint = FindingTargetFingerprint.Compute(review.RubricVersion, verdict);
        return Result.Success(new LocatedFinding(verdict, line, fingerprint));

        static Result<LocatedFinding> NotActionable() =>
            Result.Failure<LocatedFinding>(DomainError.Validation(
                "Resume.FindingNotActionable",
                "Kriteriet har ingen citerad rad att åtgärda i den aktuella granskningen."));
    }

    /// <summary>
    /// Replaces exactly one content line (first whole-line match in the deterministic
    /// search order) with <paramref name="afterLine"/>, returning a NEW content. The line
    /// vanishing between resolve and apply (e.g. an earlier change in the same batch
    /// consumed it) is a Conflict, never a silent no-op.
    /// </summary>
    public static Result<ResumeContent> ApplyToContent(
        ResumeContent content, string beforeLine, string afterLine)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterLine);

        // Search order parity with ContentLines: Summary first, then experiences, then
        // dynamic sections. Exactly ONE line is replaced — the first match wins.
        if (content.Summary is { } summary
            && ReplaceLine(summary, beforeLine, afterLine) is { } newSummary)
        {
            return Result.Success(content with { Summary = newSummary });
        }

        for (var i = 0; i < content.Experiences.Count; i++)
        {
            var experience = content.Experiences[i];
            if (experience.Description is { } description
                && ReplaceLine(description, beforeLine, afterLine) is { } newDescription)
            {
                var experiences = content.Experiences.ToList();
                experiences[i] = experience with { Description = newDescription };
                return Result.Success(content with { Experiences = experiences });
            }
        }

        for (var i = 0; i < content.Sections.Count; i++)
        {
            var section = content.Sections[i];
            for (var j = 0; j < section.Entries.Count; j++)
            {
                var entry = section.Entries[j];
                var lineIndex = IndexOfLine(entry.Lines, beforeLine);
                if (lineIndex < 0)
                {
                    continue;
                }

                var lines = entry.Lines.ToList();
                lines[lineIndex] = afterLine;
                var entries = section.Entries.ToList();
                entries[j] = entry with { Lines = lines };
                var sections = content.Sections.ToList();
                sections[i] = section with { Entries = entries };
                return Result.Success(content with { Sections = sections });
            }
        }

        return Result.Failure<ResumeContent>(DomainError.Conflict(
            "Resume.FindingLineNotFound",
            "Den citerade raden finns inte längre i CV:t. Granska på nytt och försök igen."));
    }

    // Every prose line a frame can rewrite, in the deterministic search order. Trimmed —
    // the same unit ReviewText.DescriptionLines scores (the review cites trimmed bullets).
    private static IEnumerable<string> ContentLines(ResumeContent content)
    {
        if (content.Summary is { } summary)
        {
            foreach (var line in Lines(summary))
            {
                yield return line;
            }
        }

        foreach (var experience in content.Experiences)
        {
            if (experience.Description is { } description)
            {
                foreach (var line in Lines(description))
                {
                    yield return line;
                }
            }
        }

        foreach (var section in content.Sections)
        {
            foreach (var entry in section.Entries)
            {
                foreach (var line in entry.Lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    yield return line.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

    // Replaces the first line of `text` whose trimmed form equals `beforeLine` (trimmed);
    // null when no line matches (the caller moves on to the next field).
    private static string? ReplaceLine(string text, string beforeLine, string afterLine)
    {
        var needle = beforeLine.Trim();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), needle, StringComparison.Ordinal))
            {
                lines[i] = afterLine;
                return string.Join('\n', lines);
            }
        }

        return null;
    }

    private static int IndexOfLine(IReadOnlyList<string> lines, string beforeLine)
    {
        var needle = beforeLine.Trim();
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Trim(), needle, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
