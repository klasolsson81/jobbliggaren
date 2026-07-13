using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// <see cref="ICvParsingLexicon"/> over the loaded lexicon (Fas 4b 8b.4a). Stateless singleton: it
/// holds the SAME <see cref="CvParsingLexiconData"/> instance the segmenter holds (one DI-registered
/// value, loaded once at host build), so RECOGNITION and section-id RESOLUTION cannot disagree.
/// </summary>
internal sealed class CvParsingLexiconProvider(CvParsingLexiconData lexicon) : ICvParsingLexicon
{
    private readonly CvParsingLexiconData _lexicon =
        lexicon ?? throw new ArgumentNullException(nameof(lexicon));

    public IReadOnlySet<string> FreeSectionIds => _lexicon.FreeSectionIds;

    public string? TryResolveFreeSectionId(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        // The lexicon's OWN normaliser — the same one the segmenter's heading detection runs. A
        // caller-side normalisation could drift from it, and the drift would show up as headings that
        // segment correctly but resolve to no id (a suggestion the user has already satisfied).
        var normalized = CvParsingLexiconLoader.NormalizeHeading(heading);

        return normalized.Length > 0
            && _lexicon.FreeSectionIdByHeading.TryGetValue(normalized, out var sectionId)
                ? sectionId
                : null;
    }
}
