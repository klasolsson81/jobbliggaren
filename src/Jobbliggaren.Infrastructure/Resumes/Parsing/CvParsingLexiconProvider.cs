using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// <see cref="ICvParsingLexicon"/> over the embedded lexicon (Fas 4b 8b.4a). A thin, stateless
/// singleton facade on <see cref="CvParsingLexicon"/> — it holds NO data of its own, so the
/// segmenter and every recommendation consumer are provably reading the same load (parity
/// <c>RubricProvider</c>, which likewise fronts an immutable loaded contract).
/// </summary>
internal sealed class CvParsingLexiconProvider : ICvParsingLexicon
{
    public int Version => CvParsingLexicon.Version;

    public IReadOnlyCollection<string> SectionIds => CvParsingLexicon.FreeSectionIds;

    public string? TryResolveSectionId(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        // The lexicon's OWN normaliser — the same one the segmenter's heading detection runs. A
        // caller-side normalisation could drift from it, and the drift would show up as headings
        // that segment correctly but resolve to no id (a suggestion the user already satisfied).
        var normalized = CvParsingLexicon.NormalizeHeading(heading);

        return normalized.Length > 0
            && CvParsingLexicon.FreeSectionIdByHeading.TryGetValue(normalized, out var sectionId)
                ? sectionId
                : null;
    }
}
