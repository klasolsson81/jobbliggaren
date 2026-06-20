using Jobbliggaren.Application.Matching.Abstractions;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the CV-side <see cref="ISkillResolver"/>.
/// A thin adapter over the shared <see cref="SkillTaxonomyIndex"/> (the SAME singleton
/// the ad-side <see cref="JobAdKeywordExtractor"/> uses) — there is no parallel index
/// (Decision 6). Resolves EACH skill name independently (a CV's <c>Skills</c> are
/// discrete claimed competencies, unlike an ad's free prose) and unions the resolved
/// concept-ids, so two unrelated CV skills can never combine into a spurious
/// multi-word concept match. NO AI/LLM; never logs the names (CLAUDE.md §5).
/// </summary>
internal sealed class SkillResolver(SkillTaxonomyIndex index) : ISkillResolver
{
    public IReadOnlySet<string> Resolve(
        IEnumerable<string> freeTextSkills, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(freeTextSkills);

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in freeTextSkills)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(skill))
                continue;
            foreach (var conceptId in index.ResolveConceptIds(skill))
                resolved.Add(conceptId);
        }

        return resolved;
    }
}
