using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAdExtraction;

/// <summary>
/// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075 — NO AI/LLM) — the REAL
/// <see cref="JobAdKeywordExtractor"/>'s NEW requirement pass: each
/// <c>JobAdExtractionInput.Requirements[i]</c> (a pre-linked JobTech must_have/
/// nice_to_have skill concept) becomes an <see cref="ExtractedTermKind.Requirement"/>
/// term, merged BEFORE <see cref="ExtractedTerms.From"/> (the sole-VO-producer
/// invariant, ADR 0075). Unlike the title/description skill pass this needs NO
/// taxonomy match — the requirement concepts arrive already linked, so these tests
/// feed <c>Requirements</c> directly and assert the produced Requirement terms.
///
/// Mirrors <see cref="JobAdKeywordExtractorIntegrationTests"/> (same real Swedish
/// Snowball analyzer + stemmer, no Postgres container — the extractor is pure).
///
/// RED until: <c>JobAdRequirement</c> (Application), <c>JobAdExtractionInput.
/// Requirements</c> (+ the 2-arg back-compat ctor), and the extractor's requirement
/// pass ship.
/// </summary>
public sealed class JobAdKeywordExtractorRequirementsTests
{
    private static JobAdKeywordExtractor NewExtractor()
    {
        var stemmer = new SnowballStemmer();
        var analyzer = new LocalTextAnalyzer(stemmer);
        return new JobAdKeywordExtractor(analyzer, stemmer);
    }

    // A must_have / nice_to_have requirement concept already linked by JobTech.
    private static JobAdRequirement Req(
        string conceptId,
        string label,
        ExtractedTermSource source = ExtractedTermSource.MustHave,
        double weight = 10) =>
        new(source, conceptId, label, weight);

    // ===============================================================
    // (a) The requirement pass produces a Requirement term per input requirement
    // ===============================================================

    [Fact]
    public void Extract_WithRequirements_ProducesRequirementTermPerInput()
    {
        var sut = NewExtractor();

        var input = new JobAdExtractionInput(
            Title: "Vi söker en medarbetare",
            Description: "Allmän beskrivning av tjänsten.",
            Requirements:
            [
                Req("Rq01_must_aaa", "C#", ExtractedTermSource.MustHave, 10),
                Req("Rq02_nice_bbb", "Azure", ExtractedTermSource.NiceToHave, 5),
            ]);

        var result = sut.Extract(input);

        // The must_have requirement.
        result.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == "Rq01_must_aaa");
        var mustTerm = result.Terms.First(
            t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == "Rq01_must_aaa");
        mustTerm.Lexeme.ShouldBe("Rq01_must_aaa", "Requirement: Lexeme == ConceptId (concept-level overlap-token).");
        mustTerm.Display.ShouldBe("C#", "Display = requirement label.");
        mustTerm.MatchedOn.ShouldBe("C#", "MatchedOn citerar requirement-labeln (explainable by design).");
        mustTerm.Source.ShouldBe(ExtractedTermSource.MustHave);
        mustTerm.Weight.ShouldBe(10);

