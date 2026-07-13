using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Loads the committed, versioned occupation-field → branschgrupp map + section rule-table
/// (<c>ssyk-branschgrupp.v1.json</c>, Fas 4b 8b.4a Asset A) embedded in this assembly.
/// Deserialises the <see cref="BranschgruppFile"/> DTO and maps it to the immutable
/// <see cref="BranschgruppCatalog"/> contract — no <c>*File</c> type and no
/// <c>JsonPropertyName</c> leaks past Infrastructure (CLAUDE.md §2.1, parity
/// <see cref="RubricLoader"/>).
/// <para>
/// SHAPE validation lives here. The CROSS-ASSET validation — every sectionId + heading must be
/// resolvable by the parsing lexicon — lives in <see cref="BranschgruppProvider"/>, which
/// ctor-injects the source of truth, mirroring <c>FrameProvider(IVerbMapper)</c>. That is the
/// repo's no-fork mechanism: reference the owner of the vocabulary, never restate it.
/// <para>
/// Both run at HOST BUILD, not mid-request — but only because <c>AddCvParsing()</c> constructs the
/// provider eagerly (an INSTANCE registration). See <see cref="BranschgruppProvider"/>.
/// </para>
/// </para>
/// </summary>
internal static class BranschgruppLoader
{
    // The SAME LogicalName the csproj declares for the embedded resource.
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.ssyk-branschgrupp.v1.json";

    /// <summary>Loads the committed branschgrupp asset from the embedded resource.</summary>
    internal static BranschgruppCatalog Load()
    {
        var asm = typeof(BranschgruppLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded branschgrupp-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        return LoadFrom(stream);
    }

    /// <summary>
    /// Deserialises + maps + validates from an arbitrary stream. The test seam that drives
    /// synthetic malformed assets through the REAL path (parity <see cref="RubricLoader.LoadFrom"/>).
    /// </summary>
    internal static BranschgruppCatalog LoadFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<BranschgruppFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("ssyk-branschgrupp-assetet deserialiserade till null.");

        return MapToContract(file);
    }

    private static BranschgruppCatalog MapToContract(BranschgruppFile file)
    {
        if (string.IsNullOrWhiteSpace(file.BranschgruppVersion))
            throw new InvalidOperationException("ssyk-branschgrupp-assetet saknar branschgruppVersion.");

        if (file.OccupationFields.Count == 0)
            throw new InvalidOperationException("ssyk-branschgrupp-assetet saknar occupationFields.");

        if (file.Branschgrupper.Count == 0)
            throw new InvalidOperationException("ssyk-branschgrupp-assetet saknar branschgrupper.");

        var rulesById = new Dictionary<string, BranschgruppRules>(StringComparer.Ordinal);
        foreach (var grupp in file.Branschgrupper)
        {
            if (string.IsNullOrWhiteSpace(grupp.Id))
                throw new InvalidOperationException("En branschgrupp saknar id.");

            if (string.IsNullOrWhiteSpace(grupp.Rationale))
                throw new InvalidOperationException(
                    $"Branschgrupp '{grupp.Id}' saknar rationale (badge-copyn är KB-källad, aldrig syntetiserad).");

            if (!rulesById.TryAdd(grupp.Id, MapRules(grupp)))
                throw new InvalidOperationException($"Branschgrupp-id '{grupp.Id}' är deklarerat två gånger.");
        }

        // The fallback MUST have a rule-table: it is the 62 % majority row, not a hole. Without
        // it, every unmapped occupation would resolve to a branschgrupp with no rules and the
        // feature would look alive and be dead for most users.
        if (!rulesById.ContainsKey(BranschgruppCatalog.Fallback))
            throw new InvalidOperationException(
                $"ssyk-branschgrupp-assetet saknar fallback-branschgruppen '{BranschgruppCatalog.Fallback}'.");

        var branschgruppByField = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in file.OccupationFields)
        {
            // Fail loud, never skip. A missing conceptId means a typo'd key (System.Text.Json
            // silently ignores unknown members, so "conceptid" would deserialise to null) — and
            // a silently DROPPED occupation-field is the vacuous-filter failure mode: that
            // occupation quietly falls to Övriga forever and nothing goes red. Maintainer
            // comments belong on a real entry or in the top-level "//"-keys, never as a bare
            // object in this array.
            if (string.IsNullOrWhiteSpace(field.ConceptId))
                throw new InvalidOperationException(
                    "ssyk-branschgrupp-assetet har ett yrkesområde utan conceptId.");

            if (string.IsNullOrWhiteSpace(field.Branschgrupp))
                throw new InvalidOperationException(
                    $"Yrkesområde '{field.ConceptId}' saknar branschgrupp-tilldelning.");

            if (!rulesById.ContainsKey(field.Branschgrupp))
                throw new InvalidOperationException(
                    $"Yrkesområde '{field.ConceptId}' pekar på okänd branschgrupp '{field.Branschgrupp}'.");

            if (!branschgruppByField.TryAdd(field.ConceptId, field.Branschgrupp))
                throw new InvalidOperationException(
                    $"Yrkesområde '{field.ConceptId}' är deklarerat två gånger.");
        }

