using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAdExtraction;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — the REAL
/// <see cref="JobAdKeywordExtractor"/> against the committed JobTech
/// skill-taxonomy asset + the real Swedish Snowball analyzer (mirrors
/// <c>OccupationCodeDeriverIntegrationTests</c> / <c>SwedishStemmerPostgresParityTests</c>).
/// The extractor is pure/in-process (no DB, no external call) — so unlike the
/// other F4 integration suites this one needs NO Postgres container; the
/// "integration" is the real NLP tier + the real embedded skill taxonomy. The
/// persistence + GIN round-trip lives in
/// <c>JobAdExtractedTermsPersistenceTests</c> (Testcontainers).
///
/// GOLDEN PROVENANCE (F4-2/F4-3 lesson — derive from the committed asset, NEVER
/// guess): the golden skill labels are READ LIVE from
/// <c>jobad-skill-taxonomy.v30.json</c> and the expected match lexeme is computed
/// LIVE via the same analyzer the extractor uses. A future asset/stemmer bump
/// updates the expectation automatically instead of asserting a stale magic token.
/// We pick concepts whose preferredLabel is a SINGLE distinctive token that the
/// analyzer reduces to exactly one lexeme (so the skill matches when that one
/// stemmed lexeme appears in the ad).
///
/// RED until JobAdKeywordExtractor + IJobAdKeywordExtractor + the
/// Domain ExtractedTerms VO ship.
/// </summary>
public sealed class JobAdKeywordExtractorIntegrationTests
{
    // The SAME embedded asset the extractor loads (csproj LogicalName). Read here
    // ONLY to derive golden labels live — the extractor owns its own loader in prod.
    private const string SkillTaxonomyResource =
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";

    // SUT factory — the exact ctor the impl exposes (architect §3.3 / DI Singleton):
    // internal sealed, ctor(ITextAnalyzer, IStemmer). Construct the real Swedish
    // analyzer + stemmer (parity OccupationCodeDeriverIntegrationTests.NewDeriver).
    // Concrete return type (not the IJobAdKeywordExtractor port) satisfies CA1859
    // for this local factory — the SUT is the real Infrastructure impl resolved by
    // direct construction (parity OccupationCodeDeriverIntegrationTests.NewDeriver).
    private static JobAdKeywordExtractor NewExtractor()
    {
        var stemmer = new SnowballSwedishStemmer();
        var analyzer = new SwedishTextAnalyzer(stemmer);
        return new JobAdKeywordExtractor(analyzer, stemmer);
    }

    // Concrete types (CA1859 — these are the real Infrastructure impls used to
    // derive goldens live, parity OccupationCodeDeriverIntegrationTests' direct
    // construction of the concrete SUT).
    private static readonly SnowballSwedishStemmer Stemmer = new();
    private static readonly SwedishTextAnalyzer Analyzer = new(Stemmer);

    // ===============================================================
    // (a) A skill label in the description → a Skill term with ConceptId + evidence
    // ===============================================================

    [Fact]
    public void Extract_SkillLabelInDescription_YieldsSkillTermWithConceptIdAndEvidence()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewExtractor();

        // Build a description that plainly contains the skill label (surrounded by
        // ordinary Swedish so the skill must come from the taxonomy match, not noise).
        var input = new JobAdExtractionInput(
            Title: "Vi söker en medarbetare",
            Description: $"I rollen ingår {golden.PreferredLabel} och annat arbete.");

        var result = sut.Extract(input);

