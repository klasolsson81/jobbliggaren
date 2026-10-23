using Jobbliggaren.Application.JobAds.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.SearchOccupations;

// #2198 (ADR 0180) — the row's two factories are its only constructors, and they hold the row
// shape the picker relies on. Labels and ids are real v30 data: taxonomy-snapshot.json joined to
// occupation-name-to-ssyk-level-4.v30.json (Projektledare, IT z5AM_ayf_WcL → DJh5_yyF_hEM;
// Projektledare, el 1jm3_XFQ_TdJ and Projektledare, telekom T55e_efV_Eqh → nDaB_vdy_eAy).
public class OccupationSearchRowTests
{
    private static readonly OccupationSearchVariant ProjektledareIt =
        new("Projektledare, IT", "DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl.");

    private static readonly OccupationSearchVariant ProjektledareEl =
        new("Projektledare, el", "nDaB_vdy_eAy", "Ingenjörer och tekniker inom elektroteknik");

    private static readonly OccupationSearchVariant ProjektledareTelekom =
        new("Projektledare, telekom", "nDaB_vdy_eAy", "Ingenjörer och tekniker inom elektroteknik");

    [Fact]
    public void ForWord_TwoVariantsInTwoGroups_CarriesNoGroupOfItsOwn()
    {
        var row = OccupationSearchRow.ForWord("Projektledare", [ProjektledareIt, ProjektledareEl], exactMatch: false);

        row.Kind.ShouldBe(OccupationSearchRowKind.Word);
        row.GroupConceptId.ShouldBeNull();
        row.GroupLabel.ShouldBeNull();
        row.MatchedNames.ShouldBeEmpty();
        row.LabelHit.ShouldBeFalse();
        row.HeadWord.ShouldBe("Projektledare");
        row.Variants.ShouldBe([ProjektledareIt, ProjektledareEl]);
    }

    [Fact]
    public void ForWord_VariantsInOneGroup_IsRejected()
    {
        Should.Throw<ArgumentException>(() =>
            OccupationSearchRow.ForWord("Projektledare", [ProjektledareEl, ProjektledareTelekom], exactMatch: false));
    }

    [Fact]
    public void ForWord_OneVariant_IsRejected()
    {
        Should.Throw<ArgumentException>(() =>
            OccupationSearchRow.ForWord("Projektledare", [ProjektledareIt], exactMatch: false));
    }

    [Fact]
    public void ForGroup_WithMatchedNames_CarriesNoVariantsAndNoHeadWord()
    {
        var row = OccupationSearchRow.ForGroup(
            "DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl.", ["Backend-utvecklare"], labelHit: false, exactMatch: false);

        row.Kind.ShouldBe(OccupationSearchRowKind.Group);
        row.GroupConceptId.ShouldBe("DJh5_yyF_hEM");
        row.MatchedNames.ShouldBe(["Backend-utvecklare"]);
        row.HeadWord.ShouldBeNull();
        row.Variants.ShouldBeEmpty();
    }

    [Fact]
    public void ForGroup_LabelHitAlone_IsAllowed()
    {
        var row = OccupationSearchRow.ForGroup("UxT1_tPF_Kbg", "Övriga IT-specialister", [], labelHit: true, exactMatch: false);

        row.LabelHit.ShouldBeTrue();
        row.MatchedNames.ShouldBeEmpty();
    }

    [Fact]
    public void ForGroup_WithoutNameOrLabelHit_IsRejected()
    {
        Should.Throw<ArgumentException>(() =>
            OccupationSearchRow.ForGroup("UxT1_tPF_Kbg", "Övriga IT-specialister", [], labelHit: false, exactMatch: false));
    }
}