        // An ORPHAN branschgrupp: a rule-table no occupation-field points at. It loads clean and
        // can never be reached — a whole ruleset, silently dead. The mirror guard (a field
        // pointing at an unknown branschgrupp) already exists above; this is the other direction,
        // and it is the guard that belongs here.
        //
        // (What stood here was `branschgruppByField.Count == 0`, which was UNREACHABLE: the
        // OccupationFields.Count check above has already thrown on an empty list, and every loop
        // iteration either throws or adds exactly one entry. Deleting it broke no test — the same
        // dead-machinery signature as the suppression field. Both reviewers found it independently.)
        var reachable = branschgruppByField.Values.ToHashSet(StringComparer.Ordinal);
        var orphan = rulesById.Keys
            .Where(id => !string.Equals(id, BranschgruppCatalog.Fallback, StringComparison.Ordinal))
            .FirstOrDefault(id => !reachable.Contains(id));
        if (orphan is not null)
        {
            throw new InvalidOperationException(
                $"Branschgruppen '{orphan}' har en regeltabell men inget yrkesområde pekar på den " +
                "— hela regeluppsättningen vore oåtkomlig.");
        }

        return new BranschgruppCatalog(file.BranschgruppVersion, branschgruppByField, rulesById);
    }

    private static BranschgruppRules MapRules(BranschgruppFile.GruppFile grupp)
    {
        var standard = grupp.StandardSections.Select(Map).ToList();
        var suggested = grupp.SuggestedSections.Select(Map).ToList();

        // The same section offered twice (once as standard, once as merely-suggested) would render
        // as two chips for one thing. It is a data typo, not a user state.
        var offered = standard.Concat(suggested).Select(s => s.SectionId).ToList();
        var duplicate = offered.GroupBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Branschgrupp '{grupp.Id}' föreslår sektionen '{duplicate.Key}' två gånger.");
        }

        return new BranschgruppRules(grupp.Id, grupp.Rationale, standard, suggested);

        SectionRecommendation Map(BranschgruppFile.SectionFile section)
        {
            if (string.IsNullOrWhiteSpace(section.SectionId))
                throw new InvalidOperationException($"Branschgrupp '{grupp.Id}' har en sektion utan sectionId.");
            if (string.IsNullOrWhiteSpace(section.Heading))
                throw new InvalidOperationException(
                    $"Sektionen '{section.SectionId}' i branschgrupp '{grupp.Id}' saknar heading " +
                    "(rubriken skrivs in i användarens CV — den kan inte vara tom).");

            return new SectionRecommendation(section.SectionId, section.Heading);
        }
    }
}

/// <summary>Deserialisation form for the branschgrupp asset. Infrastructure-only (parity
/// <c>RubricFile</c>) — the Swedish tokens and <c>JsonPropertyName</c>s never cross the
/// Application boundary.</summary>
internal sealed record BranschgruppFile
{
    [JsonPropertyName("branschgruppVersion")]
    public string BranschgruppVersion { get; init; } = "";

    [JsonPropertyName("occupationFields")]
    public IReadOnlyList<FieldFile> OccupationFields { get; init; } = [];

    [JsonPropertyName("branschgrupper")]
    public IReadOnlyList<GruppFile> Branschgrupper { get; init; } = [];

    internal sealed record FieldFile(
        [property: JsonPropertyName("conceptId")] string? ConceptId = null,
        [property: JsonPropertyName("branschgrupp")] string? Branschgrupp = null,
        // Maintainer documentation only — deliberately NOT mapped into the contract and NOT
        // asserted by the completeness test. The mapping is keyed on conceptId, so an upstream
        // JobTech label rewording is harmless and must not raise a false alarm.
        [property: JsonPropertyName("label")] string? Label = null);

    internal sealed record GruppFile
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("rationale")]
        public string Rationale { get; init; } = "";

        [JsonPropertyName("standardSections")]
        public IReadOnlyList<SectionFile> StandardSections { get; init; } = [];

        [JsonPropertyName("suggestedSections")]
        public IReadOnlyList<SectionFile> SuggestedSections { get; init; } = [];
    }

    internal sealed record SectionFile(
        [property: JsonPropertyName("sectionId")] string SectionId,
        [property: JsonPropertyName("heading")] string Heading);
}
