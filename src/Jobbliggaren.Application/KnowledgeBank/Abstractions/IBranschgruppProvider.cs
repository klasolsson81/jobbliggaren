namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned occupation-field → branschgrupp map + section rule-table
/// (Fas 4b 8b.4a, Asset A — <c>ssyk-branschgrupp.v1.json</c>). Sixth ISP port over the knowledge
/// bank (parity <see cref="IRubricProvider"/>/<see cref="IClicheLexicon"/>/<see cref="IVerbMapper"/>/
/// <see cref="IFrameProvider"/>/<see cref="ISpellingAllowlist"/>).
/// <para>
/// Consumed by the <c>GetCvSectionSuggestions</c> read-slice — NOT by the improvement engine. A
/// section suggestion is not a <c>ProposedChange</c>: it has no Before, no After and no transform,
/// so it is not a diff (senior-cto-advisor bind Q1-B, 2026-07-13; ADR 0107).
/// </para>
/// </summary>
public interface IBranschgruppProvider
{
    /// <summary>The current committed branschgrupp catalog (<c>ssyk-branschgrupp.v1.json</c>).</summary>
    BranschgruppCatalog GetCatalog();
}
