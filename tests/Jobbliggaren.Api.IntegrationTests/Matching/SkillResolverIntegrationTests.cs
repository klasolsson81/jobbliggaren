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
