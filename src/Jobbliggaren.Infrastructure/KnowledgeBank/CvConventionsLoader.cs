using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Loads the committed, versioned Swedish-ATS conventions (<c>cv-conventions.v1.json</c>, Fas 4b
/// 8b.4b Asset B) embedded in this assembly. Deserialises the <see cref="CvConventionsFile"/> DTO
/// and maps it to the immutable <see cref="CvConventions"/> contract — no <c>*File</c> type and no
/// <c>JsonPropertyName</c> leaks past Infrastructure (CLAUDE.md §2.1, parity
/// <see cref="RubricLoader"/>/<see cref="BranschgruppLoader"/>).
/// <para>
/// SHAPE validation lives here, including the TYPED half of section identity (resolved through the
/// lexicon loader's own <c>TryMapTypedSectionId</c> — one home, never a second switch). The
/// FREE half — an id must be one the parsing lexicon owns — is the CROSS-ASSET validation and
/// lives in <see cref="CvConventionsProvider"/>, which ctor-injects the source of truth, mirroring
/// <c>BranschgruppProvider(ICvParsingLexicon)</c> and <c>FrameProvider(IVerbMapper)</c>. That is
/// the repo's no-fork mechanism: reference the owner of the vocabulary, never restate it.
/// </para>
/// <para>
/// Both run at HOST BUILD, not mid-request — but only because <c>AddCvLexicon()</c> constructs
/// the provider eagerly (an INSTANCE registration). See <see cref="CvConventionsProvider"/>.
/// </para>
/// </summary>
internal static class CvConventionsLoader
{
    // The SAME LogicalName the csproj declares for the embedded resource.
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.cv-conventions.v1.json";

    /// <summary>Loads the committed conventions asset from the embedded resource.</summary>
    internal static CvConventions Load()
    {
        var asm = typeof(CvConventionsLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded cv-conventions-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        return LoadFrom(stream);
    }

    /// <summary>
    /// Deserialises + maps + validates from an arbitrary stream. The test seam that drives
    /// synthetic malformed assets through the REAL path (parity <see cref="RubricLoader.LoadFrom"/>).
    /// </summary>
    internal static CvConventions LoadFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<CvConventionsFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("cv-conventions-assetet deserialiserade till null.");

        return MapToContract(file);
    }

    private static CvConventions MapToContract(CvConventionsFile file)
    {
        if (string.IsNullOrWhiteSpace(file.ConventionsVersion))
            throw new InvalidOperationException("cv-conventions-assetet saknar conventionsVersion.");

        // An empty order is not "no opinion" — it is a transform that can never fire, dressed as
        // data. The whole asset exists to carry this list.
        if (file.SectionOrder.Count == 0)
            throw new InvalidOperationException(
                "cv-conventions-assetet saknar sectionOrder. En tom ordning gör "
                + "SectionReorderTransform permanent inert — assetets enda syfte är den listan.");

        // fontAllowlist (Fas 4b #891, ADR 0108): the D3 body-font recommendation. An empty list is
        // not "no opinion" — it is a D3 rule that can never match a standard font, i.e. every
        // measured CV Warns. The asset's font half exists to carry these names.
        if (file.FontAllowlist.Count == 0)
            throw new InvalidOperationException(
                "cv-conventions-assetet saknar fontAllowlist. En tom lista gör D3-regeln oförmögen "
                + "att känna igen ett standardtypsnitt — varje mätt CV skulle då varna.");

        var fontSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var font in file.FontAllowlist)
        {
            if (string.IsNullOrWhiteSpace(font))
                throw new InvalidOperationException("cv-conventions-assetet har ett tomt fontnamn i fontAllowlist.");

            // A font named twice is a data typo, not a second opinion — the rule dedupes on
            // normalisation anyway, but a duplicate here should fail loud like a duplicate sectionId.
            if (!fontSeen.Add(font))
                throw new InvalidOperationException(
                    $"Typsnittet '{font}' står två gånger i fontAllowlist — en post per typsnitt.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<CvSectionOrderEntry>(file.SectionOrder.Count);

        foreach (var sectionId in file.SectionOrder)
        {
            if (string.IsNullOrWhiteSpace(sectionId))
                throw new InvalidOperationException("cv-conventions-assetet har en tom sectionId i sectionOrder.");

            // A section named twice has two positions. The sort would silently take one of them and
            // the other would be a lie the file tells about itself.
            if (!seen.Add(sectionId))
                throw new InvalidOperationException(
                    $"Sektionen '{sectionId}' står två gånger i sectionOrder — en sektion har en position.");

            // TYPED ids resolve against the lexicon loader's own id-space (ParsedSectionKind). An id
            // that is NOT typed is a FREE-section candidate and is checked against the lexicon in
            // CvConventionsProvider — Infrastructure's shape check cannot see the lexicon instance.
            order.Add(CvParsingLexiconLoader.TryMapTypedSectionId(sectionId, out var kind)
                ? new CvSectionOrderEntry(sectionId, kind)
                : new CvSectionOrderEntry(sectionId, TypedKind: null));
        }

        return new CvConventions(file.ConventionsVersion, order, file.FontAllowlist);
    }
}

/// <summary>Deserialisation form for the conventions asset. Infrastructure-only (parity
/// <c>RubricFile</c>/<c>BranschgruppFile</c>) — the <c>JsonPropertyName</c>s never cross the
/// Application boundary.</summary>
internal sealed record CvConventionsFile
{
    [JsonPropertyName("conventionsVersion")]
    public string ConventionsVersion { get; init; } = "";

    [JsonPropertyName("sectionOrder")]
    public IReadOnlyList<string> SectionOrder { get; init; } = [];

    [JsonPropertyName("fontAllowlist")]
    public IReadOnlyList<string> FontAllowlist { get; init; } = [];
}
