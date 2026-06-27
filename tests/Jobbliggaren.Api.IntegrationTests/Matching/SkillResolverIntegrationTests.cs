using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the REAL CV-side
/// <see cref="ISkillResolver"/> (the <c>internal sealed SkillResolver</c> over the
/// shared <c>internal sealed SkillTaxonomyIndex</c>) against the committed JobTech
/// skill-taxonomy asset + the real Swedish Snowball analyzer. Pure/in-process (no DB,
/// no external hop) — so, like <see cref="JobAdExtraction.JobAdKeywordExtractorIntegrationTests"/>,
/// the "integration" is the real NLP tier + the real embedded taxonomy, not Postgres.
///
/// <para>
/// GOLDEN PROVENANCE (F4-2/F4-3 lesson — derive from the committed asset, NEVER guess):
/// the golden skill labels are READ LIVE from <c>jobad-skill-taxonomy.v30.json</c> and
/// the expected concept-id is computed LIVE via the SAME index the resolver uses, so a
/// future asset/stemmer bump updates the expectation automatically rather than asserting
/// a stale magic token.
/// </para>
///
/// <para>
/// <b>THE NO-PARALLEL-INDEX REGRESSION (test 4, ADR 0076 Decision 6):</b> the resolver
/// must resolve EXACTLY the same concept-ids the (refactored, shared-index)
/// <c>JobAdKeywordExtractor</c> emits as Skill terms — proving extraction is
/// behaviour-preserving and there is ONE shared <c>SkillTaxonomyIndex</c>, not a second
/// resolver index that could silently diverge.
/// </para>
///
/// RED until F4-15 ships <c>ISkillResolver</c> (Application) +
/// <c>SkillResolver</c>/<c>SkillTaxonomyIndex</c> (Infrastructure, internal sealed).
/// </summary>
public sealed class SkillResolverIntegrationTests
{
    private const string SkillTaxonomyResource =
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";

    private static readonly SnowballStemmer Stemmer = new();
    private static readonly LocalTextAnalyzer Analyzer = new(Stemmer);

    // SUT factory — the real shared index + the real CV-side resolver over it (the SAME
    // index the ad-side extractor uses, ADR 0076 Decision 6). internal sealed ctors
    // (SkillTaxonomyIndex(ITextAnalyzer), SkillResolver(SkillTaxonomyIndex)) reachable via
    // InternalsVisibleTo("Jobbliggaren.Api.IntegrationTests").
    private static SkillResolver NewResolver() =>
        new(new SkillTaxonomyIndex(new LocalTextAnalyzer(new SnowballStemmer())));

    // The bare shared index — the substring-search region (ADR 0079 STEG 3 PR-C) exercises
    // SkillTaxonomyIndex.Search(query, max) directly to assert the index-level rank/cap rules
    // (the resolver's MaxSearchResults=20 cap is verified through SkillResolver.Search).
    private static SkillTaxonomyIndex NewIndex() =>
        new(new LocalTextAnalyzer(new SnowballStemmer()));

    // The same index a fresh extractor would build, plus a fresh extractor wired to it —
    // the "one shared index" the no-parallel-resolver pin (test 4) exercises.
    private static (JobAdKeywordExtractor Extractor, SkillResolver Resolver, SkillTaxonomyIndex Index)
        NewSharedPair()
    {
        var stemmer = new SnowballStemmer();
        var analyzer = new LocalTextAnalyzer(stemmer);
        var index = new SkillTaxonomyIndex(analyzer);
        return (new JobAdKeywordExtractor(analyzer, stemmer, index), new SkillResolver(index), index);
    }

    // ===============================================================
    // 1. A known Swedish skill label resolves to its concept-id
    // ===============================================================

    [Fact]
    public void Resolve_KnownSwedishSkillLabel_YieldsItsConceptId()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewResolver();

        var resolved = sut.Resolve([golden.PreferredLabel], TestContext.Current.CancellationToken);

