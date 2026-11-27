using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// One language's embedded knowledge-bank list: the provenance source name a proposed change
/// cites, and the manifest resource that holds the list.
/// </summary>
internal sealed record KnowledgeBankAsset(string Source, string ResourceName)
{
    internal Stream Open() =>
        typeof(KnowledgeBankAsset).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded kunskapsbanks-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

    // Every ResumeLanguage needs its own list: a CV with none would be read with another language's
    // list, the defect the language key exists to prevent (#2341).
    internal static IReadOnlyDictionary<ResumeLanguage, T> LoadForEveryLanguage<T>(
        IReadOnlyDictionary<ResumeLanguage, KnowledgeBankAsset> assets,
        string kind,
        Func<KnowledgeBankAsset, T> load)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var missing = ResumeLanguage.List.Where(l => !assets.ContainsKey(l)).Select(l => l.Name).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Kunskapsbanken saknar {kind} för språk: {string.Join(", ", missing)}.");
        }

        return ResumeLanguage.List.ToDictionary(l => l, l => load(assets[l]));
    }
}
