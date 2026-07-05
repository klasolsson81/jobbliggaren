namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned sentence/measure-frame catalog (Fas 4b PR-5,
/// ADR 0093 §D3 — frames built FIRST as the hard PR-7 dependency). Fourth ISP port
/// over the knowledge bank (parity <see cref="IRubricProvider"/>/<see cref="IClicheLexicon"/>/
/// <see cref="IVerbMapper"/>) — consumed by the PR-7 apply-half (<c>FromFrame</c>),
/// which enforces the §D2 provenance invariants; PR-5 ships data + structural
/// validation only.
/// </summary>
public interface IFrameProvider
{
    /// <summary>The current committed frame catalog (<c>frames.v1.json</c>).</summary>
    FrameCatalog GetFrameCatalog();
}
