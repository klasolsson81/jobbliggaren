using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// #2198 (ADR 0180) — the picker's search rule as a pure function. Every label and id below is
/// real v30 data, taken from taxonomy-snapshot.json and joined through
/// occupation-name-to-ssyk-level-4.v30.json on 2026-10-23 (name concept id → group id):
/// Projektledare, IT z5AM_ayf_WcL → DJh5_yyF_hEM; Projektledare, el 1jm3_XFQ_TdJ and
/// Projektledare, telekom T55e_efV_Eqh → nDaB_vdy_eAy; Projektledare, bygg och anläggning
/// pTTz_5DC_itW → thZP_oR7_WrY; Backend-utvecklare 7wdX_4rv_33z, Frontend-utvecklare
/// GDHs_eoz_uKx and Applikationsutvecklare CZkP_hCz_KM8 → DJh5_yyF_hEM; UX-designer iwJU_gW4_SxT → UxT1_tPF_Kbg; Rektor hr5f_dci_NcD →
/// z8di_AqX_GBr; Grillkock snabbmat FntA_2Z4_8zD → a8gg_ktU_w8V; Kock storhushåll Vg3d_Azf_p5Y →
/// BStc_SJh_DKG; Anestesisjuksköterska 4qYQ_tWb_QjD → nrVt_xUL_KdQ; Sjuksköterska, grundutbildad
/// bXNH_MNX_dUR → Z8ci_bBE_tmx; Elingenjör kx3b_92W_PuD →
/// nDaB_vdy_eAy; Elingenjör, fartyg/Fartygseltekniker YEm9_pLR_XoT → Rsoy_sS6_6c4; Florist
/// EuTa_edS_Add → YKL2_FCB_1yr; Åklagare xrNb_bgJ_bdD → 6aak_8eh_iwT.
/// </summary>
public class OccupationSearchRuleTests
{
    private const string Mjukvaru = "DJh5_yyF_hEM";
    private const string Elektroteknik = "nDaB_vdy_eAy";
    private const string Bygg = "thZP_oR7_WrY";
    private const string OvrigaIt = "UxT1_tPF_Kbg";
    private const string SkolChefer = "z8di_AqX_GBr";
    private const string Pizzabagare = "a8gg_ktU_w8V";
    private const string Kockar = "BStc_SJh_DKG";
    private const string Anestesi = "nrVt_xUL_KdQ";
    private const string Grundutbildade = "Z8ci_bBE_tmx";
    private const string Maskinbefal = "Rsoy_sS6_6c4";
    private const string Butikssaljare = "YKL2_FCB_1yr";
    private const string Aklagare = "6aak_8eh_iwT";

    private static readonly (string Id, string Label)[] Groups =
    [
        (Mjukvaru, "Mjukvaru- och systemutvecklare m.fl."),
        (Elektroteknik, "Ingenjörer och tekniker inom elektroteknik"),
        (Bygg, "Ingenjörer och tekniker inom bygg och anläggning"),
        (OvrigaIt, "Övriga IT-specialister"),
        (SkolChefer, "Chefer inom grund- och gymnasieskola samt vuxenutbildning"),
        (Pizzabagare, "Pizzabagare m.fl."),
        (Kockar, "Kockar och kallskänkor"),
        (Anestesi, "Anestesisjuksköterskor"),
        (Grundutbildade, "Grundutbildade sjuksköterskor"),
        (Maskinbefal, "Maskinbefäl"),
        (Butikssaljare, "Butikssäljare, fackhandel"),
        (Aklagare, "Åklagare"),
    ];

    private static readonly (string Name, string GroupId)[] Names =
    [
        ("Projektledare, IT", Mjukvaru),
        ("Projektledare, el", Elektroteknik),
        ("Projektledare, telekom", Elektroteknik),
        ("Projektledare, bygg och anläggning", Bygg),
        ("Frontend-utvecklare", Mjukvaru),
        ("Applikationsutvecklare", Mjukvaru),
        ("Backend-utvecklare", Mjukvaru),
        ("UX-designer", OvrigaIt),
        ("Rektor", SkolChefer),
        ("Grillkock snabbmat", Pizzabagare),
        ("Kock storhushåll", Kockar),
        ("Anestesisjuksköterska", Anestesi),
        ("Sjuksköterska, grundutbildad", Grundutbildade),
        ("Elingenjör", Elektroteknik),
        ("Elingenjör, fartyg/Fartygseltekniker", Maskinbefal),
        ("Florist", Butikssaljare),
        ("Åklagare", Aklagare),
    ];

