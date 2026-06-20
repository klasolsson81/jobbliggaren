namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — resolves free-text CV skill names to
/// JobTech skill-taxonomy concept-ids, so the deterministic FULL match score can
/// compute skill overlap (CV skills ∩ ad <c>extracted_terms</c> concept-ids). NO
/// AI/LLM (ADR 0071). It REUSES the one inverted skill index that already backs the
/// ad-side keyword extractor (ADR 0075) — there is no parallel resolver (Decision 6);
/// the Infrastructure impl and the extractor share a single
/// <c>SkillTaxonomyIndex</c> singleton, so ad-side extraction and CV-side resolution
/// can never silently diverge on anchoring / most-specific-wins / Snowball parity.
/// <para>
/// <b>Fail-closed on absence (ADR 0076 Decision 7, CLAUDE.md §5):</b> a free-text
/// skill the taxonomy does not carry (a niche tool, a typo, an English term) is
/// dropped silently — it is normal, not an error. The method NEVER throws on an
/// unresolvable name and returns the set of successfully-resolved concept-ids
/// (possibly empty). An empty result flows into
/// <see cref="FullCandidateMatchProfile.CvSkillConceptIds"/> → the skill/requirement
/// dimensions report <c>NotAssessed</c> (degrade to Fast), never <c>NoMatch</c>.
/// </para>
/// <para>
/// The port surface is BCL-only (<c>string</c> in, concept-ids out). The impl lives
/// in Infrastructure (the embedded taxonomy + Snowball NLP are Infrastructure
/// machinery, parity <c>IJobAdKeywordExtractor</c>); it takes no <c>ILogger</c> and
/// never logs the skill names (they are decrypted CV-PII-adjacent, CLAUDE.md §5).
/// Synchronous — pure CPU over the in-memory index, no I/O (a fake-async signature
/// would imply I/O that does not exist).
/// </para>
/// </summary>
public interface ISkillResolver
{
    /// <summary>
    /// Resolves the distinct JobTech skill concept-ids for <paramref name="freeTextSkills"/>
    /// (each a CV skill name). Unresolvable / blank entries are dropped; the result is
    /// the union of resolved concept-ids (possibly empty). Deterministic. Honours
    /// <paramref name="cancellationToken"/> between entries.
    /// </summary>
    IReadOnlySet<string> Resolve(IEnumerable<string> freeTextSkills, CancellationToken cancellationToken);
}
