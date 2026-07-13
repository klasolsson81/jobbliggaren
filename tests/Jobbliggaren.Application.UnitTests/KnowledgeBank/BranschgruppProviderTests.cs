using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b 8b.4a (ADR 0107) — the committed branschgrupp asset (<c>ssyk-branschgrupp.v1.json</c>)
/// loads through the real <see cref="BranschgruppProvider"/> (which runs the FULL cross-asset
/// validation against the real parsing lexicon at construction) and agrees with the two assets it
/// sits between: the parsing lexicon (section identity) and the taxonomy snapshot (occupation
/// fields). The rule table is VERSIONED DATA (CLAUDE.md §5), never C# literals.
/// <para>
/// The three load-bearing tests here are the COMPLETENESS pin (§4 — all 21 fields), the
/// SUBSET pin (§2 — the asset may only name sections the lexicon owns) and the ROUND-TRIP pin
/// (§3 — every heading resolves back to its own sectionId). Together they are what stops this
/// feature from becoming the vacuous filter it would otherwise quietly decay into.
/// </para>
/// </summary>
public class BranschgruppProviderTests
{
    private static CvParsingLexiconProvider RealLexicon() =>
        new(CvParsingLexiconLoader.Load());

    private static BranschgruppCatalog LoadCatalog() =>
        new BranschgruppProvider(RealLexicon()).GetCatalog();

    [Fact]
    public void GetCatalog_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var catalog = LoadCatalog();

