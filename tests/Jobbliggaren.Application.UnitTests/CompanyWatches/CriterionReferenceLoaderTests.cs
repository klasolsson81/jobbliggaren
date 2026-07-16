using System.Text;
using Jobbliggaren.Infrastructure.CompanyRegister.Reference;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches;

/// <summary>
/// #560 PR-3 (CTO Fork G2) — <see cref="CriterionReferenceLoader"/> shape validation, driven
/// through the real <c>Load*From(Stream)</c> seams with synthetic assets (the
/// <c>BranschgruppLoaderTests</c> mold), plus REAL-asset pins: the loader validates FORM (SCB
/// revisions change counts legitimately), the tests pin the CURRENT counts so a truncated or
/// double-loaded asset cannot ship silently.
/// </summary>
public class CriterionReferenceLoaderTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>Injects a defect and PROVES it landed — an unmatched Replace is a silent no-op
    /// and the test would fail for a reason that has nothing to do with the loader.</summary>
    private static MemoryStream MutatedSni(string find, string replaceWith)
    {
        MinimalValidSni.Contains(find, StringComparison.Ordinal).ShouldBeTrue(
            $"test-buggen: mönstret '{find}' finns inte i MinimalValidSni — mutationen landade aldrig.");
        return Json(MinimalValidSni.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    private static MemoryStream MutatedKommun(string find, string replaceWith)
    {
        MinimalValidKommun.Contains(find, StringComparison.Ordinal).ShouldBeTrue(
            $"test-buggen: mönstret '{find}' finns inte i MinimalValidKommun — mutationen landade aldrig.");
        return Json(MinimalValidKommun.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    // True to SNI 2025: division 62 sits under section K, and the programming leaf is 62100
    // (SNI 2007's J/62010 do not exist in 2025 — the restructure that bit twice on 2026-07-16).
    private const string MinimalValidSni = """
        {
          "sniVersion": "test.v1",
          "sections": [ { "code": "K", "name": "Förlagsverksamhet, IT och telekommunikation" } ],
          "divisions": [ { "code": "62", "section": "K", "name": "Dataprogrammering, datakonsultverksamhet o.d." } ],
          "leaves": [ { "code": "62100", "division": "62", "name": "Dataprogrammering" } ]
        }
        """;

    private const string MinimalValidKommun = """
        {
          "kommunVersion": "test.v1",
          "lan": [ { "code": "01", "name": "Stockholms län" } ],
          "kommuner": [ { "code": "0180", "name": "Stockholm", "lanCode": "01" } ]
        }
        """;

    // ---- positive controls ----

    [Fact]
    public void LoadSniFrom_MapsTheContract_WhenTheAssetIsWellFormed()
    {
        var catalog = CriterionReferenceLoader.LoadSniFrom(Json(MinimalValidSni));

        catalog.Version.ShouldBe("test.v1");
        catalog.Sections.ShouldHaveSingleItem().Code.ShouldBe("K");
        catalog.Divisions.ShouldHaveSingleItem().SectionCode.ShouldBe("K");
        catalog.Leaves.ShouldHaveSingleItem().Code.ShouldBe("62100");
        catalog.LeafExists("62100").ShouldBeTrue();
        catalog.LeafExists("99999").ShouldBeFalse();
    }

    [Fact]
    public void LoadKommunerFrom_MapsTheContract_WhenTheAssetIsWellFormed()
    {
        var catalog = CriterionReferenceLoader.LoadKommunerFrom(Json(MinimalValidKommun));

        catalog.Version.ShouldBe("test.v1");
        catalog.Lan.ShouldHaveSingleItem().Name.ShouldBe("Stockholms län");
        catalog.Kommuner.ShouldHaveSingleItem().LanCode.ShouldBe("01");
        catalog.Exists("0180").ShouldBeTrue();
        catalog.Exists("9999").ShouldBeFalse();
    }

    // ---- the REAL embedded assets (integrity pins) ----

    [Fact]
    public void LoadSni_RealAsset_CarriesTheFullSni2025Universe()
    {
        var catalog = CriterionReferenceLoader.LoadSni();

        // SCB's published SNI 2025 counts (fetched 2026-07-16). If SCB revises the standard these
        // pins move WITH the new dataset in the same commit — what they catch is a truncated,
        // double-loaded or mis-parsed asset shipping silently.
        catalog.Sections.Count.ShouldBe(22);
        catalog.Divisions.Count.ShouldBe(87);
        catalog.Leaves.Count.ShouldBe(835);

        // Spot-pins: the leaf form is DOTLESS (register namespace), Swedish names survived UTF-8.
        catalog.LeafExists("62100").ShouldBeTrue();
        catalog.LeafExists("01110").ShouldBeTrue();
        catalog.LeafExists("62.100").ShouldBeFalse();
        catalog.Leaves.Single(static l => l.Code == "01110")
            .Name.ShouldContain("Odling av spannmål");

        // THE VERSION PIN. "62010" is SNI 2007's programming leaf; SNI 2025 restructured division
        // 62 (62100/62201/62202/62900, under section K — not J). Its absence proves the asset is
        // the 2025 standard — and the register speaks 2025 too (measured against dev-DB 2026-07-16:
        // sni_codes && '{62100}' → 25 370 rows; && '{62010}' → 0). A 2007 dataset here would make
        // every picker-authored criterion silently match nothing.
        catalog.LeafExists("62010").ShouldBeFalse(
            "'62010' är en SNI 2007-kod — hittas den i katalogen är assetet fel standardversion.");
    }

    [Fact]
    public void LoadKommuner_RealAsset_CarriesAll290Kommuner()
    {
        var catalog = CriterionReferenceLoader.LoadKommuner();

        catalog.Lan.Count.ShouldBe(21);
        catalog.Kommuner.Count.ShouldBe(290);

        // Leading zero is load-bearing ("0180" = Stockholm; 180 matches nothing).
        catalog.Exists("0180").ShouldBeTrue();
        catalog.Exists("180").ShouldBeFalse();
        catalog.Kommuner.Single(static k => k.Code == "1480").Name.ShouldBe("Göteborg");
        catalog.Kommuner.Single(static k => k.Code == "0184").Name.ShouldBe("Solna");
    }

    [Fact]
    public void RealAssets_EveryLeafSatisfiesTheDomainFormatGuard()
    {
        // The catalog's codes and CompanyWatchCriteriaSpec's format guard must agree, or "exists"
        // and "storable" silently diverge: a picker-offered code the Domain rejects (or the
        // reverse). The spec is the owner of the format rule — run every real code through IT.
        var sni = CriterionReferenceLoader.LoadSni();
        var kommuner = CriterionReferenceLoader.LoadKommuner();

        foreach (var leaf in sni.Leaves)
        {
            Jobbliggaren.Domain.CompanyWatches.CompanyWatchCriteriaSpec
                .Create([leaf.Code], ["0180"]).IsSuccess
                .ShouldBeTrue($"SNI-lövet '{leaf.Code}' avvisas av Domain-formatguarden.");
        }

        foreach (var kommun in kommuner.Kommuner)
        {
            Jobbliggaren.Domain.CompanyWatches.CompanyWatchCriteriaSpec
                .Create(["62100"], [kommun.Code]).IsSuccess
                .ShouldBeTrue($"Kommunkoden '{kommun.Code}' avvisas av Domain-formatguarden.");
        }
    }

    // ---- SNI negatives ----

    [Fact]
    public void LoadSniFrom_Throws_WhenVersionIsMissing()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(MutatedSni("\"sniVersion\": \"test.v1\",", "")));

    [Fact]
    public void LoadSniFrom_Throws_WhenALeafCodeIsMalformed()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(MutatedSni("\"62100\"", "\"6201\"")));

    [Fact]
    public void LoadSniFrom_Throws_WhenALeafCodeCarriesUnicodeDigits()
    {
        // The register is ASCII-only; a fullwidth code in the CATALOG would make the validator
        // accept what the register can never match — the silent-miss sin at the reference layer.
        Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(MutatedSni("\"62100\"", "\"６２０１０\"")));
    }

    [Fact]
    public void LoadSniFrom_Throws_WhenALeafPointsAtAnUnknownDivision()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(
                MutatedSni("\"division\": \"62\"", "\"division\": \"63\"")));

    [Fact]
    public void LoadSniFrom_Throws_WhenTheDivisionIsNotTheLeafCodePrefix()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(MutatedSni(
                "\"code\": \"62100\", \"division\": \"62\"",
                "\"code\": \"63010\", \"division\": \"62\"")));

    [Fact]
    public void LoadSniFrom_Throws_WhenALeafIsDeclaredTwice()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(MutatedSni(
                "{ \"code\": \"62100\", \"division\": \"62\", \"name\": \"Dataprogrammering\" }",
                "{ \"code\": \"62100\", \"division\": \"62\", \"name\": \"Dataprogrammering\" },"
                + "{ \"code\": \"62100\", \"division\": \"62\", \"name\": \"Dubblett\" }")));

    [Fact]
    public void LoadSniFrom_Throws_WhenALeafNameIsMissing()
    {
        // System.Text.Json ignores unknown members — a typo'd key deserialises to null, and a
        // nameless picker row is a silently broken UI, so the loader must refuse it.
        Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(MutatedSni(
                "\"name\": \"Dataprogrammering\"", "\"nmae\": \"Dataprogrammering\"")));
    }

    [Fact]
    public void LoadSniFrom_Throws_WhenADivisionPointsAtAnUnknownSection()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadSniFrom(
                MutatedSni("\"section\": \"K\"", "\"section\": \"X\"")));

    // ---- kommun negatives ----

    [Fact]
    public void LoadKommunerFrom_Throws_WhenVersionIsMissing()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadKommunerFrom(
                MutatedKommun("\"kommunVersion\": \"test.v1\",", "")));

    [Fact]
    public void LoadKommunerFrom_Throws_WhenAKommunCodeIsMalformed()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadKommunerFrom(
                MutatedKommun("\"0180\"", "\"018\"")));

    [Fact]
    public void LoadKommunerFrom_Throws_WhenAKommunPointsAtAnUnknownLan()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadKommunerFrom(
                MutatedKommun("\"lanCode\": \"01\"", "\"lanCode\": \"99\"")));

    [Fact]
    public void LoadKommunerFrom_Throws_WhenTheLanIsNotTheKommunCodePrefix()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadKommunerFrom(MutatedKommun(
                "\"code\": \"0180\"", "\"code\": \"1180\"")));

    [Fact]
    public void LoadKommunerFrom_Throws_WhenAKommunIsDeclaredTwice()
        => Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadKommunerFrom(MutatedKommun(
                "{ \"code\": \"0180\", \"name\": \"Stockholm\", \"lanCode\": \"01\" }",
                "{ \"code\": \"0180\", \"name\": \"Stockholm\", \"lanCode\": \"01\" },"
                + "{ \"code\": \"0180\", \"name\": \"Dubblett\", \"lanCode\": \"01\" }")));
}