        // The nice_to_have requirement.
        var niceTerm = result.Terms.First(
            t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == "Rq02_nice_bbb");
        niceTerm.Source.ShouldBe(ExtractedTermSource.NiceToHave);
        niceTerm.Display.ShouldBe("Azure");
    }

    // ===============================================================
    // (b) Empty / absent Requirements → no Requirement terms (back-compat)
    // ===============================================================

    [Fact]
    public void Extract_WithEmptyRequirements_ProducesNoRequirementTerms()
    {
        var sut = NewExtractor();

        var input = new JobAdExtractionInput(
            Title: "Systemutvecklare",
            Description: "Vi söker en utvecklare med erfarenhet av ekonomi.",
            Requirements: []);

        var result = sut.Extract(input);

        result.Terms.ShouldNotContain(t => t.Kind == ExtractedTermKind.Requirement,
            "tom Requirements-lista ska inte ge några Requirement-termer.");
    }

    [Fact]
    public void Extract_WithTwoArgInput_ProducesNoRequirementTerms_BackCompat()
    {
        // The 2-arg ctor (title, description) defaults Requirements to [] — the F4-4
        // local backfill + every non-Platsbanken source stays keyword/skill-only.
        var sut = NewExtractor();

        var input = new JobAdExtractionInput(
            Title: "Systemutvecklare",
            Description: "Vi söker en utvecklare med erfarenhet av ekonomi.");

        var result = sut.Extract(input);

        result.Terms.ShouldNotContain(t => t.Kind == ExtractedTermKind.Requirement,
            "2-arg-konstruktorn (utan Requirements) ska vara bakåtkompatibel — inga Requirement-termer.");
    }

    // ===============================================================
    // (c) Requirement terms merge WITH the title/description terms
    // ===============================================================

    [Fact]
    public void Extract_MergesRequirementTermsWithTitleAndDescriptionTerms()
    {
        var sut = NewExtractor();

        var input = new JobAdExtractionInput(
            Title: "Tjänst",
            Description: "Vi värdesätter trivseln på arbetsplatsen.",
            Requirements: [Req("Rq01_must_aaa", "C#")]);

        var result = sut.Extract(input);

        // The Requirement term is present...
        result.Terms.ShouldContain(t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == "Rq01_must_aaa");
        // ...alongside the keyword(s) extracted from the free-text description.
        result.Terms.ShouldContain(t => t.Kind == ExtractedTermKind.Keyword,
            "Requirement-passet ska MERGAS med (inte ersätta) title/description-termerna.");
    }

    // ===============================================================
    // (d) A concept present in BOTH must_have AND description → a Skill term
    //     AND a Requirement term (different identity, both survive)
    // ===============================================================

    [Fact]
    public void Extract_ConceptInBothMustHaveAndDescription_YieldsSkillTermAndRequirementTerm()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewExtractor();

        // The same concept appears as a must_have requirement (pre-linked) AND in the
        // free-text description (where the skill pass resolves it via the taxonomy).
        var input = new JobAdExtractionInput(
            Title: "Erfaren medarbetare",
            Description: $"I rollen ingår {golden.PreferredLabel} och annat arbete.",
            Requirements: [Req(golden.ConceptId, golden.PreferredLabel, ExtractedTermSource.MustHave, 10)]);

        var result = sut.Extract(input);

        // A Skill term for the description-resolved concept.
        result.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Skill && t.ConceptId == golden.ConceptId,
            "konceptet i description ska ge en Skill-term (taxonomi-match).");
        // AND a Requirement term for the same concept from must_have.
        result.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Requirement && t.ConceptId == golden.ConceptId,
            "samma koncept i must_have ska ge en separat Requirement-term (olika Kind = olika identitet).");
    }

    // ---------------------------------------------------------------
    // Golden derivation — reuse the live-asset provenance rule (F4-2/F4-3): pick a
    // single-token skill concept the analyzer reduces to exactly one lexeme, read
    // LIVE from the committed taxonomy so a future asset bump can't stale this.
    // ---------------------------------------------------------------

    private sealed record SkillGolden(string ConceptId, string PreferredLabel);

    private static readonly SnowballStemmer Stemmer = new();
    private static readonly LocalTextAnalyzer Analyzer = new(Stemmer);

    private const string SkillTaxonomyResource =
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";

    private static SkillGolden FirstSingleTokenSkillGolden()
    {
        var asm = typeof(LocalTextAnalyzer).Assembly;
        using var stream = asm.GetManifestResourceStream(SkillTaxonomyResource);
        stream.ShouldNotBeNull(
            $"Skill-taxonomi-resursen '{SkillTaxonomyResource}' ska vara en embedded resource.");

        using var doc = System.Text.Json.JsonDocument.Parse(stream!);
        var skills = doc.RootElement.GetProperty("skills");

        var candidates = new List<(SkillGolden Golden, string Lexeme)>();
        var lexemeOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var el in skills.EnumerateArray())
        {
            var conceptId = el.GetProperty("conceptId").GetString()!;
            var label = el.GetProperty("preferredLabel").GetString()?.Trim() ?? string.Empty;
            if (label.Length is < 7 or > 14)
                continue;
            if (label.Any(ch => !char.IsLetter(ch)))
                continue;

            var lexemes = Analyzer.ToLexemes(label, TextLanguage.Swedish);
            if (lexemes.Count != 1)
                continue;
            var lex = lexemes[0];

            if (!lexemeOwners.TryGetValue(lex, out var owners))
                lexemeOwners[lex] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(conceptId);
            candidates.Add((new SkillGolden(conceptId, label), lex));
        }

        var golden = candidates
            .Where(x => lexemeOwners[x.Lexeme].Count == 1)
            .Select(x => x.Golden)
            .OrderBy(g => g.ConceptId, StringComparer.Ordinal)
            .FirstOrDefault();

        golden.ShouldNotBeNull(
            "Inga single-token skill-goldens kunde härledas — assetens form har ändrats " +
            "(F4-2/F4-3 provenance-regel: härled, gissa aldrig).");
        return golden!;
    }
}
