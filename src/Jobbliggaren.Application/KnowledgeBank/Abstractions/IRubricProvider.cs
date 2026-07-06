namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned CV-quality <see cref="Rubric"/> (F4-7). One of
/// three ISP ports over the knowledge bank (senior-cto-advisor DQ5=B) — the review
/// engine (F4-9) takes this one. Infrastructure loads the embedded asset once and
/// caches the mapped contract (singleton).
/// </summary>
public interface IRubricProvider
{
    /// <summary>The current committed rubric (<c>rubric.v2.0.0.json</c>).</summary>
    Rubric GetRubric();
}
