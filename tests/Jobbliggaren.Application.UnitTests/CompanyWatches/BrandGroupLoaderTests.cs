using System.Text;
using Jobbliggaren.Infrastructure.CompanyWatches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — <see cref="BrandGroupLoader"/> shape validation, driven through the real
/// <c>LoadFrom(Stream)</c> seam with synthetic assets (the <c>CriterionReferenceLoader</c> /
/// <c>BranschgruppLoader</c> mold). Every malformed shape must fail LOUD (the provider is
/// instance-registered → host build dies), EXCEPT the deliberate divergence: an EMPTY groups array is
/// LEGAL. Plus a real-asset pin so the shipped catalogue can never regress its own contract.
/// </summary>
public class BrandGroupLoaderTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>Injects a defect and PROVES it landed — an unmatched Replace is a silent no-op and the
    /// test would then fail for a reason unrelated to the loader.</summary>
    private static MemoryStream Mutated(string find, string replaceWith)
    {
        MinimalValid.Contains(find, StringComparison.Ordinal).ShouldBeTrue(
            $"test-buggen: mönstret '{find}' finns inte i MinimalValid — mutationen landade aldrig.");
        return Json(MinimalValid.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    // A well-formed catalogue: one group, two PUBLIC AB member org.nrs (3rd digit >= 2 → not
    // personnummer-shaped). "5560125790" (3rd digit 6) and "5569876543" (3rd digit 6).
    private const string MinimalValid = """
        {
          "//": "test asset",
          "brandGroupVersion": "test.v1",
          "groups": [
            { "id": "volvo-koncernen", "displayName": "Volvo (koncern)", "members": ["5560125790", "5569876543"] }
          ]
        }
        """;

    private const string EmptyCatalogue = """
        { "brandGroupVersion": "test.v1", "groups": [] }
        """;

    // ---- positive controls ----

    [Fact]
    public void LoadFrom_MapsTheContract_WhenTheAssetIsWellFormed()
    {
        var catalog = BrandGroupLoader.LoadFrom(Json(MinimalValid));

        catalog.Version.ShouldBe("test.v1");
        catalog.Groups.Count.ShouldBe(1);
        var group = catalog.Find("volvo-koncernen");
        group.ShouldNotBeNull();
        group!.DisplayName.ShouldBe("Volvo (koncern)");
        group.MemberOrgNrs.ShouldBe(["5560125790", "5569876543"]);
        catalog.Find("unknown-slug").ShouldBeNull();
    }

    // THE deliberate divergence from the sibling loaders: an empty groups array is a LEGAL state (the
    // mechanism ships before any group is curated). It must NOT throw.
    [Fact]
    public void LoadFrom_WithEmptyGroups_ReturnsEmptyCatalogue_NotAnError()
    {
        var catalog = BrandGroupLoader.LoadFrom(Json(EmptyCatalogue));

        catalog.Version.ShouldBe("test.v1");
        catalog.Groups.Count.ShouldBe(0);
        catalog.Find("anything").ShouldBeNull();
    }

    // ---- fail-loud shape checks (each mutation-verified to actually land) ----

    [Fact]
    public void LoadFrom_WithBlankVersion_Throws()
    {
        Should.Throw<InvalidOperationException>(
            () => BrandGroupLoader.LoadFrom(Mutated("\"brandGroupVersion\": \"test.v1\"",
                                                    "\"brandGroupVersion\": \"\"")));
    }

    [Fact]
    public void LoadFrom_WithPersonnummerShapedMember_Throws()
    {
        // 3rd digit 0 → personnummer-shaped → must be rejected fail-loud (never a pnr in the repo).
        Should.Throw<InvalidOperationException>(
            () => BrandGroupLoader.LoadFrom(Mutated("\"5560125790\"", "\"8001019876\"")));
    }

    [Fact]
    public void LoadFrom_WithMalformedMemberOrgNr_Throws()
    {
        Should.Throw<InvalidOperationException>(
            () => BrandGroupLoader.LoadFrom(Mutated("\"5560125790\"", "\"12345\"")));
    }

    [Fact]
    public void LoadFrom_WithDuplicateGroupId_Throws()
    {
        // Add a second group with the SAME id.
        var json = MinimalValid.Replace(
            "\"members\": [\"5560125790\", \"5569876543\"] }",
            "\"members\": [\"5560125790\", \"5569876543\"] },"
            + " { \"id\": \"volvo-koncernen\", \"displayName\": \"Dup\", \"members\": [\"5561234567\"] }",
            StringComparison.Ordinal);

        Should.Throw<InvalidOperationException>(() => BrandGroupLoader.LoadFrom(Json(json)));
    }

    [Fact]
    public void LoadFrom_WithBlankDisplayName_Throws()
    {
        Should.Throw<InvalidOperationException>(
            () => BrandGroupLoader.LoadFrom(Mutated("\"displayName\": \"Volvo (koncern)\"",
                                                    "\"displayName\": \"\"")));
    }

    [Fact]
    public void LoadFrom_WithZeroMembers_Throws()
    {
        // A zero-member group is a follow that matches nothing forever — the cardinal sin. Unlike an
        // empty CATALOGUE (legal), an empty MEMBER list is a hard error.
        Should.Throw<InvalidOperationException>(
            () => BrandGroupLoader.LoadFrom(Mutated("\"members\": [\"5560125790\", \"5569876543\"]",
                                                    "\"members\": []")));
    }

    [Fact]
    public void LoadFrom_WithDuplicateMemberWithinAGroup_Throws()
    {
        Should.Throw<InvalidOperationException>(
            () => BrandGroupLoader.LoadFrom(Mutated("\"5569876543\"", "\"5560125790\"")));
    }

    // ---- real embedded asset ----

    [Fact]
    public void Load_RealEmbeddedAsset_IsAValidEmptyCatalogue()
    {
        // The shipped catalogue is deliberately empty (D5b). This proves the embedded resource is wired
        // (LogicalName matches) AND that the shipped file satisfies its own contract.
        var catalog = BrandGroupLoader.Load();

        catalog.Version.ShouldBe("1.0");
        catalog.Groups.Count.ShouldBe(0);
    }
}
