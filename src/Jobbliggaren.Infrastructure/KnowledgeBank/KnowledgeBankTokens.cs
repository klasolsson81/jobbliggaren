using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Explicit Swedish-data-token → English-enum mapping tables for the knowledge-bank
/// assets (F4-7, senior-cto-advisor DQ7). <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>
/// is deliberately NOT used — the JSON carries Swedish tokens (faithful to research
/// §2, åäö-bearing) while the C# enums stay English (CLAUDE.md §1), and an unknown
/// token must fail loud rather than silently dropping to a default.
/// <para>
/// <b>Asymmetry is intentional:</b> <c>weight/category/profile/band/categoryLetter</c>
/// are fail-loud on an unknown token (a typo there would corrupt scoring silently).
/// <see cref="Assessability"/> defaults a MISSING/null token to
/// <see cref="CriterionAssessability.NotAssessedV1"/> (DQ6 — never over-report an
/// older/under-specified criterion) but still fails loud on a present-but-unknown
/// token (catches a typo).
/// </para>
/// </summary>
internal static class KnowledgeBankTokens
{
    private static readonly Dictionary<string, CriterionWeight> WeightTokens = new(StringComparer.Ordinal)
    {
        ["Kritisk"] = CriterionWeight.Critical,
        ["Hög"] = CriterionWeight.High,
        ["Medel"] = CriterionWeight.Medium,
        ["Låg"] = CriterionWeight.Low,
    };

    private static readonly Dictionary<string, RubricCategory> CategoryTokens = new(StringComparer.Ordinal)
    {
        ["Innehåll"] = RubricCategory.Content,
        ["Struktur"] = RubricCategory.Structure,
        ["Språk"] = RubricCategory.Language,
        ["ATS-parsbarhet"] = RubricCategory.AtsParsability,
        ["Visuell kvalitet"] = RubricCategory.VisualQuality,
    };

    // Compact letter keys used by categoryWeights (research §2.7: A/B/C/D, A/B/C/E).
    private static readonly Dictionary<string, RubricCategory> CategoryLetters = new(StringComparer.Ordinal)
    {
        ["A"] = RubricCategory.Content,
        ["B"] = RubricCategory.Structure,
        ["C"] = RubricCategory.Language,
        ["D"] = RubricCategory.AtsParsability,
        ["E"] = RubricCategory.VisualQuality,
    };

    private static readonly Dictionary<string, RubricProfile> ProfileTokens = new(StringComparer.Ordinal)
    {
        ["Båda"] = RubricProfile.Both,
        ["EndastAts"] = RubricProfile.AtsOnly,
        ["EndastVisuell"] = RubricProfile.VisualOnly,
    };

    private static readonly Dictionary<string, CriterionAssessability> AssessabilityTokens = new(StringComparer.Ordinal)
    {
        ["deterministic"] = CriterionAssessability.Deterministic,
        ["deterministic_plus_nlp"] = CriterionAssessability.DeterministicPlusNlp,
        ["not_assessed_v1"] = CriterionAssessability.NotAssessedV1,
    };

    private static readonly Dictionary<string, ScoreBandLabel> BandTokens = new(StringComparer.Ordinal)
    {
        ["EjRedo"] = ScoreBandLabel.NotReady,
        ["BehöverOmarbetning"] = ScoreBandLabel.NeedsRework,
        ["Konkurrenskraftigt"] = ScoreBandLabel.Competitive,
        ["Toppskikt"] = ScoreBandLabel.TopTier,
    };

    // Cliché-lexicon entry kinds are English tokens (the phrases are Swedish, the discriminator
    // is code — CLAUDE.md §1). A missing/null token defaults to Cliche (the safe minimal default:
    // an N-1 asset without the field keeps the original anti-cliché-only behaviour, A7's domain).
    private static readonly Dictionary<string, ClicheKind> ClicheKindTokens = new(StringComparer.Ordinal)
    {
        ["cliche"] = ClicheKind.Cliche,
        ["softSkill"] = ClicheKind.SoftSkill,
    };

    public static CriterionWeight Weight(string token) =>
        Resolve(WeightTokens, token, "vikt");

    public static RubricCategory Category(string token) =>
        Resolve(CategoryTokens, token, "kategori");

    public static RubricCategory CategoryLetter(string token) =>
        Resolve(CategoryLetters, token, "kategori-bokstav");

    public static RubricProfile Profile(string token) =>
        Resolve(ProfileTokens, token, "profil");

    public static ScoreBandLabel Band(string token) =>
        Resolve(BandTokens, token, "score-band");

    /// <summary>Maps the assessability token. A missing/null token defaults to
    /// <see cref="CriterionAssessability.NotAssessedV1"/> (DQ6 honest default); a
    /// present-but-unknown token fails loud.</summary>
    public static CriterionAssessability Assessability(string? token) =>
        token is null
            ? CriterionAssessability.NotAssessedV1
            : Resolve(AssessabilityTokens, token, "assessability");

    /// <summary>Maps the cliché-entry kind token. A missing/null token defaults to
    /// <see cref="ClicheKind.Cliche"/> (an older asset without the field keeps the original
    /// anti-cliché-only routing); a present-but-unknown token fails loud (catches a typo).</summary>
    public static ClicheKind ClicheEntryKind(string? token) =>
        token is null
            ? ClicheKind.Cliche
            : Resolve(ClicheKindTokens, token, "cliché-kind");

    private static TEnum Resolve<TEnum>(Dictionary<string, TEnum> table, string token, string kind)
        where TEnum : struct, Enum =>
        table.TryGetValue(token, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Okänd {kind}-token i knowledge-bank-asset: '{token}'.");
}