        result.IsEmpty.ShouldBeFalse();
        result.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Skill && t.ConceptId == golden.ConceptId,
            $"En Skill-term för '{golden.PreferredLabel}' (concept {golden.ConceptId}) " +
            "ska extraheras när labeln finns i annonstexten.");
        // Inspect the matched skill term.
        var term = result.Terms.First(
            t => t.Kind == ExtractedTermKind.Skill && t.ConceptId == golden.ConceptId);
        term.Lexeme.ShouldBe(golden.ConceptId, "Skill: Lexeme == ConceptId (concept-level overlap-token).");
        term.MatchedOn.ShouldNotBeNullOrWhiteSpace("cited evidence (ADR 0074) — aldrig opak.");
        // The cited evidence is the skill's own label span.
        term.MatchedOn.ShouldBe(golden.PreferredLabel,
            "MatchedOn ska citera den matchade skill-labeln (explainable by design).");
        term.Display.ShouldBe(golden.PreferredLabel, "Display = preferred label.");
    }

    [Fact]
    public void Extract_SkillLabelInTitle_CitesTitleAsSource()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewExtractor();

        var input = new JobAdExtractionInput(
            Title: golden.PreferredLabel,
            Description: "Allmän beskrivning av tjänsten.");

        var result = sut.Extract(input);

        var term = result.Terms.First(
            t => t.Kind == ExtractedTermKind.Skill && t.ConceptId == golden.ConceptId);
        // A skill whose lexemes occur in the title is sourced from the Title.
        term.Source.ShouldBe(ExtractedTermSource.Title);
    }

    // ===============================================================
    // (b) An inflected (bestämd form) skill label still resolves via Snowball
    // ===============================================================

    [Fact]
    public void Extract_InflectedSkillLabel_ResolvesViaSnowballStemming()
    {
        // Pick a single-token skill label and inflect it to its Swedish definite
        // form (label + "en"/"n"). The extractor lexemizes via Snowball, so the
        // inflected surface must stem to the SAME lexeme as the label — we verify
        // that precondition LIVE (never a guessed stem), then assert the match.
        var golden = SingleTokenSkillGoldens()
            .First(g =>
            {
                var inflected = DefiniteForm(g.PreferredLabel);
                // The inflected surface must NOT be itself a label token but MUST
                // stem to the label's single lexeme — i.e. only Snowball can bridge.
                var labelLex = SingleLexeme(g.PreferredLabel);
                var inflLex = SingleLexeme(inflected);
                return labelLex is not null && inflLex == labelLex
                       && !string.Equals(inflected, g.PreferredLabel, StringComparison.OrdinalIgnoreCase);
            });

        var inflectedForm = DefiniteForm(golden.PreferredLabel);
        var sut = NewExtractor();

        var input = new JobAdExtractionInput(
            Title: "Tjänst söks",
            Description: $"Du arbetar med {inflectedForm} dagligen.");

        var result = sut.Extract(input);

        result.Terms.ShouldContain(
            t => t.Kind == ExtractedTermKind.Skill && t.ConceptId == golden.ConceptId,
            $"Böjd form '{inflectedForm}' ska stemma till samma lexem som labeln " +
            $"'{golden.PreferredLabel}' (Snowball, F4-2) och matcha skill {golden.ConceptId}.");
    }

    // ===============================================================
    // (c) A plain non-skill word → a Keyword term (ConceptId null)
    // ===============================================================

    [Fact]
    public void Extract_PlainNonSkillWord_YieldsKeywordWithNoConceptId()
    {
        var sut = NewExtractor();

        // "trivseln" is ordinary Swedish, not a skill-taxonomy concept label.
        var input = new JobAdExtractionInput(
            Title: "Tjänst",
            Description: "Vi värdesätter trivseln på arbetsplatsen.");

        var result = sut.Extract(input);

        // Every keyword carries no concept-id and cites its own surface form.
        result.Terms.ShouldContain(t => t.Kind == ExtractedTermKind.Keyword);
        result.Terms
            .Where(t => t.Kind == ExtractedTermKind.Keyword)
            .ShouldAllBe(t => t.ConceptId == null,
                "Keyword bär aldrig ConceptId (det är skill-spårets signatur).");
        var keyword = result.Terms.First(t => t.Kind == ExtractedTermKind.Keyword);
        keyword.MatchedOn.ShouldNotBeNullOrWhiteSpace("keyword citerar sin yt-form (evidence).");
    }

    // ===============================================================
    // (d) Empty / whitespace input → ExtractedTerms.Empty, never throws
    // ===============================================================

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("\t", "\n")]
    public void Extract_BlankInput_ReturnsEmpty_NeverThrows(string title, string description)
    {
        var sut = NewExtractor();

        var result = sut.Extract(new JobAdExtractionInput(title, description));

        result.ShouldBe(ExtractedTerms.Empty);
        result.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Extract_StopwordsOnlyInput_ReturnsEmpty()
    {
        // Pure Swedish stopwords lexemize to nothing → Empty (no false keywords).
        var sut = NewExtractor();

        var result = sut.Extract(new JobAdExtractionInput("och en", "att de som är"));

        result.IsEmpty.ShouldBeTrue("rena stopord ska ge tom extraktion, inte falska keywords.");
    }

    // ===============================================================
    // (e) Determinism — same input twice → identical ordered sequence
    // ===============================================================

    [Fact]
    public void Extract_SameInputTwice_ReturnsIdenticalOrderedSequence()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewExtractor();
        var input = new JobAdExtractionInput(
            Title: "Systemutvecklare med bred kompetens",
            Description: $"Erfarenhet av {golden.PreferredLabel}, ekonomi, ledning och ansvar.");

        var first = sut.Extract(input);
        var second = sut.Extract(input);

        // Identical sequence (order + content) — not merely set-equal.
        var firstKeys = first.Terms
            .Select(t => (t.Lexeme, t.Kind, t.Source, t.Weight)).ToList();
        var secondKeys = second.Terms
            .Select(t => (t.Lexeme, t.Kind, t.Source, t.Weight)).ToList();
        secondKeys.ShouldBe(firstKeys);
        // VO structural equality is the stronger statement.
        second.ShouldBe(first);
    }

    [Fact]
    public void Extract_TwoFreshExtractorInstances_AgreeOnSameInput()
    {
        // Determinism across instances (the lazy skill index is immutable reference
        // data — two builds yield the same matching behavior).
        var golden = FirstSingleTokenSkillGolden();
        var input = new JobAdExtractionInput("Titel", $"Arbete med {golden.PreferredLabel}.");

        var a = NewExtractor().Extract(input);
        var b = NewExtractor().Extract(input);

        a.ShouldBe(b);
    }

    // ===============================================================
    // (f) Ordering + bounded cap (≤ MaxTerms = 64)
    // ===============================================================

    [Fact]
    public void Extract_RichAd_IsDeterministicallyOrdered_SkillsBeforeKeywords()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewExtractor();
        var input = new JobAdExtractionInput(
            Title: "Erfaren medarbetare",
            Description:
                $"Vi söker dig med {golden.PreferredLabel}. Du har erfarenhet av " +
                "ekonomi, ledning, kvalitet, utbildning, kommunikation och ansvar. " +
                "Arbetet kräver noggrannhet, samarbete och initiativförmåga.");

        var result = sut.Extract(input);

        var kinds = result.Terms.Select(t => (int)t.Kind).ToList();
        // Kind ordinal non-decreasing: every Skill (0) precedes every Keyword (1).
        kinds.ShouldBe(kinds.OrderBy(k => k).ToList(),
            "Skills ska sorteras före Keywords (ExtractedTermKind declaration-order).");
    }

    [Fact]
    public void Extract_VeryLongAd_IsBoundedToMaxTerms()
    {
        var sut = NewExtractor();
        // 300 distinct non-stopword tokens → far more than the 64-cap.
        var words = string.Join(' ',
            Enumerable.Range(0, 300).Select(i => $"ordalydelse{i:D3}"));
        var input = new JobAdExtractionInput("Lång annons", words);

        var result = sut.Extract(input);

        result.Terms.Count.ShouldBeLessThanOrEqualTo(ExtractedTerms.MaxTerms,
            "En mycket lång annons får inte ge en obegränsad term-lista (DoS-/relevans-bound).");
    }

    // ===============================================================
    // (g) The extractor itself persists nothing / is a pure function
    // ===============================================================

    [Fact]
    public void Extract_IsPure_ReturnsNormalizedDomainValueObject_NoSideEffects()
    {
        // The extractor returns the canonical Domain VO directly (Variant A) and
        // touches no DB/external resource — calling it twice on independent inputs
        // does not bleed state between calls (the index is immutable shared data).
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewExtractor();

        var withSkill = sut.Extract(new JobAdExtractionInput("T", $"Arbete med {golden.PreferredLabel}."));
        var blank = sut.Extract(new JobAdExtractionInput("", ""));
        var withSkillAgain = sut.Extract(new JobAdExtractionInput("T", $"Arbete med {golden.PreferredLabel}."));

        blank.IsEmpty.ShouldBeTrue("ett tomt anrop emellan får inte påverkas av/påverka ett rikt anrop.");
        withSkillAgain.ShouldBe(withSkill, "extraktion är ren — inget state läcker mellan anrop.");
    }

    // ---------------------------------------------------------------
    // Golden derivation helpers — read the committed asset live, pick concepts
    // whose preferredLabel is a single distinctive token that lexemizes to ONE
    // lexeme (so a single stemmed token in the ad triggers the skill match).
    // ---------------------------------------------------------------

    private sealed record SkillGolden(string ConceptId, string PreferredLabel);

    private static SkillGolden FirstSingleTokenSkillGolden() =>
        SingleTokenSkillGoldens()[0];

    // All skill concepts whose preferredLabel is a single word (no space/hyphen),
    // 7–14 letters, ascii+åäö, that the analyzer reduces to exactly one lexeme AND
    // that lexeme is UNIQUE to one concept across the asset (so the match is
    // unambiguous). Ordered by conceptId for a stable, deterministic pick.
    private static List<SkillGolden> SingleTokenSkillGoldens()
    {
        var concepts = ReadSkillConcepts();

        // Build a single-token candidate set + a lexeme→conceptIds index to keep
        // only labels whose lexeme maps to exactly one concept (unambiguous golden).
        var candidates = new List<(SkillGolden Golden, string Lexeme)>();
        var lexemeOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var c in concepts)
        {
            var label = c.PreferredLabel?.Trim() ?? string.Empty;
            if (label.Length is < 7 or > 14)
                continue;
            if (label.Any(ch => !(char.IsLetter(ch))))
                continue; // single word only (rejects spaces, hyphens, digits, punctuation)

            var lex = SingleLexeme(label);
            if (lex is null)
                continue; // multi-lexeme or stopword label — not a clean single-token golden

            if (!lexemeOwners.TryGetValue(lex, out var owners))
                lexemeOwners[lex] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(c.ConceptId);
            candidates.Add((new SkillGolden(c.ConceptId, label), lex));
        }

        var goldens = candidates
            .Where(x => lexemeOwners[x.Lexeme].Count == 1) // unambiguous lexeme
            .Select(x => x.Golden)
            .OrderBy(g => g.ConceptId, StringComparer.Ordinal)
            .ToList();

        goldens.ShouldNotBeEmpty(
            "Inga single-token skill-goldens kunde härledas ur " +
            $"{SkillTaxonomyResource} — assetens form har ändrats (F4-2/F4-3 " +
            "provenance-regel: härled, gissa aldrig).");
        return goldens;
    }

    // Lexemizes a label/surface and returns its single lexeme, or null if it does
    // not reduce to exactly one lexeme (multi-token or stopword).
    private static string? SingleLexeme(string text)
    {
        var lexemes = Analyzer.ToLexemes(text, TextLanguage.Swedish);
        return lexemes.Count == 1 ? lexemes[0] : null;
    }

    // Swedish definite-form heuristic for a noun (label + "en" or "n"). Only used
    // to PROBE for an inflected surface that stems back to the label's lexeme;
    // whether it actually does is verified live before the golden is used.
    private static string DefiniteForm(string label) =>
        label.EndsWith('a') ? label + 'n' : label + "en";

    private static List<SkillConceptJson> ReadSkillConcepts()
    {
        var asm = typeof(SwedishTextAnalyzer).Assembly; // Infrastructure assembly
        using var stream = asm.GetManifestResourceStream(SkillTaxonomyResource);
        stream.ShouldNotBeNull(
            $"Skill-taxonomi-resursen '{SkillTaxonomyResource}' ska vara en " +
            "<EmbeddedResource> i Infrastructure-assemblyn (csproj LogicalName).");

        using var doc = JsonDocument.Parse(stream!);
        var skills = doc.RootElement.GetProperty("skills");
        var list = new List<SkillConceptJson>(skills.GetArrayLength());
        foreach (var el in skills.EnumerateArray())
        {
            list.Add(new SkillConceptJson(
                el.GetProperty("conceptId").GetString()!,
                el.GetProperty("preferredLabel").GetString()!));
        }
        return list;
    }

    private sealed record SkillConceptJson(string ConceptId, string PreferredLabel);
}