        catalog.ShouldNotBeNull();
        catalog.Version.ShouldBe("1.0");
        catalog.RulesById.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(["it", "ovriga", "skola", "vard"]);
    }

    // ───────────────────────────────────────────────────────────────────
    // §1 — Klas' 21→4 ruling (2026-07-13), pinned as DATA
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    // IT/tech ← Data/IT
    [InlineData("apaJ_2ja_LuF", "it")]
    // Vård och omsorg ← Hälso- och sjukvård + Yrken med social inriktning (C3(i) ACCEPTED —
    // kurator IS a skyddad yrkestitel and the vård ruleset genuinely fits).
    [InlineData("NYW6_mP6_vwf", "vard")]
    [InlineData("GazW_2TU_kJw", "vard")]
    // Skola ← Pedagogik
    [InlineData("MVqp_eS8_kDZ", "skola")]
    // C3(ii) DECLINED — "Yrken med teknisk inriktning" stays in Övriga. This is the ruling most
    // likely to be "helpfully" reverted by someone chasing coverage, so it is pinned by id:
    // suggesting a byggnadsingenjör add a GitHub link and an AWS certificate is the machine-slop
    // impression the civic tone exists to prevent. Coverage bought by mis-suggesting is negative.
    [InlineData("6Hq3_tKo_V57", "ovriga")]
    public void BranschgruppByOccupationField_ShouldMatchKlasRuling_ForTheDecidedFields(
        string occupationFieldConceptId, string expectedBranschgrupp)
    {
        LoadCatalog().BranschgruppByOccupationField[occupationFieldConceptId]
            .ShouldBe(expectedBranschgrupp);
    }

    // ───────────────────────────────────────────────────────────────────
    // §2 + §3 — the cross-asset pins against the parsing lexicon
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EverySectionTheAssetNames_ShouldExistInTheLexicon_WhenLoaded()
    {
        // The no-fork constraint: the LEXICON owns section identity, the ASSET owns
        // recommendation. An asset that names a section the lexicon has never heard of has forked
        // the vocabulary — the exact fork PR-1 existed to make impossible.
        var lexicon = RealLexicon();
        var catalog = LoadCatalog();

        var named = catalog.RulesById.Values
            .SelectMany(r => r.StandardSections.Concat(r.SuggestedSections).Select(s => s.SectionId)
                .Concat(r.SuppressedSectionIds))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        named.ShouldNotBeEmpty("vacuity guard — an empty asset would pass every assertion below.");
        named.ShouldAllBe(id => lexicon.FreeSectionIds.Contains(id));
    }

    [Fact]
    public void EveryHeadingTheAssetWrites_ShouldRoundTripBackToItsOwnSectionId_WhenLoaded()
    {
        // THE test this feature stands on. The heading is written INTO the user's CV when she
        // accepts a suggestion. If the segmenter cannot resolve it on the next import, that
        // section's body is swallowed by the preceding section — the live #815 bug PR-1 fixed for
        // legitimation/korkort. Suggesting a heading the parser cannot see would be shipping that
        // bug back deliberately. So: suggestions ⊆ what the lexicon can recognise, proven here
        // against the REAL lexicon, not a fixture.
        var lexicon = RealLexicon();
        var catalog = LoadCatalog();

        var sections = catalog.RulesById.Values
            .SelectMany(r => r.StandardSections.Concat(r.SuggestedSections))
            .ToList();

        sections.ShouldNotBeEmpty("vacuity guard.");

        foreach (var section in sections)
        {
            lexicon.TryResolveFreeSectionId(section.Heading).ShouldBe(section.SectionId,
                $"rubriken '{section.Heading}' måste resolva tillbaka till '{section.SectionId}' — " +
                "annars sväljs sektionens text av föregående sektion vid nästa import (#815).");
        }
    }

    [Fact]
    public void Constructing_ShouldThrow_WhenTheAssetNamesASectionTheLexiconDoesNotOwn()
    {
        // Drive a SYNTHETIC drifted catalog through the REAL check. Without this, the two tests
        // above only ever see the good asset and prove nothing about the guard itself — they
        // would stay green if the guard were deleted.
        var drifted = new BranschgruppCatalog(
            "1.0",
            new Dictionary<string, string> { ["apaJ_2ja_LuF"] = "it" },
            new Dictionary<string, BranschgruppRules>
            {
                ["it"] = new("it", "Vanligt inom data och IT",
                    [new SectionRecommendation("meritering", "Meriteringsnivå")], [], []),
            });

        var ex = Should.Throw<InvalidOperationException>(
            () => BranschgruppProvider.ValidateAgainstLexicon(drifted, RealLexicon()));

        ex.Message.ShouldContain("meritering");
    }

    [Fact]
    public void Constructing_ShouldThrow_WhenAHeadingResolvesToADifferentSectionThanItIsFiledUnder()
    {
        // The subtler half, and the one a reviewer would wave through: "Kurser och certifikat" is
        // a REAL heading the lexicon knows — but it leads with "kurser", so it resolves to
        // `kurser`, not `certifikat`. Filing it under `certifikat` would mean the panel says
        // "already have it" for the wrong section, forever.
        var drifted = new BranschgruppCatalog(
            "1.0",
            new Dictionary<string, string> { ["apaJ_2ja_LuF"] = "it" },
            new Dictionary<string, BranschgruppRules>
            {
                ["it"] = new("it", "Vanligt inom data och IT",
                    [], [new SectionRecommendation("certifikat", "Kurser och certifikat")], []),
            });

        var ex = Should.Throw<InvalidOperationException>(
            () => BranschgruppProvider.ValidateAgainstLexicon(drifted, RealLexicon()));

        ex.Message.ShouldContain("kurser");
    }

    [Fact]
    public void Constructing_ShouldThrow_WhenASuppressedSectionIsNotOwnedByTheLexicon()
    {
        // A suppression that cannot hit anything is a silent no-op — and almost certainly a typo.
        var drifted = new BranschgruppCatalog(
            "1.0",
            new Dictionary<string, string> { ["NYW6_mP6_vwf"] = "vard" },
            new Dictionary<string, BranschgruppRules>
            {
                ["vard"] = new("vard", "Vanligt inom vård och omsorg", [], [], ["projekkt"]),
            });

        Should.Throw<InvalidOperationException>(
            () => BranschgruppProvider.ValidateAgainstLexicon(drifted, RealLexicon()));
    }

    // ───────────────────────────────────────────────────────────────────
    // §4 — the completeness pin against the taxonomy snapshot (the R7 / #805-3 antidote)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TheAsset_ShouldMapExactlyTheOccupationFieldsTheTaxonomySnapshotHas_BothDirections()
    {
        // This is why the asset lists all 21 fields explicitly instead of 4-plus-a-default. A
        // "3 entries + default" table cannot distinguish "this field means Övriga" from "this
        // field is unknown to us" — so a JobTech snapshot bump that adds or replaces a field would
        // send every affected CV to Övriga SILENTLY, with every test still green. That is the
        // vacuous-filter failure mode (#805-3, JobAd.DeletedAt). Set equality in BOTH directions
        // turns it into a red test instead.
        //
        // Deliberately NOT asserted: the labels. The mapping is keyed on conceptId, so an upstream
        // label rewording is harmless — pinning it would manufacture a false alarm.
        var snapshotFieldIds = TaxonomySnapshotSeeder.LoadSnapshot().OccupationFields
            .Select(f => f.ConceptId)
            .ToHashSet(StringComparer.Ordinal);

        var assetFieldIds = LoadCatalog().BranschgruppByOccupationField.Keys
            .ToHashSet(StringComparer.Ordinal);

        snapshotFieldIds.Count.ShouldBe(21, "taxonomy-snapshot.json ska bära 21 yrkesområden.");

        assetFieldIds.Except(snapshotFieldIds).ShouldBeEmpty(
            "ssyk-branschgrupp-assetet mappar ett yrkesområde som taxonomi-snapshoten inte har " +
            "(död nyckel → den branschgruppen kan aldrig träffas).");

        snapshotFieldIds.Except(assetFieldIds).ShouldBeEmpty(
            "taxonomi-snapshoten har ett yrkesområde som assetet inte mappar (omappat område " +
            "faller TYST till Övriga — det är precis det vakuösa filtret assetet finns för att " +
            "förhindra; lägg till det explicit, även om svaret är 'ovriga').");
    }

    [Fact]
    public void SeventeenOccupationFields_ShouldMapToOvriga_WhenLoaded()
    {
        // The measured reality (93 469 ads): Övriga is 62.1 % of users, not an edge case. Pinning
        // the count keeps anyone from quietly reassigning a field without a Klas ruling.
        var catalog = LoadCatalog();

        catalog.BranschgruppByOccupationField.Values
            .Count(g => g == BranschgruppCatalog.Fallback)
            .ShouldBe(17);
    }

    // ───────────────────────────────────────────────────────────────────
    // §5 — the product rulings, pinned
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Referenser_ShouldNeverBeSuggested_ByAnyBranschgrupp()
    {
        // Klas ruling 2026-07-13: a section whose only sanctioned content is "Lämnas på begäran"
        // is close to worthless, and modern Swedish CV convention argues against it. `referenser`
        // remains a RECOGNISED section (a CV that has one keeps it — rule (a)); it is simply never
        // OFFERED. That distinction is the whole point, so it is pinned rather than assumed.
        var catalog = LoadCatalog();

        var offered = catalog.RulesById.Values
            .SelectMany(r => r.StandardSections.Concat(r.SuggestedSections))
            .Select(s => s.SectionId)
            .ToList();

        offered.ShouldNotContain("referenser");
        RealLexicon().FreeSectionIds.ShouldContain("referenser",
            "…men den ska fortfarande KÄNNAS IGEN — filen vinner alltid (regel (a)).");
    }

    [Fact]
    public void Vard_ShouldSuppressProjekt_AndOfferLegitimationAsStandard()
    {
        // Handoff §7, the vård row: Legitimation och intyg is the EXTRA STANDARD section, and
        // Projekt is explicitly not offered.
        var vard = LoadCatalog().RulesFor("vard");

        vard.StandardSections.Select(s => s.SectionId).ShouldBe(["legitimation"]);
        vard.SuppressedSectionIds.ShouldContain("projekt");
        vard.SuggestedSections.Select(s => s.SectionId).ShouldBe(["kurser", "korkort"]);
    }

    [Fact]
    public void Ovriga_ShouldBeAFirstClassRow_WithItsOwnSuggestions()
    {
        // Övriga is the 62.1 % majority experience. An empty rule-table here would make the
        // feature look alive and be dead for most users — the honest failure this asset is
        // designed to prevent.
        var ovriga = LoadCatalog().RulesFor(BranschgruppCatalog.Fallback);

        ovriga.SuggestedSections.ShouldNotBeEmpty();
        ovriga.SuggestedSections.Select(s => s.SectionId).ShouldBe(["kurser", "korkort", "ideellt"]);
        ovriga.Rationale.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EveryBranschgrupp_ShouldCarryARationale_WhenLoaded()
    {
        // The badge copy is KB-sourced (parity ProposedChange.Rationale) — never prose the engine
        // synthesised at render time (§5).
        LoadCatalog().RulesById.Values.ShouldAllBe(r => !string.IsNullOrWhiteSpace(r.Rationale));
    }
}