    private static readonly OccupationSearchRule.PreparedIndex Index = OccupationSearchRule.Prepare(Contents(Names, Groups));

    private static OccupationNameIndexContents Contents(
        IEnumerable<(string Name, string GroupId)> names, IEnumerable<(string Id, string Label)> groups)
    {
        var groupList = groups.Select(g => new OccupationGroupEntry(g.Id, g.Label)).ToList();
        var labelById = groupList.ToDictionary(g => g.ConceptId, g => g.Label, StringComparer.Ordinal);
        return new OccupationNameIndexContents(
            names.Select(n => new OccupationNameEntry(n.Name, n.GroupId, labelById[n.GroupId])).ToList(),
            groupList);
    }

    private static OccupationSearchResult Search(string query) => OccupationSearchRule.Search(Index, query);

    [Fact]
    public void Search_WordAtATokensStart_SelectsItsGroupWithTheNameAsEvidence()
    {
        var result = Search("frontend");

        var row = result.Rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe(OccupationSearchRowKind.Group);
        row.GroupConceptId.ShouldBe(Mjukvaru);
        row.MatchedNames.ShouldBe(["Frontend-utvecklare"]);
        result.TotalRows.ShouldBe(1);
    }

    [Fact]
    public void Search_SynonymsInOneGroup_GiveOneRowCarryingEveryNameStartArmFirstThenCollation()
    {
        // The fixture lists Frontend, Applikations-, Backend-: the order below is the rule's, not the input's.
        var row = Search("utvecklare").Rows.ShouldHaveSingleItem();

        row.GroupConceptId.ShouldBe(Mjukvaru);
        row.MatchedNames.ShouldBe(["Backend-utvecklare", "Frontend-utvecklare", "Applikationsutvecklare"]);
        row.LabelHit.ShouldBeTrue();
    }

    [Fact]
    public void Search_AWordAboveTheFloorInsideAToken_NeverHits()
    {
        // sjuk starts Sjuksköterska and sjuksköterskor, but sits inside Anestesisjuksköterska: the end arm
        // admits a compound's head, never its middle.
        var rows = Search("sjuk").Rows;

        rows.Select(r => r.GroupConceptId).ShouldBe([Grundutbildade]);
    }

    [Fact]
    public void Search_DecomposedQuery_IsNormalisedBeforeMatching()
    {
        // "A" followed by U+030A COMBINING RING ABOVE is how a pasted Å can arrive.
        var row = Search("Åklagare").Rows.ShouldHaveSingleItem();

        row.GroupConceptId.ShouldBe(Aklagare);
        row.ExactMatch.ShouldBeTrue();
    }

