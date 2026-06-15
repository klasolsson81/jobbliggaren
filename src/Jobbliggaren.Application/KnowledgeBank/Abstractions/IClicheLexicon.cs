namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned cliché lexicon (F4-7, research §6.1). One of the
/// three ISP ports over the knowledge bank (senior-cto-advisor DQ5=B) — consumed by
/// F4-9 (A7 anti-cliché) and F4-10 (rewrite suggestions).
/// </summary>
public interface IClicheLexicon
{
    /// <summary>The current committed cliché list (<c>cliche-list.v1.json</c>).</summary>
    ClicheList GetClicheList();
}
