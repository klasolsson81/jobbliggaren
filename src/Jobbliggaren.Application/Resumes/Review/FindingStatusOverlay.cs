using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Review;

/// <summary>
/// Merges the persisted status ledger onto freshly computed verdicts (D2(e); ADR 0097 §6). A
/// Resolved decision is shown only on the finding its fingerprint names. When the criterion now
/// carries another finding, the decision is about one that no longer exists and is not shown,
/// whether a content change stamped it stale or the review changed under it at the same rubric
/// version (a language change, an engine change). A stale decision whose finding is still present
/// is shown with its staleness ("marked fixed, still present"). Ignored is shown as recorded.
/// Statuses keyed to another rubric version never carry over (D2(e) key boundary).
/// </summary>
internal static class FindingStatusOverlay
{
    internal static Dictionary<string, (string Status, DateTimeOffset? StaleAt)> Build(
        IReadOnlyList<ResumeFindingStatus> statuses,
        CvReviewResult result,
        IFindingFingerprinter fingerprinter)
    {
        var version = result.RubricVersion.ToString();
        var byCriterion = statuses
            .Where(s => s.RubricVersion == version)
            .ToDictionary(s => s.CriterionId, StringComparer.Ordinal);

        var overlay = new Dictionary<string, (string, DateTimeOffset?)>(StringComparer.Ordinal);
        foreach (var verdict in result.Verdicts)
        {
            if (!byCriterion.TryGetValue(verdict.CriterionId, out var row))
                continue;

            if (row.Status == ReviewFindingStatus.Resolved)
            {
                var currentFingerprint = fingerprinter.Compute(result.RubricVersion, verdict);
                if (!string.Equals(currentFingerprint, row.TargetFingerprint, StringComparison.Ordinal))
                    continue;
            }

            overlay[verdict.CriterionId] = (row.Status.Name, row.StaleAt);
        }

        return overlay;
    }
}
