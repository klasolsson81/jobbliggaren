using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Loads + validates the committed, versioned frame catalog (<c>frames.v1.json</c>,
/// Fas 4b PR-5, ADR 0093 §D3 — frames built FIRST as the hard PR-7 dependency).
/// Fail-loud at construction (parity <see cref="RubricLoader"/>): a malformed asset can
/// never reach the apply-half. Structural validation here; the §D2 provenance
/// invariants (noun-slots ⊆ Before-span, number == user echo) are PR-7's
/// <c>FromFrame</c> concern.
/// <para>
/// Cross-asset invariant (CTO-bind PR-5 D3): every sentence frame's lead verb MUST
/// resolve in the strong-verb groups of the verb mapping, and the catalog's
/// <c>verbMappingVersion</c> MUST equal the loaded mapping's version — a verb-list bump
/// forces a deliberate frames re-validation. Authoring note: the prototype's fifth
/// example verb ("kvalitetssäkrade", handoff §6.2) is NOT in verb-mapping v1.1's strong
/// set and is therefore omitted from frames v1.0 — adding it to the verb list is an
/// engine-behaviour change (A2 strong-opener ratio) awaiting a Klas product decision.
/// </para>
/// </summary>
internal static partial class FramesLoader
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.frames.v1.json";

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TemplatePlaceholders();

    [GeneratedRegex(@"^[a-zåäö][a-zåäö0-9]*$")]
    private static partial Regex SlotNameShape();

    [GeneratedRegex(@"^[A-E][1-9][0-9]?$")]
    private static partial Regex CriterionIdShape();

    public static FrameCatalog Load(VerbMapping verbMapping)
    {
        var asm = typeof(FrameProvider).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded frames-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        return LoadFrom(stream, verbMapping);
    }

    /// <summary>
    /// Deserialise + map + validate from an arbitrary stream — the same seam the rubric
    /// N-1 tests use (<see cref="RubricLoader.LoadFrom"/>): synthetic fixtures drive the
    /// REAL parse+validate path, never a parallel one.
    /// </summary>
    internal static FrameCatalog LoadFrom(Stream stream, VerbMapping verbMapping)
    {
        var file = JsonSerializer.Deserialize<FramesFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("frames.v1.json deserialiserade till null.");

        return MapToContract(file, verbMapping);
    }

    private static FrameCatalog MapToContract(FramesFile file, VerbMapping verbMapping)
    {
        if (string.IsNullOrWhiteSpace(file.Version))
        {
            throw new InvalidOperationException("frames-asset saknar framesVersion (fail-loud).");
        }

        if (!string.Equals(file.VerbMappingVersion, verbMapping.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"frames-asset är authorad mot verb-mapping v{file.VerbMappingVersion} men den " +
                $"laddade verb-mappingen är v{verbMapping.Version}. En verb-listbump kräver en " +
                "medveten frames-omvalidering + versionsstämpel (ADR 0093 §D2 \"verb list at a " +
                "specific version\").");
        }

        var strongVerbs = verbMapping.StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var frames = file.Frames.Select(f => MapFrame(f, strongVerbs, seenIds)).ToList();

        if (frames.Count == 0)
        {
            throw new InvalidOperationException("frames-asset innehåller inga frames (fail-loud).");
        }

        return new FrameCatalog(file.Version, file.VerbMappingVersion, frames);
    }

    private static CvFrame MapFrame(
        FramesFile.FrameFile f, IReadOnlySet<string> strongVerbs, HashSet<string> seenIds)
    {
        if (string.IsNullOrWhiteSpace(f.Id))
        {
            throw new InvalidOperationException("frame saknar id (fail-loud).");
        }

        if (!seenIds.Add(f.Id))
        {
            throw new InvalidOperationException($"frame-id '{f.Id}' är duplicerat (fail-loud).");
        }

        var kind = ResolveKind(f.Id, f.Kind);
        var criterionIds = ValidateCriterionIds(f.Id, f.CriterionIds);
        var slots = ValidateSlots(f.Id, f.Slots);
        ValidateVerbContract(f.Id, kind, f.Verb, slots, strongVerbs);
        ValidateTemplate(f.Id, f.Template, slots);

        return new CvFrame(f.Id, kind, criterionIds, f.Verb, slots, f.Template);
    }

    private static FrameKind ResolveKind(string id, string kind) => kind switch
    {
        "sentence" => FrameKind.Sentence,
        "measure" => FrameKind.Measure,
        _ => throw new InvalidOperationException(
            $"frame '{id}': okänd kind-token '{kind}' (fail-loud, parity KnowledgeBankTokens)."),
    };

    private static IReadOnlyList<string> ValidateCriterionIds(
        string id, IReadOnlyList<string> criterionIds)
    {
        if (criterionIds.Count == 0)
        {
            throw new InvalidOperationException($"frame '{id}': criterionIds är tom (fail-loud).");
        }

        foreach (var criterionId in criterionIds)
        {
            if (!CriterionIdShape().IsMatch(criterionId))
            {
                throw new InvalidOperationException(
                    $"frame '{id}': criterionId '{criterionId}' matchar inte rubrik-id-formen " +
                    "(A1..E99, fail-loud).");
            }
        }

        return criterionIds;
    }

    private static List<FrameSlot> ValidateSlots(
        string id, IReadOnlyList<FramesFile.SlotFile> slotFiles)
    {
        if (slotFiles.Count == 0)
        {
            throw new InvalidOperationException($"frame '{id}': slots är tom (fail-loud).");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var slots = new List<FrameSlot>(slotFiles.Count);
        foreach (var slot in slotFiles)
        {
            if (!SlotNameShape().IsMatch(slot.Name))
            {
                throw new InvalidOperationException(
                    $"frame '{id}': slot-namnet '{slot.Name}' är inte en gemen identifierare " +
                    "(fail-loud).");
            }

            if (!seen.Add(slot.Name))
            {
                throw new InvalidOperationException(
                    $"frame '{id}': slot-namnet '{slot.Name}' är duplicerat (fail-loud).");
            }

            slots.Add(new FrameSlot(slot.Name, ResolveSlotKind(id, slot.Name, slot.Kind)));
        }

        return slots;
    }

    private static FrameSlotKind ResolveSlotKind(string id, string name, string kind) => kind switch
    {
        "noun" => FrameSlotKind.Noun,
        "verb" => FrameSlotKind.Verb,
        "number" => FrameSlotKind.Number,
        "text" => FrameSlotKind.Text,
        _ => throw new InvalidOperationException(
            $"frame '{id}': slot '{name}' har okänd kind-token '{kind}' (fail-loud)."),
    };

    private static void ValidateVerbContract(
        string id,
        FrameKind kind,
        string? verb,
        IReadOnlyList<FrameSlot> slots,
        IReadOnlySet<string> strongVerbs)
    {
        var verbSlots = slots.Count(s => s.Kind == FrameSlotKind.Verb);
        var numberSlots = slots.Count(s => s.Kind == FrameSlotKind.Number);

        if (kind == FrameKind.Sentence)
        {
            if (string.IsNullOrWhiteSpace(verb))
            {
                throw new InvalidOperationException(
                    $"frame '{id}': en sentence-frame kräver ett fast lead-verb (fail-loud).");
            }

            if (!strongVerbs.Contains(verb))
            {
                throw new InvalidOperationException(
                    $"frame '{id}': verbet '{verb}' finns inte i verb-mappningens starka grupper " +
                    "— cross-asset-invarianten (ADR 0093 §D2 b) fail-loud.");
            }

            if (verbSlots != 0)
            {
                throw new InvalidOperationException(
                    $"frame '{id}': en sentence-frame har ett FAST verb och får inte bära en " +
                    "verb-slot (fail-loud).");
            }

            if (numberSlots != 0)
            {
                throw new InvalidOperationException(
                    $"frame '{id}': en sentence-frame bär inga number-slots i v1 — siffror hör " +
                    "till måttramen (fail-loud; en framtida frame bumper asset + loader medvetet).");
            }
        }
        else
        {
            if (verb is not null)
            {
                throw new InvalidOperationException(
                    $"frame '{id}': en measure-frame väljer verbet vid apply och får inte bära " +
                    "ett fast lead-verb (fail-loud).");
            }

            if (verbSlots != 1)
            {
                throw new InvalidOperationException(
                    $"frame '{id}': en measure-frame kräver exakt EN verb-slot (fann {verbSlots}, " +
                    "fail-loud).");
            }

            if (numberSlots == 0)
            {
                throw new InvalidOperationException(
                    $"frame '{id}': en measure-frame kräver minst en number-slot — mekaniken ÄR " +
                    "användarens egen siffra (handoff §6.2, fail-loud).");
            }
        }
    }

    private static void ValidateTemplate(
        string id, string template, IReadOnlyList<FrameSlot> slots)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException($"frame '{id}': template är tom (fail-loud).");
        }

        var placeholders = TemplatePlaceholders().Matches(template)
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (placeholders.Distinct(StringComparer.Ordinal).Count() != placeholders.Count)
        {
            throw new InvalidOperationException(
                $"frame '{id}': en placeholder förekommer mer än en gång i templaten (fail-loud).");
        }

        var slotNames = slots.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var placeholderSet = placeholders.ToHashSet(StringComparer.Ordinal);

        if (!placeholderSet.SetEquals(slotNames))
        {
            throw new InvalidOperationException(
                $"frame '{id}': template-placeholders [{string.Join(", ", placeholders)}] matchar " +
                $"inte de deklarerade slots [{string.Join(", ", slotNames)}] (arity-invarianten, " +
                "fail-loud).");
        }

        var stripped = TemplatePlaceholders().Replace(template, string.Empty);
        if (stripped.Contains('{') || stripped.Contains('}'))
        {
            throw new InvalidOperationException(
                $"frame '{id}': templaten innehåller obalanserade klammerparenteser (fail-loud).");
        }
    }
}

/// <summary>Deserialisation form for the frames asset.</summary>
internal sealed record FramesFile
{
    [JsonPropertyName("framesVersion")]
    public string Version { get; init; } = "unknown";

    [JsonPropertyName("verbMappingVersion")]
    public string VerbMappingVersion { get; init; } = "unknown";

    [JsonPropertyName("frames")]
    public IReadOnlyList<FrameFile> Frames { get; init; } = [];

    internal sealed record FrameFile
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; init; } = string.Empty;

        [JsonPropertyName("criterionIds")]
        public IReadOnlyList<string> CriterionIds { get; init; } = [];

        [JsonPropertyName("verb")]
        public string? Verb { get; init; }

        [JsonPropertyName("slots")]
        public IReadOnlyList<SlotFile> Slots { get; init; } = [];

        [JsonPropertyName("template")]
        public string Template { get; init; } = string.Empty;
    }

    internal sealed record SlotFile(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("kind")] string Kind);
}
