using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned weak→strong verb mapping (F4-7, research §6.3).
/// One of the three ISP ports over the knowledge bank (senior-cto-advisor DQ5=B) —
/// consumed by F4-9 (A2/C3 weak-verb detection) and F4-10 (propose-and-approve verb
/// upgrades).
/// </summary>
public interface IVerbMapper
{
    /// <summary>The committed verb mapping for a CV written in <paramref name="language"/>
    /// (<c>verb-mapping.sv.v1.json</c>, <c>verb-mapping.en.v1.json</c>). Every
    /// <see cref="ResumeLanguage"/> has one.</summary>
    VerbMapping GetVerbMapping(ResumeLanguage language);
}
