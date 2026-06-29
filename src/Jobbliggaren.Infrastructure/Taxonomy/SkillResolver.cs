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

    // ADR 0079 STEG 3 — labelled resolution for CV-seeded skill chips. Unions across CV
    // skills, dedupes per concept-id (first preferred label wins — MatchForms already
    // keeps one form per concept-id, and the same concept-id resolves to the same label
    // regardless of which CV skill hit it), and returns a deterministic ordinal order so
    // the proposal jsonb + chips are reproducible.
    public IReadOnlyList<ResolvedSkill> ResolveDetailed(
        IEnumerable<string> freeTextSkills, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(freeTextSkills);

        var byConcept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skill in freeTextSkills)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(skill))
                continue;
            foreach (var form in index.ResolveForms(skill))
                byConcept.TryAdd(form.ConceptId, form.PreferredLabel);
        }

        return byConcept
            .Select(kv => new ResolvedSkill(kv.Key, kv.Value))
            .OrderBy(r => r.ConceptId, StringComparer.Ordinal)
            .ToList();
    }

    // ADR 0079 STEG 3 PR-C — skill typeahead. Thin adapter over the shared index's
    // substring search; caps results so the picker stays bounded. Synchronous CPU over
    // the in-memory index (ct honoured at the call boundary only — a single capped scan).
    private const int MaxSearchResults = 20;

    public IReadOnlyList<ResolvedSkill> Search(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return index
            .Search(query, MaxSearchResults)
            .Select(hit => new ResolvedSkill(hit.ConceptId, hit.Label))
            .ToList();
    }

    // ADR 0079 STEG 3 PR-C — reverse-lookup for saved skill chips. Thin adapter over the
    // shared index's concept-id → label map; unknown ids drop silently.
    public IReadOnlyList<ResolvedSkill> ResolveLabels(
        IEnumerable<string> conceptIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return index
            .ResolveLabels(conceptIds)
            .Select(hit => new ResolvedSkill(hit.ConceptId, hit.Label))
            .ToList();
    }

    // #277 — group concept-ids by shared exact-label surface (ESCO + AF twins → one chip). Thin
    // adapter over the shared index's GroupConceptIds; the index guards the no-drop/partition
    // invariant. Synchronous CPU over the in-memory index (ct honoured at the call boundary).
    public IReadOnlyList<ResolvedSkillGroup> GroupConceptIds(
        IEnumerable<string> conceptIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conceptIds);
        cancellationToken.ThrowIfCancellationRequested();
        return index
            .GroupConceptIds(conceptIds)
            .Select(g => new ResolvedSkillGroup(g.CanonicalConceptId, g.CanonicalLabel, g.MemberConceptIds))
            .ToList();
    }
}
