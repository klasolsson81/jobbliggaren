namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned Swedish-ATS CV conventions (Fas 4b 8b.4b, Asset B —
/// <c>cv-conventions.v1.json</c>). Seventh ISP port over the knowledge bank (parity
/// <see cref="IRubricProvider"/>/<see cref="IClicheLexicon"/>/<see cref="IVerbMapper"/>/
/// <see cref="IFrameProvider"/>/<see cref="ISpellingAllowlist"/>/<see cref="IBranschgruppProvider"/>).
/// <para>
/// Consumed by <c>SectionReorderTransform</c> in the improvement engine — the asset and its only
/// consumer ship in the SAME step, which is what ADR 0098 demanded ("each as a cohesive
/// data+loader+consumer unit") and what its dead-machinery edict forbids splitting.
/// </para>
/// </summary>
public interface ICvConventionsProvider
{
    /// <summary>The current committed conventions (<c>cv-conventions.v1.json</c>).</summary>
    CvConventions GetConventions();
}