        resolved.ShouldContain(golden.ConceptId,
            $"Skill-labeln '{golden.PreferredLabel}' ska resolvas till concept {golden.ConceptId}.");
    }

    [Fact]
    public void Resolve_InflectedSkillLabel_ResolvesViaSnowballStemming()
    {
        // A CV writes the definite Swedish form; Snowball must bridge it to the label's
        // lexeme (same NLP tier the ad side uses). Verified LIVE before asserting.
        var golden = SingleTokenSkillGoldens()
            .First(g =>
            {
                var inflected = DefiniteForm(g.PreferredLabel);
                var labelLex = SingleLexeme(g.PreferredLabel);
                var inflLex = SingleLexeme(inflected);
                return labelLex is not null && inflLex == labelLex
                       && !string.Equals(inflected, g.PreferredLabel, StringComparison.OrdinalIgnoreCase);
            });
        var sut = NewResolver();

        var resolved = sut.Resolve([DefiniteForm(golden.PreferredLabel)], TestContext.Current.CancellationToken);

        resolved.ShouldContain(golden.ConceptId,
            $"Böjd form ska stemma till samma lexem som labeln '{golden.PreferredLabel}' (Snowball).");
    }

    // ===============================================================
    // 2. Unresolvable garbage / blank input → empty, never throws
    // ===============================================================

    [Fact]
    public void Resolve_UnresolvableGarbage_ReturnsEmpty_NeverThrows()
    {
        var sut = NewResolver();

        var resolved = sut.Resolve(["zzqxyw"], TestContext.Current.CancellationToken);

        resolved.ShouldBeEmpty(
            "En CV-kompetens taxonomin inte bär ska droppas tyst (normalt, ej fel).");
    }

    [Fact]
    public void Resolve_EmptyList_ReturnsEmpty()
    {
        var sut = NewResolver();

        sut.Resolve([], TestContext.Current.CancellationToken).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Resolve_BlankOrWhitespaceEntries_AreDropped_NeverThrows(string blank)
    {
        var sut = NewResolver();

        var resolved = sut.Resolve([blank], TestContext.Current.CancellationToken);

        resolved.ShouldBeEmpty("Tom/whitespace-post ska droppas tyst, aldrig kasta.");
    }

    [Fact]
    public void Resolve_MixedResolvableAndGarbage_DropsGarbage_KeepsResolved()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewResolver();

        var resolved = sut.Resolve(
            ["zzqxyw", golden.PreferredLabel, "   "], TestContext.Current.CancellationToken);

        resolved.ShouldContain(golden.ConceptId);
        resolved.ShouldNotContain("zzqxyw");
    }

    // ===============================================================
    // 3. Multiple skills → distinct union of concept-ids
    // ===============================================================

    [Fact]
    public void Resolve_MultipleDistinctSkills_UnionsDistinctConceptIds()
    {
        var goldens = SingleTokenSkillGoldens().Take(3).ToList();
        goldens.Count.ShouldBe(3, "Behöver minst tre distinkta single-token-goldens.");
        var sut = NewResolver();

        var resolved = sut.Resolve(
            goldens.Select(g => g.PreferredLabel), TestContext.Current.CancellationToken);

        foreach (var golden in goldens)
            resolved.ShouldContain(golden.ConceptId);
    }

    [Fact]
    public void Resolve_SameSkillTwice_YieldsConceptIdOnce()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewResolver();

        var resolved = sut.Resolve(
            [golden.PreferredLabel, golden.PreferredLabel], TestContext.Current.CancellationToken);

        // IReadOnlySet → membership is inherently distinct; assert the count for the concept.
        resolved.Count(c => c == golden.ConceptId).ShouldBe(1,
            "Distinkt concept-id-union — samma skill två gånger ger EN post.");
    }

    [Fact]
    public void Resolve_IsDeterministic_SameInputTwice_EqualSets()
    {
        var goldens = SingleTokenSkillGoldens().Take(2).Select(g => g.PreferredLabel).ToList();
        var sut = NewResolver();

        var first = sut.Resolve(goldens, TestContext.Current.CancellationToken);
        var second = NewResolver().Resolve(goldens, TestContext.Current.CancellationToken);

        first.ShouldBe(second, ignoreOrder: true,
            "Resolvern är ren över immutable reference-data → identiska resultat.");
    }

    // ===============================================================
    // 3b. Exact-label fast-path (#253 ACC-2/ACC-4) — a discrete CV skill string
    //     that LITERALLY matches a taxonomy label/synonym (case-insensitive)
    //     resolves to EXACTLY those concept(s), short-circuiting the Snowball
    //     lexeme-bag fan-out that mis-resolves punctuation/single-letter names
    //     ("C#" -> lexeme {c} -> C, C++ ...). Both legitimate ESCO/AF twins are
    //     kept (A-pure, CTO-bind: correctness over chip-minimalism). Twin pair +
    //     fan-out are derived LIVE from the asset (never guess concept-ids).
    // ===============================================================

    [Fact]
    public void Resolve_ExactPunctuationSkill_ShortCircuitsLexemeFanout_KeepingOnlyLiteralTwins()
    {
        const string skill = "C#";
        var concepts = ReadSearchConcepts();
        var literalTwins = concepts
            .Where(c => LiteralMatches(c, skill))
            .Select(c => c.ConceptId)
            .ToHashSet(StringComparer.Ordinal);
        literalTwins.Count.ShouldBeGreaterThanOrEqualTo(2,
            "Förutsättning: 'C#' bär minst två literal-concepts (ESCO bare + AF qualified).");

        // What the OLD bare-lexeme path resolves: "C#" -> {c} -> every concept-form
        // whose lexemes are a subset of {c} (the C language, C++ x layers, C# x layers).
        var fanout = NewIndex()
            .MatchForms(Analyzer.ToLexemes(skill, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal))
            .Select(f => f.ConceptId)
            .ToHashSet(StringComparer.Ordinal);
        fanout.Count.ShouldBeGreaterThan(literalTwins.Count,
            "Förutsättning: lexeme-vägen fan-out:ar bredare än literal-träffarna (annars ingen ACC-2-bugg).");

        var resolved = NewResolver().Resolve([skill], TestContext.Current.CancellationToken);

        resolved.ShouldBe(literalTwins, ignoreOrder: true,
            "Exact-label fast-path: 'C#' resolvar till EXAKT sina literal-matchande concepts (ESCO + AF).");
        foreach (var garbage in fanout.Except(literalTwins))
            resolved.ShouldNotContain(garbage,
                "Fan-out-garbage (C/C++/C-språket) får ALDRIG resolvas från 'C#' (ACC-2 #253).");
    }

    [Fact]
    public void Resolve_ExactSkill_IsCaseInsensitiveOnTheLiteralSurface()
    {
        var upper = NewResolver().Resolve(["C#"], TestContext.Current.CancellationToken);
        var lower = NewResolver().Resolve(["c#"], TestContext.Current.CancellationToken);

        upper.Count.ShouldBeGreaterThanOrEqualTo(2);
        lower.ShouldBe(upper, ignoreOrder: true,
            "Exact-label fast-path matchar literalt case-insensitivt → 'c#' == 'C#'.");
    }

    [Fact]
    public void ResolveDetailed_ExactPunctuationSkill_YieldsLiteralTwinChips_WithRealLabels_NoRawIds()
    {
        // A-pure (#253 CTO-bind): we deliberately keep BOTH legitimate twins
        // (correctness over chip-minimalism). Each chip carries a real preferred
        // label (never a raw concept-id), and every concept-id is confirmable/scorable.
        const string skill = "C#";
        var concepts = ReadSearchConcepts();
        var literalTwins = concepts.Where(c => LiteralMatches(c, skill)).ToList();
        literalTwins.Count.ShouldBeGreaterThanOrEqualTo(2);

        var resolved = NewResolver().ResolveDetailed([skill], TestContext.Current.CancellationToken);

        resolved.Select(r => r.ConceptId).ToHashSet(StringComparer.Ordinal).ShouldBe(
            literalTwins.Select(c => c.ConceptId).ToHashSet(StringComparer.Ordinal), ignoreOrder: true,
            "ResolveDetailed för 'C#' bär exakt sina literal-twins (A-pure, behåll båda).");
        foreach (var chip in resolved)
        {
            chip.Label.ShouldNotBeNullOrWhiteSpace();
            chip.Label.ShouldNotBe(chip.ConceptId,
                "Chippen visar canonical label, aldrig ett raw concept-id (#253).");
            concepts.ShouldContain(c => c.ConceptId == chip.ConceptId && c.PreferredLabel == chip.Label,
                "Chip-labeln är en riktig preferredLabel ur den committade taxonomin.");
        }
    }

    [Fact]
    public void ResolveDetailed_And_Resolve_AgreeOnConceptIds_ForExactTwinSkill()
    {
        // The shared-path invariant must hold for the AMBIGUOUS twin input too (the
        // existing ProvingOneSharedPath test only exercises lexeme-unique goldens).
        const string skill = "C#";
        var ids = NewResolver().Resolve([skill], TestContext.Current.CancellationToken)
            .ToHashSet(StringComparer.Ordinal);
        var detailedIds = NewResolver().ResolveDetailed([skill], TestContext.Current.CancellationToken)
            .Select(r => r.ConceptId)
            .ToHashSet(StringComparer.Ordinal);

        ids.Count.ShouldBeGreaterThanOrEqualTo(2);
        detailedIds.ShouldBe(ids, ignoreOrder: true,
            "ResolveDetailed och Resolve delar samma fast-path → identiska concept-id-mängder.");
    }

    [Fact]
    public void ResolveDetailed_ExactSynonymOnlyMatch_YieldsCanonicalPreferredLabel_NotTheSynonym()
    {
        // A CV writes a SYNONYM verbatim (not the preferred label). The fast-path must
        // resolve to that single concept and the chip must show its canonical preferred
        // label, never the synonym surface the CV used (#253 ACC-4). Derived LIVE.
        var concepts = ReadSearchConcepts();
        var ownerCount = LiteralOwnerCounts(concepts);

        var found = concepts
            .Where(c => !string.IsNullOrWhiteSpace(c.PreferredLabel))
            .SelectMany(c => c.Synonyms
                .Where(s => !string.IsNullOrWhiteSpace(s)
                    && !string.Equals(s.Trim(), c.PreferredLabel.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(s => (Concept: c, Synonym: s.Trim())))
            // The synonym literal is carried by EXACTLY one concept → a clean single match.
            .FirstOrDefault(x => ownerCount.GetValueOrDefault(x.Synonym) == 1);

        found.Concept.ShouldNotBeNull(
            "Assetet ska bära minst en synonym-only single-exact-literal (härled, gissa aldrig).");

        var resolved = NewResolver().ResolveDetailed([found.Synonym], TestContext.Current.CancellationToken);

        var chip = resolved.ShouldHaveSingleItem();
        chip.ConceptId.ShouldBe(found.Concept.ConceptId);
        chip.Label.ShouldBe(found.Concept.PreferredLabel,
            "Exact-synonym-match: chippet bär canonical PreferredLabel, aldrig synonym-ytan (#253).");
    }

    [Fact]
    public void Resolve_ExactSingleLabelMatch_ResolvesToExactlyThatOneConcept()
    {
        // A literal carried by exactly ONE concept → the fast-path resolves to that one
        // and never broadens (the positive complement of the twin fan-out test). LIVE.
        var concepts = ReadSearchConcepts();
        var ownerCount = LiteralOwnerCounts(concepts);
        var golden = concepts
            .Where(c => !string.IsNullOrWhiteSpace(c.PreferredLabel))
            .First(c => ownerCount.GetValueOrDefault(c.PreferredLabel.Trim()) == 1);

        var resolved = NewResolver().Resolve([golden.PreferredLabel], TestContext.Current.CancellationToken);

        resolved.ShouldBe([golden.ConceptId], ignoreOrder: true,
            $"Exact single-label-match '{golden.PreferredLabel}' resolvar till exakt sitt enda concept.");
    }

    [Theory]
    [InlineData(" C#")]
    [InlineData("C# ")]
    [InlineData("  C#  ")]
    public void Resolve_ExactSkill_TrimsSurroundingWhitespace(string padded)
    {
        var baseline = NewResolver().Resolve(["C#"], TestContext.Current.CancellationToken);
        baseline.Count.ShouldBeGreaterThanOrEqualTo(2);

        var resolved = NewResolver().Resolve([padded], TestContext.Current.CancellationToken);

        resolved.ShouldBe(baseline, ignoreOrder: true,
            $"Omgivande whitespace ska trimmas före exact-label-uppslaget ('{padded}' == 'C#').");
    }

    // ===============================================================
    // 4. Behaviour-equivalence — the no-parallel-resolver regression
    //    (ADR 0076 Decision 6): the resolver resolves each individual skill
    //    label the shared-index extractor would emit as a Skill term, from the
    //    SAME free text, proving ONE shared SkillTaxonomyIndex.
    // ===============================================================

    [Fact]
    public void Resolve_OverAdSkillLabels_EqualsExtractorSkillConceptIds_ProvingSharedIndex()
    {
        // Build an ad whose description plainly contains several distinctive skill
        // labels; the (shared-index) extractor emits Skill terms for them. The CV-side
        // resolver, given those SAME label strings, must resolve to the SAME concept-id
        // set — there is one index, not two.
        var goldens = SingleTokenSkillGoldens().Take(3).ToList();
        var (extractor, resolver, _) = NewSharedPair();

        var labels = goldens.Select(g => g.PreferredLabel).ToList();
        var description = "I rollen ingår " + string.Join(", ", labels) + " och annat arbete.";
        var extracted = extractor.Extract(new JobAdExtractionInput("Vi söker en medarbetare", description));

        var extractorSkillConceptIds = extracted.Terms
            .Where(t => t.Kind == ExtractedTermKind.Skill)
            .Select(t => t.ConceptId!)
            .ToHashSet(StringComparer.Ordinal);

        // The resolver, over the SAME individual label strings, resolves each one.
        var resolved = resolver.Resolve(labels, TestContext.Current.CancellationToken);

        // Every label the extractor recognised as a Skill, the resolver also resolves to
        // the same concept-id (shared index — same anchoring/most-specific/Snowball).
        foreach (var conceptId in extractorSkillConceptIds)
            resolved.ShouldContain(conceptId,
                "Ad-sidans extractor och CV-sidans resolver delar EN SkillTaxonomyIndex " +
                "(ADR 0076 Decision 6) — concept-id-mängderna får inte divergera.");
    }

    [Fact]
    public void Resolve_SingleAdSkillLabel_MatchesExtractorConceptIdExactly()
    {
        // Sharpest form: one distinctive label → the extractor's lone Skill concept-id
        // equals the resolver's lone resolved concept-id (no divergence at all).
        var golden = FirstSingleTokenSkillGolden();
        var (extractor, resolver, _) = NewSharedPair();

        var extracted = extractor.Extract(new JobAdExtractionInput(
            "Tjänst", $"Arbete med {golden.PreferredLabel} dagligen."));
        var extractorConceptIds = extracted.Terms
            .Where(t => t.Kind == ExtractedTermKind.Skill && t.ConceptId == golden.ConceptId)
            .Select(t => t.ConceptId!)
            .ToList();
        extractorConceptIds.ShouldContain(golden.ConceptId,
            "Förutsättning: extractorn känner igen labeln som en Skill-term.");

        var resolved = resolver.Resolve([golden.PreferredLabel], TestContext.Current.CancellationToken);

        resolved.ShouldContain(golden.ConceptId);
    }

    // ===============================================================
    // 5. ResolveDetailed (ADR 0079 STEG 3) — like Resolve but carries each
    //    concept-id's preferred (canonical) label so the CV-seeded skill chips
    //    are user-readable. Deduped per concept-id, deterministic ordinal order,
    //    same fail-closed/honest-drop semantics. RED until ResolveDetailed ships.
    // ===============================================================

    [Fact]
    public void ResolveDetailed_KnownSwedishSkillLabel_YieldsConceptIdWithItsPreferredLabel()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewResolver();

        var resolved = sut.ResolveDetailed([golden.PreferredLabel], TestContext.Current.CancellationToken);

        var match = resolved.ShouldHaveSingleItem();
        match.ConceptId.ShouldBe(golden.ConceptId);
        // The chip must show the canonical preferred label (a bare concept-id is not
        // user-readable — propose-and-approve needs the label, CLAUDE.md §5).
        match.Label.ShouldBe(golden.PreferredLabel);
    }

    [Fact]
    public void ResolveDetailed_SameConceptViaMultipleCvSkills_DedupesToOnePerConceptId()
    {
        // A CV may list the same skill twice (e.g. the label and an inflected form) — the
        // result carries the concept-id ONCE, with one canonical label.
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewResolver();

        var resolved = sut.ResolveDetailed(
            [golden.PreferredLabel, golden.PreferredLabel], TestContext.Current.CancellationToken);

        resolved.Count(s => s.ConceptId == golden.ConceptId).ShouldBe(1,
            "Deduped per concept-id — samma skill två gånger ger EN post.");
    }

    [Fact]
    public void ResolveDetailed_UnresolvableGarbage_ReturnsEmpty_NeverThrows()
    {
        var sut = NewResolver();

        var resolved = sut.ResolveDetailed(["zzqxyw"], TestContext.Current.CancellationToken);

        resolved.ShouldBeEmpty(
            "En CV-kompetens taxonomin inte bär ska droppas tyst (fail-closed, normalt).");
    }

    [Fact]
    public void ResolveDetailed_EmptyList_ReturnsEmpty()
    {
        var sut = NewResolver();

        sut.ResolveDetailed([], TestContext.Current.CancellationToken).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ResolveDetailed_BlankOrWhitespaceEntries_AreDropped_NeverThrows(string blank)
    {
        var sut = NewResolver();

        var resolved = sut.ResolveDetailed([blank], TestContext.Current.CancellationToken);

        resolved.ShouldBeEmpty("Tom/whitespace-post ska droppas tyst, aldrig kasta.");
    }

    [Fact]
    public void ResolveDetailed_MixedResolvableAndGarbage_DropsGarbage_KeepsResolved()
    {
        var golden = FirstSingleTokenSkillGolden();
        var sut = NewResolver();

        var resolved = sut.ResolveDetailed(
            ["zzqxyw", golden.PreferredLabel, "   "], TestContext.Current.CancellationToken);

        resolved.ShouldContain(s => s.ConceptId == golden.ConceptId);
        resolved.ShouldNotContain(s => s.ConceptId == "zzqxyw");
    }

    [Fact]
    public void ResolveDetailed_IsDeterministic_SameInputTwice_EqualOrderedSequences()
    {
        var labels = SingleTokenSkillGoldens().Take(3).Select(g => g.PreferredLabel).ToList();

        var first = NewResolver().ResolveDetailed(labels, TestContext.Current.CancellationToken);
        var second = NewResolver().ResolveDetailed(labels, TestContext.Current.CancellationToken);

        // Deterministic ORDINAL ORDER — the ordered sequences must be identical (not just
        // set-equal): same concept-ids in the same positions, same labels.
        first.Select(s => s.ConceptId).ShouldBe(second.Select(s => s.ConceptId),
            "ResolveDetailed är ren över immutable reference-data → identisk ordnad sekvens.");
        first.Select(s => s.Label).ShouldBe(second.Select(s => s.Label));
    }

    [Fact]
    public void ResolveDetailed_ConceptIds_MatchResolveConceptIds_ProvingOneSharedPath()
    {
        // ResolveDetailed and Resolve must agree on WHICH concept-ids resolve from the same
        // input — ResolveDetailed only ADDS the label, it must not change the resolution set
        // (one shared SkillTaxonomyIndex, no second labelled path that could diverge).
        var labels = SingleTokenSkillGoldens().Take(3).Select(g => g.PreferredLabel).ToList();
        var sut = NewResolver();

        var resolveIds = sut.Resolve(labels, TestContext.Current.CancellationToken);
        var detailedIds = NewResolver()
            .ResolveDetailed(labels, TestContext.Current.CancellationToken)
            .Select(s => s.ConceptId)
            .ToHashSet(StringComparer.Ordinal);

        detailedIds.ShouldBe(resolveIds.ToHashSet(StringComparer.Ordinal), ignoreOrder: true,
            "ResolveDetailed:s concept-id-mängd måste vara identisk med Resolve:s — labeln " +
            "läggs till, resolutionen ändras inte (ADR 0079, EN delad index).");
    }

    // ===============================================================
    // 6. Search (ADR 0079 STEG 3 PR-C) — the skill TYPEAHEAD for the editable
    //    skill chips' "add" affordance. UNLIKE Resolve/ResolveDetailed (a
    //    Snowball-lexeme match of a FULL skill name), Search is a case-insensitive
    //    literal SUBSTRING match against every concept's preferred label + synonyms,
    //    deduped per concept-id (best rank kept), ranked PREFIX-before-CONTAINS then
    //    shortest label then ordinal, capped at max. Returns the canonical
    //    PreferredLabel even on a synonym hit. Blank / <2 chars / max<=0 → empty.
    //    Goldens are READ LIVE from the committed asset (NEVER a hardcoded token).
    //    SkillResolver.Search caps at MaxSearchResults=20.
    // ===============================================================

    // A cap so large the membership/rank tests are never excluded by it — those tests assert
    // WHICH concepts match and HOW they rank, not the cap (the cap is tests 6/7 below). With a
    // generous max every match is returned, so a real match can never be cut by the cap.
    private const int UnboundedMax = 1_000_000;

    [Fact]
    public void Search_KnownLabelPrefix_ReturnsThatConceptIdAmongHits()
    {
        // A real PreferredLabel read live from the asset; its first ~3 chars must surface
        // that concept among the hits (prefix → rank 0). Pick a label whose 3-char prefix
        // also clears the <2-char floor and is searchable. Use an unbounded max so the cap
        // (a separate concern) can never be the reason the concept is absent.
        var golden = SearchGoldens().First(g => Prefix(g.PreferredLabel, 3) is { Length: >= 2 });
        var needle = Prefix(golden.PreferredLabel, 3);
        var sut = NewIndex();

        var hits = sut.Search(needle, UnboundedMax);

        hits.ShouldContain(h => h.ConceptId == golden.ConceptId,
            $"Prefixet '{needle}' av labeln '{golden.PreferredLabel}' ska finnas bland träffarna.");
    }

    [Fact]
    public void Search_PrefixBeatsContains_RanksPrefixHitFirst()
    {
        // The rank rule: among hits for one needle, every PREFIX hit (label starts with the
        // needle) must rank before every CONTAINS hit (needle mid-label). Derive a needle
        // LIVE that has at least one of each, then assert the rank ordering holds — never a
        // hardcoded needle.
        var (needle, prefixIds, containsIds) = FindNeedleWithBothPrefixAndContains();
        var sut = NewIndex();

        var hits = sut.Search(needle, UnboundedMax);

        var prefixPositions = hits
            .Select((h, i) => (h, i))
            .Where(x => prefixIds.Contains(x.h.ConceptId))
            .Select(x => x.i)
            .ToList();
        var containsPositions = hits
            .Select((h, i) => (h, i))
            .Where(x => containsIds.Contains(x.h.ConceptId))
            .Select(x => x.i)
            .ToList();

        // The assertion is only meaningful if BOTH groups are actually present in the result.
        prefixPositions.ShouldNotBeEmpty($"Needeln '{needle}' ska ha minst en prefix-träff.");
        containsPositions.ShouldNotBeEmpty($"Needeln '{needle}' ska ha minst en contains-träff.");

        prefixPositions.Max().ShouldBeLessThan(containsPositions.Min(),
            $"För needeln '{needle}' ska varje prefix-träff rankas före varje contains-träff " +
            "(PREFIX-before-CONTAINS, ADR 0079 STEG 3 PR-C).");
    }

    [Fact]
    public void Search_ConceptMatchingViaLabelAndSynonym_AppearsOnceDeduped()
    {
        // A concept whose preferred label AND a synonym both contain the needle must still
        // surface ONCE (deduped per concept-id, best rank kept). Construct the needle LIVE
        // from such a concept in the asset.
        var (golden, needle) = FindConceptWhereLabelAndSynonymShareNeedle();
        var sut = NewIndex();

        var hits = sut.Search(needle, UnboundedMax);

        hits.Count(h => h.ConceptId == golden.ConceptId).ShouldBe(1,
            $"Concept {golden.ConceptId} matchar needeln '{needle}' via både label och synonym, " +
            "men ska deduperas till EN träff.");
    }

    [Fact]
    public void Search_NeedleOnlyInSynonym_ReturnsCanonicalPreferredLabel()
    {
        // A needle that a SYNONYM carries but the preferred label does NOT — the returned
        // Label must be the canonical PreferredLabel, never the synonym (the chip displays
        // the canonical label, CLAUDE.md §5). Derive such a concept LIVE; skip gracefully
        // if the asset has none.
        var found = TryFindConceptWhereNeedleOnlyInSynonym();
        Assert.SkipUnless(found is not null,
            "Assetet saknar ett concept där en synonym-only-needle kan härledas — hoppas " +
            "ärligt (härled, gissa aldrig).");
        var (golden, needle) = found!.Value;
        var sut = NewIndex();

        var hits = sut.Search(needle, UnboundedMax);

        var hit = hits.First(h => h.ConceptId == golden.ConceptId);
        hit.Label.ShouldBe(golden.PreferredLabel,
            $"Träffen kom via en synonym men Label ska vara den kanoniska PreferredLabel " +
            $"'{golden.PreferredLabel}', aldrig synonymen.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("a")]
    [InlineData(" b ")] // trims to 1 char → under the 2-char floor
    public void Search_BlankWhitespaceOrSingleChar_ReturnsEmpty(string query)
    {
        var sut = NewIndex();

        sut.Search(query, 50).ShouldBeEmpty(
            "Blank/whitespace/<2-tecken ska ge tom lista (ingen flooding på 1-tecken).");
    }

    [Fact]
    public void Search_MaxNonPositive_ReturnsEmpty()
    {
        // A real prefix that DOES match many concepts, but max<=0 → empty (the cap guard).
        var golden = SearchGoldens().First(g => Prefix(g.PreferredLabel, 3) is { Length: >= 2 });
        var needle = Prefix(golden.PreferredLabel, 3);
        var sut = NewIndex();

        sut.Search(needle, 0).ShouldBeEmpty("max<=0 ska ge tom lista.");
        sut.Search(needle, -5).ShouldBeEmpty("max<=0 ska ge tom lista.");
    }

    [Fact]
    public void Search_CommonSubstring_IndexCapsAtMax()
    {
        // A very common Swedish letter-bigram matches far more than the cap; the index must
        // return AT MOST `max`. Verify it actually saturates (more raw matches than the cap)
        // so the assertion is meaningful, then assert the cap holds.
        var needle = CommonBigram();
        var sut = NewIndex();

        const int max = 7;
        var hits = sut.Search(needle, max);

        hits.Count.ShouldBeLessThanOrEqualTo(max,
            $"Index.Search ska returnera högst max={max} för den vanliga needeln '{needle}'.");
        // Prove saturation: an uncapped (large-max) search returns strictly more than `max`,
        // so the cap is exercised rather than vacuously satisfied.
        sut.Search(needle, 10_000).Count.ShouldBeGreaterThan(max,
            $"Needeln '{needle}' ska matcha fler än {max} concept så cap-regeln faktiskt prövas.");
    }

    [Fact]
    public void Search_ResolverCapsAtTwenty_ForCommonSubstring()
    {
        // SkillResolver.Search hard-caps at MaxSearchResults=20 regardless of how many
        // concepts a common needle matches.
        var needle = CommonBigram();
        var sut = NewResolver();

        var hits = sut.Search(needle, TestContext.Current.CancellationToken);

        hits.Count.ShouldBeLessThanOrEqualTo(20,
            $"SkillResolver.Search ska kapa vid 20 för den vanliga needeln '{needle}'.");
        // And it saturates the cap (the underlying index has well over 20 matches).
        hits.Count.ShouldBe(20,
            $"Needeln '{needle}' matchar långt fler än 20 concept → exakt cap-träff (20).");
    }

    [Fact]
    public void Search_ResolverYieldsSameConceptIdsAsIndex_AdapterFidelity()
    {
        // SkillResolver.Search is a thin adapter over index.Search(query, 20): for the same
        // query it must return the SAME ordered concept-ids (and labels) the index returns
        // at max=20 — no second search path that could diverge.
        var golden = SearchGoldens().First(g => Prefix(g.PreferredLabel, 3) is { Length: >= 2 });
        var needle = Prefix(golden.PreferredLabel, 3);

        var indexHits = NewIndex().Search(needle, 20);
        var resolverHits = NewResolver().Search(needle, TestContext.Current.CancellationToken);

        resolverHits.Select(r => r.ConceptId).ShouldBe(indexHits.Select(h => h.ConceptId),
            "SkillResolver.Search ska spegla index.Search(query, 20):s concept-id-sekvens exakt.");
        resolverHits.Select(r => r.Label).ShouldBe(indexHits.Select(h => h.Label),
            "Adaptern ska bära index:ets kanoniska labels oförändrade.");
    }

    [Fact]
    public void Search_IsDeterministic_SameQueryTwice_IdenticalOrderedResult()
    {
        var needle = CommonBigram();

        var first = NewIndex().Search(needle, 20);
        var second = NewIndex().Search(needle, 20);

        first.Select(h => h.ConceptId).ShouldBe(second.Select(h => h.ConceptId),
            "Search är ren över immutable reference-data → identisk ordnad concept-id-sekvens.");
        first.Select(h => h.Label).ShouldBe(second.Select(h => h.Label),
            "Identisk ordnad label-sekvens vid upprepad körning.");
    }

    // ===============================================================
    // 7. ResolveLabels (ADR 0079 STEG 3 PR-C) — reverse-lookup, the skill analog of
    //    the occupation taxonomy reverse-lookup (ADR 0043). Given STORED concept-ids,
    //    map each to its canonical PreferredLabel so the saved skill chips render names,
    //    never opaque ids. UNKNOWN ids dropped silently (a stale/removed concept never
    //    crashes the read); blank/whitespace dropped; deduped per concept-id;
    //    deterministic ORDINAL order by ConceptId. The (conceptId, PreferredLabel) pair
    //    is READ LIVE from the committed asset (NEVER a hardcoded concept-id).
    //    SkillResolver.ResolveLabels is the thin adapter (→ ResolvedSkill).
    // ===============================================================

    // An id that cannot exist in the JobTech snapshot — used to assert the silent drop.
    private const string UnknownConceptId = "skill_does_not_exist_xyz";

    [Fact]
    public void ResolveLabels_RealConceptId_ReturnsItsCanonicalPreferredLabel()
    {
        // A real (conceptId, PreferredLabel) pair read live from the asset.
        var golden = SearchGoldens()[0];
        var sut = NewIndex();

        var resolved = sut.ResolveLabels([golden.ConceptId]);

        var match = resolved.ShouldHaveSingleItem();
        match.ConceptId.ShouldBe(golden.ConceptId);
        match.Label.ShouldBe(golden.PreferredLabel,
            $"Concept {golden.ConceptId} ska resolvas till sin kanoniska label " +
            $"'{golden.PreferredLabel}' (ej en opak id, CLAUDE.md §5).");
    }

    [Fact]
    public void ResolveLabels_UnknownConceptId_IsDroppedSilently_NeverThrows()
    {
        var sut = NewIndex();

        var resolved = sut.ResolveLabels([UnknownConceptId]);

        resolved.ShouldBeEmpty(
            "Ett okänt/borttaget concept-id ska droppas tyst (graceful, aldrig krasch).");
    }

    [Fact]
    public void ResolveLabels_MixedKnownAndUnknown_ResolvesOnlyTheKnown()
    {
        var golden = SearchGoldens()[0];
        var sut = NewIndex();

        var resolved = sut.ResolveLabels([UnknownConceptId, golden.ConceptId]);

        resolved.ShouldContain(r => r.ConceptId == golden.ConceptId && r.Label == golden.PreferredLabel);
        resolved.ShouldNotContain(r => r.ConceptId == UnknownConceptId);
    }

    [Fact]
    public void ResolveLabels_DuplicateConceptIds_AreDedupedToOneEntry()
    {
        var golden = SearchGoldens()[0];
        var sut = NewIndex();

        var resolved = sut.ResolveLabels([golden.ConceptId, golden.ConceptId]);

        resolved.Count(r => r.ConceptId == golden.ConceptId).ShouldBe(1,
            "Duplicerade concept-ids ska deduperas till EN post.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ResolveLabels_BlankOrWhitespaceConceptIds_AreDropped_NeverThrows(string blank)
    {
        var sut = NewIndex();

        var resolved = sut.ResolveLabels([blank]);

        resolved.ShouldBeEmpty("Tom/whitespace concept-id ska droppas tyst, aldrig kasta.");
    }

    [Fact]
    public void ResolveLabels_RoundTripsWithSearch_SameConceptResolvesToSameLabel()
    {
        // A concept surfaced by Search("<prefix>") must reverse-lookup via ResolveLabels to
        // the SAME canonical label — one shared LabelByConceptId map, no divergence between
        // the typeahead path and the saved-chip path. Derive the prefix LIVE from the asset.
        var golden = SearchGoldens().First(g => Prefix(g.PreferredLabel, 3) is { Length: >= 2 });
        var needle = Prefix(golden.PreferredLabel, 3);
        var sut = NewIndex();

        var searchHit = sut.Search(needle, UnboundedMax)
            .First(h => h.ConceptId == golden.ConceptId);

        var reversed = sut.ResolveLabels([searchHit.ConceptId]).ShouldHaveSingleItem();

        reversed.ConceptId.ShouldBe(searchHit.ConceptId);
        reversed.Label.ShouldBe(searchHit.Label,
            $"Concept-id:t som Search('{needle}') gav ska reverse-lookup:a till SAMMA label " +
            "(en delad LabelByConceptId-map, ingen divergens).");
    }

    [Fact]
    public void ResolveLabels_IsDeterministic_SameInputTwice_IdenticalOrderedResult()
    {
        // Several real concept-ids in non-ordinal input order → identical ORDINAL-ordered
        // result twice (sorted by ConceptId, deterministic over immutable reference data).
        var ids = SearchGoldens().Take(4).Select(g => g.ConceptId).Reverse().ToList();

        var first = NewIndex().ResolveLabels(ids);
        var second = NewIndex().ResolveLabels(ids);

        first.Select(r => r.ConceptId).ShouldBe(second.Select(r => r.ConceptId),
            "ResolveLabels är ren över immutable reference-data → identisk ordnad sekvens.");
        first.Select(r => r.Label).ShouldBe(second.Select(r => r.Label),
            "Identisk ordnad label-sekvens vid upprepad körning.");
        // And the documented invariant: deterministic ORDINAL order by ConceptId.
        first.Select(r => r.ConceptId)
            .ShouldBe(first.Select(r => r.ConceptId).OrderBy(id => id, StringComparer.Ordinal),
                "Resultatet ska vara ordinal-sorterat på ConceptId.");
    }

    [Fact]
    public void ResolveLabels_Resolver_MatchesIndex_AdapterFidelity()
    {
        // SkillResolver.ResolveLabels is a thin adapter over index.ResolveLabels: for the
        // same ids it must return the SAME ordered concept-ids AND labels (mapped to
        // ResolvedSkill) — no second reverse-lookup path that could diverge.
        var ids = SearchGoldens().Take(4).Select(g => g.ConceptId).Reverse().ToList();

        var indexHits = NewIndex().ResolveLabels(ids);
        var resolverHits = NewResolver().ResolveLabels(ids, TestContext.Current.CancellationToken);

        resolverHits.Select(r => r.ConceptId).ShouldBe(indexHits.Select(h => h.ConceptId),
            "SkillResolver.ResolveLabels ska spegla index.ResolveLabels:s concept-id-sekvens exakt.");
        resolverHits.Select(r => r.Label).ShouldBe(indexHits.Select(h => h.Label),
            "Adaptern ska bära index:ets kanoniska labels oförändrade.");
    }

    // ---------------------------------------------------------------
    // Search-golden derivation — read the committed asset live WITH synonyms (the
    // typeahead matches preferred label + synonyms, so the Resolve goldens above,
    // which read labels only, are insufficient). Every needle is constructed from a
    // real surface form; nothing is hardcoded.
    // ---------------------------------------------------------------

    private sealed record SearchConcept(string ConceptId, string PreferredLabel, IReadOnlyList<string> Synonyms);

    private static string Prefix(string label, int n)
    {
        var t = label.Trim();
        return t.Length <= n ? t : t[..n];
    }

    // Concepts usable as search goldens: a non-blank, letter-only-ish prefix is derivable.
    private static List<SearchConcept> SearchGoldens()
    {
        var goldens = ReadSearchConcepts()
            .Where(c => !string.IsNullOrWhiteSpace(c.PreferredLabel))
            .Where(c => c.PreferredLabel.Trim().Length >= 4)
            .OrderBy(c => c.ConceptId, StringComparer.Ordinal)
            .ToList();
        goldens.ShouldNotBeEmpty(
            $"Inga search-goldens kunde härledas ur {SkillTaxonomyResource} (härled, gissa aldrig).");
        return goldens;
    }

    // A needle that is a PREFIX of at least one concept's label AND a mid-label CONTAINS of
    // at least one OTHER concept's label — so the prefix-before-contains rank rule is testable.
    private static (string Needle, HashSet<string> PrefixIds, HashSet<string> ContainsIds)
        FindNeedleWithBothPrefixAndContains()
    {
        var concepts = ReadSearchConcepts()
            .Where(c => !string.IsNullOrWhiteSpace(c.PreferredLabel))
            .ToList();

        // Candidate needles: 3-char lower-cased prefixes of real labels.
        foreach (var c in concepts.OrderBy(c => c.ConceptId, StringComparer.Ordinal))
        {
            var needle = Prefix(c.PreferredLabel, 3).ToLowerInvariant();
            if (needle.Length < 2)
                continue;

            var prefixIds = new HashSet<string>(StringComparer.Ordinal);
            var containsIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var other in concepts)
            {
                var idx = other.PreferredLabel.Trim().ToLowerInvariant()
                    .IndexOf(needle, StringComparison.Ordinal);
                if (idx == 0)
                    prefixIds.Add(other.ConceptId);
                else if (idx > 0)
                    containsIds.Add(other.ConceptId);
            }

            // Need genuine separation: a concept that is ONLY a contains-hit (not also a
            // prefix-hit on another form) so the rank comparison is unambiguous.
            containsIds.ExceptWith(prefixIds);
            if (prefixIds.Count > 0 && containsIds.Count > 0)
                return (needle, prefixIds, containsIds);
        }

        throw new InvalidOperationException(
            "Kunde inte härleda en needle med både prefix- och contains-träff ur " +
            $"{SkillTaxonomyResource} — assetens form har ändrats.");
    }

    // A concept whose preferred label AND one of its synonyms both contain the same needle.
    private static (SearchConcept Golden, string Needle) FindConceptWhereLabelAndSynonymShareNeedle()
    {
        foreach (var c in ReadSearchConcepts().OrderBy(c => c.ConceptId, StringComparer.Ordinal))
        {
            var label = c.PreferredLabel.Trim().ToLowerInvariant();
            if (label.Length < 2)
                continue;
            foreach (var syn in c.Synonyms)
            {
                var s = (syn ?? string.Empty).Trim().ToLowerInvariant();
                if (s.Length < 2)
                    continue;
                // Shared needle = the longest common >=2-char prefix of label and synonym
                // is overkill; the synonym is typically a substring of the label (e.g. "Bower"
                // vs "Bower, pakethanterare"). Use the first 2 chars of the synonym when both
                // contain it.
                var needle = s.Length >= 2 ? s[..Math.Min(s.Length, 3)] : s;
                if (label.Contains(needle, StringComparison.Ordinal)
                    && s.Contains(needle, StringComparison.Ordinal)
                    && needle.Length >= 2)
                {
                    return (c, needle);
                }
            }
        }

        throw new InvalidOperationException(
            "Kunde inte härleda ett concept där label och synonym delar en needle ur " +
            $"{SkillTaxonomyResource}.");
    }

    // A concept where a needle exists in a SYNONYM but NOT in the preferred label, AND that
    // needle does not collide with the same concept via another form — so the canonical-label
    // assertion is clean. Returns null if the asset has none (test skips gracefully).
    private static (SearchConcept Golden, string Needle)? TryFindConceptWhereNeedleOnlyInSynonym()
    {
        foreach (var c in ReadSearchConcepts().OrderBy(c => c.ConceptId, StringComparer.Ordinal))
        {
            var label = c.PreferredLabel.Trim().ToLowerInvariant();
            foreach (var syn in c.Synonyms)
            {
                var s = (syn ?? string.Empty).Trim().ToLowerInvariant();
                if (s.Length < 2)
                    continue;
                // The whole synonym is a needle the synonym carries; if the label does NOT
                // contain it, a search on it hits this concept ONLY via the synonym → the
                // returned Label must be the canonical PreferredLabel.
                if (!label.Contains(s, StringComparison.Ordinal))
                    return (c, s);
            }
        }

        return null;
    }

    // A very common Swedish letter-bigram guaranteed to match far more than any test cap.
    // Verified live to exceed the caps by the saturation assertions; "in" is ubiquitous in
    // Swedish skill labels (utbildning, hantering, ...).
    private static string CommonBigram() => "in";

    // Number of concepts that carry each trimmed literal (case-insensitive) across
    // preferred label + synonyms — used to derive single-owner exact literals live
    // (a literal with count == 1 resolves to exactly one concept). O(n) one pass.
    private static Dictionary<string, int> LiteralOwnerCounts(IEnumerable<SearchConcept> concepts)
    {
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in concepts)
        {
            var literals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(c.PreferredLabel))
                literals.Add(c.PreferredLabel.Trim());
            foreach (var s in c.Synonyms)
                if (!string.IsNullOrWhiteSpace(s))
                    literals.Add(s.Trim());
            foreach (var lit in literals)
                count[lit] = count.GetValueOrDefault(lit) + 1;
        }
        return count;
    }

    // True when the concept carries the literal (trimmed, case-insensitive) as its
    // preferred label OR one of its synonyms — the exact-label-match oracle (#253).
    private static bool LiteralMatches(SearchConcept c, string literal)
    {
        if (string.Equals(c.PreferredLabel?.Trim(), literal, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var syn in c.Synonyms)
            if (string.Equals(syn?.Trim(), literal, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static List<SearchConcept> ReadSearchConcepts()
    {
        var asm = typeof(LocalTextAnalyzer).Assembly; // Infrastructure assembly
        using var stream = asm.GetManifestResourceStream(SkillTaxonomyResource);
        stream.ShouldNotBeNull(
            $"Skill-taxonomi-resursen '{SkillTaxonomyResource}' ska vara en " +
            "<EmbeddedResource> i Infrastructure-assemblyn (csproj LogicalName).");

        using var doc = JsonDocument.Parse(stream!);
        var skills = doc.RootElement.GetProperty("skills");
        var list = new List<SearchConcept>(skills.GetArrayLength());
        foreach (var el in skills.EnumerateArray())
        {
            var synonyms = new List<string>();
            if (el.TryGetProperty("synonyms", out var syns) && syns.ValueKind == JsonValueKind.Array)
                foreach (var s in syns.EnumerateArray())
                    if (s.GetString() is { } str)
                        synonyms.Add(str);

            list.Add(new SearchConcept(
                el.GetProperty("conceptId").GetString()!,
                el.GetProperty("preferredLabel").GetString()!,
                synonyms));
        }
        return list;
    }

    // ---------------------------------------------------------------
    // Golden derivation — read the committed asset live, pick concepts whose
    // preferredLabel is a single distinctive token lexemizing to ONE lexeme that
    // is UNIQUE to one concept (so resolution is unambiguous). Parity
    // JobAdKeywordExtractorIntegrationTests' provenance helpers.
    // ---------------------------------------------------------------

    private sealed record SkillGolden(string ConceptId, string PreferredLabel);

    private static SkillGolden FirstSingleTokenSkillGolden() => SingleTokenSkillGoldens()[0];

    private static List<SkillGolden> SingleTokenSkillGoldens()
    {
        var concepts = ReadSkillConcepts();
        var candidates = new List<(SkillGolden Golden, string Lexeme)>();
        var lexemeOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var c in concepts)
        {
            var label = c.PreferredLabel?.Trim() ?? string.Empty;
            if (label.Length is < 7 or > 14)
                continue;
            if (label.Any(ch => !char.IsLetter(ch)))
                continue;

            var lex = SingleLexeme(label);
            if (lex is null)
                continue;

            if (!lexemeOwners.TryGetValue(lex, out var owners))
                lexemeOwners[lex] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(c.ConceptId);
            candidates.Add((new SkillGolden(c.ConceptId, label), lex));
        }

        var goldens = candidates
            .Where(x => lexemeOwners[x.Lexeme].Count == 1)
            .Select(x => x.Golden)
            .OrderBy(g => g.ConceptId, StringComparer.Ordinal)
            .ToList();

        goldens.ShouldNotBeEmpty(
            "Inga single-token skill-goldens kunde härledas ur " +
            $"{SkillTaxonomyResource} — assetens form har ändrats (härled, gissa aldrig).");
        return goldens;
    }

    private static string? SingleLexeme(string text)
    {
        var lexemes = Analyzer.ToLexemes(text, TextLanguage.Swedish);
        return lexemes.Count == 1 ? lexemes[0] : null;
    }

    private static string DefiniteForm(string label) =>
        label.EndsWith('a') ? label + 'n' : label + "en";

    private static List<SkillConceptJson> ReadSkillConcepts()
    {
        var asm = typeof(LocalTextAnalyzer).Assembly; // Infrastructure assembly
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
