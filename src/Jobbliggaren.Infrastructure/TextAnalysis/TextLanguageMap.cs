using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// The text-analysis language a CV language is read with. Both engines share this one map, so a CV
/// language without its analysis fails instead of being read as Swedish (#2341).
/// </summary>
internal static class TextLanguageMap
{
    public static TextLanguage For(ResumeLanguage language) => language.Name switch
    {
        nameof(ResumeLanguage.Sv) => TextLanguage.Swedish,
        nameof(ResumeLanguage.En) => TextLanguage.English,
        _ => throw new InvalidOperationException($"Textanalys saknas för språk: {language.Name}."),
    };
}
