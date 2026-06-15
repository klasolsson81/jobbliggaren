namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned weak→strong verb mapping (F4-7, research §6.3).
/// One of the three ISP ports over the knowledge bank (senior-cto-advisor DQ5=B) —
/// consumed by F4-9 (A2/C3 weak-verb detection) and F4-10 (propose-and-approve verb
/// upgrades).
/// </summary>
public interface IVerbMapper
{
    /// <summary>The current committed verb mapping (<c>verb-mapping.v1.json</c>).</summary>
    VerbMapping GetVerbMapping();
}
