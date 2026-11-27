using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned cliché lexicon (F4-7, research §6.1). One of the
/// three ISP ports over the knowledge bank (senior-cto-advisor DQ5=B) — consumed by
/// F4-9 (A7 anti-cliché) and F4-10 (rewrite suggestions).
/// </summary>
public interface IClicheLexicon
{
    /// <summary>The committed cliché list for a CV written in <paramref name="language"/>
    /// (<c>cliche-list.sv.v2.json</c>, <c>cliche-list.en.v1.json</c>). Every
    /// <see cref="ResumeLanguage"/> has one.</summary>
    ClicheList GetClicheList(ResumeLanguage language);
}
