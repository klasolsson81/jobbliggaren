using System.Text.Json;
using Jobbliggaren.Infrastructure.Resumes.Parsing;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// The ONE test-side reader of the embedded lexicon (8b.4a). The resource name, the file record and
/// the JSON options were copied into two test files; a third copy of a vocabulary's access path is
/// the same fork the port exists to prevent, one layer down (dotnet-architect, minor 8).
/// </summary>
internal static class CvParsingLexiconFixture
{
    internal const string ResourceName =
        "Jobbliggaren.Infrastructure.Resumes.Parsing.cv-parsing-lexicon.v1.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>The raw shipped file — for tests that must assert on the DATA as authored.</summary>
    internal sealed record LexiconFile(
        int Version,
        Dictionary<string, string[]>? Headings,
        string[]? NameBanners,
        Dictionary<string, string[]>? FreeSections,
        Dictionary<string, string>? DisplayForms);

    internal static LexiconFile ReadFile()
    {
        var assembly = typeof(Jobbliggaren.Infrastructure.DependencyInjection).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Det inbäddade parsing-lexikonet saknas.");

        return JsonSerializer.Deserialize<LexiconFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Lexikonet deserialiserade till null.");
    }

    /// <summary>The LOADED lexicon — what production actually runs on.</summary>
    internal static CvParsingLexiconData Load() => CvParsingLexiconLoader.Load();

    /// <summary>A segmenter over the real shipped lexicon (the DI wiring, in one line).</summary>
    internal static HeadingDrivenResumeSegmenter Segmenter() => new(Load());

    /// <summary>Every shipped (synonym, sectionId) pair, read from the asset — never a copy of it.</summary>
    internal static List<(string Synonym, string SectionId)> ShippedPairs()
    {
        var freeSections = ReadFile().FreeSections
            ?? throw new InvalidOperationException("freeSections saknas i lexikonet.");

        return freeSections
            .SelectMany(pair => pair.Value.Select(synonym => (Synonym: synonym, SectionId: pair.Key)))
            .ToList();
    }
}