    [Fact]
    public void Search_DiacriticsAreNotFolded()
    {
        Search("aklagare").Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Search_CompoundHeadAtTheFloor_ReachesTheCompound()
    {
        var rows = Search("kock").Rows;

        rows.Select(r => r.GroupConceptId).ShouldBe([Kockar, Pizzabagare]);
        rows[1].MatchedNames.ShouldBe(["Grillkock snabbmat"]);
    }

    [Fact]
    public void Search_OneLetterBelowTheFloor_NeverReachesACompoundsHead()
    {
        Search("ock").Rows.ShouldBeEmpty();
        OccupationSearchRule.SuffixFloor.ShouldBe(4);
    }

    [Fact]
    public void Search_Ux_FindsUxDesignerAndNeverVuxenutbildning()
    {
        var row = Search("UX").Rows.ShouldHaveSingleItem();

        row.GroupConceptId.ShouldBe(OvrigaIt);
        row.MatchedNames.ShouldBe(["UX-designer"]);
    }

    [Fact]
    public void Search_HeadWordOverSeveralGroups_IsOneWordRowWhoseVariantsNameTheirGroups()
    {
        var row = Search("projektledare").Rows.ShouldHaveSingleItem();

        row.Kind.ShouldBe(OccupationSearchRowKind.Word);
        row.HeadWord.ShouldBe("Projektledare");
        row.GroupConceptId.ShouldBeNull();
        // Swedish collation, not Ordinal: Ordinal would put "IT" before "bygg".
        row.Variants.Select(v => (v.NameLabel, v.GroupConceptId)).ShouldBe(
        [
            ("Projektledare, bygg och anläggning", Bygg),
            ("Projektledare, el", Elektroteknik),
            ("Projektledare, IT", Mjukvaru),
            ("Projektledare, telekom", Elektroteknik),
        ]);
        row.ExactMatch.ShouldBeFalse();
    }

    [Fact]
    public void Search_EveryQueryWord_MustHit()
    {
        var row = Search("projektledare it").Rows.ShouldHaveSingleItem();

        row.Kind.ShouldBe(OccupationSearchRowKind.Group);
        row.GroupConceptId.ShouldBe(Mjukvaru);
        row.MatchedNames.ShouldBe(["Projektledare, IT"]);
    }

    [Fact]
    public void Search_ANameTypedInFull_FlagsItsWordRowExactAndListsItFirst()
    {
        var rows = Search("Elingenjör").Rows;

        rows[0].Kind.ShouldBe(OccupationSearchRowKind.Word);
        rows[0].ExactMatch.ShouldBeTrue();
        rows[0].Variants[0].NameLabel.ShouldBe("Elingenjör");
        rows[0].Variants[0].GroupConceptId.ShouldBe(Elektroteknik);
    }

    [Fact]
    public void Search_WholeLabel_IgnoresCaseAndRepeatedWhitespace()
    {
        var row = Search("  projektledare,   IT ").Rows[0];

        row.ExactMatch.ShouldBeTrue();
        row.GroupConceptId.ShouldBe(Mjukvaru);
    }

    [Fact]
    public void Search_GroupLabelTypedInFull_IsALabelOnlyExactRow()
    {
        var row = Search("Maskinbefäl").Rows.ShouldHaveSingleItem();

        row.GroupConceptId.ShouldBe(Maskinbefal);
        row.LabelHit.ShouldBeTrue();
        row.MatchedNames.ShouldBeEmpty();
        row.ExactMatch.ShouldBeTrue();
    }

    [Fact]
    public void Search_NameAndLabelOfOneGroup_MergeIntoOneRow()
    {
        var row = Search("anläggning").Rows.ShouldHaveSingleItem();

        // Reached both by the name and by the label: one row, the label hit flagged.
        row.GroupConceptId.ShouldBe(Bygg);
        row.LabelHit.ShouldBeTrue();
        row.MatchedNames.ShouldBe(["Projektledare, bygg och anläggning"]);
    }

    [Fact]
    public void Search_PrefixRowsComeBeforeRowsReachedOnlyThroughTheCompoundHead()
    {
        // Collation alone would put "Anestesisjuksköterskor" first; it is reached only through the
        // compound's head, so the row whose name starts with the word comes first.
        var rows = Search("sjuksköterska").Rows;

        rows.Select(r => r.GroupConceptId).ShouldBe([Grundutbildade, Anestesi]);
    }

    [Fact]
    public void LabelStopwords_AreExactlyTheFiveMeasuredWords()
    {
        OccupationSearchRule.LabelStopwords.Order(StringComparer.Ordinal)
            .ShouldBe(["fl", "inom", "m", "och", "samt"]);
    }

    [Fact]
    public void Search_StopwordInALabel_NeverMatchesButALoneStopwordQueryStillSearches()
    {
        // "Pizzabagare m.fl." carries the token fl, which is stripped; Florist starts with fl.
        var rows = Search("fl").Rows;

        rows.Select(r => r.GroupConceptId).ShouldBe([Butikssaljare]);
    }

    [Fact]
    public void Search_StopwordBesideAnotherWord_IsDroppedFromTheQuery()
    {
        var row = Search("kockar och").Rows.ShouldHaveSingleItem();

        row.GroupConceptId.ShouldBe(Kockar);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("k")]
    [InlineData("a b c")]
    public void Search_NoTokenOfTwoCharacters_GivesNoRows(string query)
    {
        var result = Search(query);

        result.Rows.ShouldBeEmpty();
        result.TotalRows.ShouldBe(0);
    }

    [Fact]
    public void Collation_IsSwedish()
    {
        string[] unordered = ["Ö", "Ä", "Z", "Å"];

        var ordered = unordered.Order(OccupationSearchRule.Collation).ToArray();

        ordered.ShouldBe(["Z", "Å", "Ä", "Ö"]);
    }

    [Fact]
    public void Search_MoreRowsThanTheCap_ReturnsTheCapAndTheExactTotal()
    {
        // Inserted in reverse, so a cap taken before the ordering would keep the wrong fifty.
        var groups = Enumerable.Range(0, OccupationSearchRule.MaxRows + 7)
            .Reverse()
            .Select(i => ($"g{i:D3}", $"Testgrupp {i:D3}"))
            .ToList();
        var index = OccupationSearchRule.Prepare(Contents([], groups));

        var result = OccupationSearchRule.Search(index, "testgrupp");

        result.Rows.Count.ShouldBe(OccupationSearchRule.MaxRows);
        result.TotalRows.ShouldBe(OccupationSearchRule.MaxRows + 7);
        result.Rows[0].GroupLabel.ShouldBe("Testgrupp 000");
        result.Rows[^1].GroupLabel.ShouldBe($"Testgrupp {OccupationSearchRule.MaxRows - 1:D3}");
    }

    [Fact]
    public void Search_OverTheWholeV30Index_ReachesTheCapForChefAndKeepsProjektledareOneWordRow()
    {
        var index = OccupationSearchRule.Prepare(V30.Load());

        var chef = OccupationSearchRule.Search(index, "chef");
        chef.Rows.Count.ShouldBe(OccupationSearchRule.MaxRows);
        chef.TotalRows.ShouldBeGreaterThan(OccupationSearchRule.MaxRows);

        // it is two letters, so every row is a start-arm row: the order is Swedish collation alone
        // (Ordinal would put "IT-chefer" before "Ingenjörer …").
        var it = OccupationSearchRule.Search(index, "it");
        var labels = it.Rows.Select(r => r.GroupLabel ?? r.HeadWord!).ToList();
        labels.ShouldBe(labels.Order(OccupationSearchRule.Collation).ToList());
        labels.ShouldContain("IT-chefer");
        labels.ShouldContain("Ingenjörer och tekniker inom elektroteknik");

        var projektledare = OccupationSearchRule.Search(index, "projektledare");
        projektledare.Rows.ShouldHaveSingleItem().Kind.ShouldBe(OccupationSearchRowKind.Word);
    }

    /// <summary>The real v30 index from the two embedded resources, without a database.</summary>
    private static class V30
    {
        internal static OccupationNameIndexContents Load()
        {
            var asm = typeof(OccupationNameIndex).Assembly;
            using var snapshot = Parse(asm, "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json");
            using var map = Parse(asm,
                "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources.occupation-name-to-ssyk-level-4.v30.json");

            var groupByName = map.RootElement.GetProperty("mappings").EnumerateObject()
                .ToDictionary(e => e.Name, e => e.Value.GetString()!, StringComparer.Ordinal);
            var fields = snapshot.RootElement.GetProperty("occupationFields").EnumerateArray().ToList();
            var groups = fields.SelectMany(f => f.GetProperty("occupationGroups").EnumerateArray())
                .Select(g => (Id: g.GetProperty("conceptId").GetString()!, Label: g.GetProperty("label").GetString()!))
                .DistinctBy(g => g.Id)
                .ToList();
            var groupIds = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
            var names = fields.SelectMany(f => f.GetProperty("occupations").EnumerateArray())
                .Select(o => (Id: o.GetProperty("conceptId").GetString()!, Label: o.GetProperty("label").GetString()!))
                .Where(o => groupByName.TryGetValue(o.Id, out var g) && groupIds.Contains(g))
                .Select(o => (o.Label, groupByName[o.Id]));

            return Contents(names, groups);
        }

        private static JsonDocument Parse(System.Reflection.Assembly asm, string name)
        {
            using var stream = asm.GetManifestResourceStream(name);
            stream.ShouldNotBeNull();
            return JsonDocument.Parse(stream!);
        }
    }
}
