using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — unit tests for the register's legal-entities-only ingest guard. The
/// personnummer-shape exclusion is the register's GDPR foundation (CLAUDE.md §5 highest-priority /
/// security-auditor veto surface), so it is pinned here as a first-class, fast, DB-free test.
/// </summary>
public class ScbLegalEntityFilterTests
{
    private static ScbCompanyRecord Record(
        string orgNr,
        string name = "Acme AB",
        string status = "1",
        bool advertisingBlock = false,
        params string[] sni) =>
        new(orgNr, name, "0180", "Stockholm", sni, advertisingBlock, status);

    [Fact]
    public void Apply_ExcludesPersonnummerShapedOrgNr_AndNeverPersistsIt()
    {
        // 1901012384: third digit '0' < '2' → personnummer-shaped (a sole trader's org.nr equals a
        // personnummer). Even if the SCB Juridisk-form filter let it through, the register must NEVER
        // store it. This IS the near-GDPR-free guarantee.
        var batch = new[] { Record("1901012384", name: "Anna Andersson") };

        var result = ScbLegalEntityFilter.Apply(batch);

        result.Entries.ShouldBeEmpty();
        result.ExcludedPersonnummerShaped.ShouldBe(1);
        result.ExcludedInvalid.ShouldBe(0);
    }

    [Fact]
    public void Apply_ExcludesInvalidOrgNr()
    {
        var batch = new[] { Record("not-ten-digits"), Record("123") };

        var result = ScbLegalEntityFilter.Apply(batch);

        result.Entries.ShouldBeEmpty();
        result.ExcludedInvalid.ShouldBe(2);
        result.ExcludedPersonnummerShaped.ShouldBe(0);
    }

    [Fact]
    public void Apply_MapsValidLegalEntity_WithAllFields()
    {
        // 5560125790 (Volvo): third digit '6' ≥ '2' → a legal entity, not personnummer-shaped.
        var batch = new[]
        {
            Record("5560125790", name: "Volvo AB", status: "1", advertisingBlock: true, sni: ["29100", "45200"]),
        };

        var result = ScbLegalEntityFilter.Apply(batch);

        var entry = result.Entries.ShouldHaveSingleItem();
        entry.OrganizationNumber.ShouldBe("5560125790");
        entry.Name.ShouldBe("Volvo AB");
        entry.SeatMunicipalityCode.ShouldBe("0180");
        entry.SeatMunicipalityName.ShouldBe("Stockholm");
        entry.SniCodes.ShouldBe(["29100", "45200"]);
        entry.HasAdvertisingBlock.ShouldBeTrue();
        entry.ScbStatusRaw.ShouldBe("1");
        entry.Status.ShouldBe(CompanyRegisterStatus.Active);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("9", false)]
    [InlineData("", false)]
    [InlineData("unexpected", false)]
    public void MapStatus_DerivesActiveOnlyForCode1(string raw, bool expectedActive)
    {
        var expected = expectedActive ? CompanyRegisterStatus.Active : CompanyRegisterStatus.Deregistered;
        ScbLegalEntityFilter.MapStatus(raw).ShouldBe(expected);
    }

    [Fact]
    public void Apply_TreatsEmptyRawStatusAsNull_ColumnStaysNullNotEmptyString()
    {
        var result = ScbLegalEntityFilter.Apply([Record("5560125790", status: "")]);

        var entry = result.Entries.ShouldHaveSingleItem();
        entry.ScbStatusRaw.ShouldBeNull();
        entry.Status.ShouldBe(CompanyRegisterStatus.Deregistered);
    }

    [Fact]
    public void Apply_SeparatesGuardsInAMixedBatch()
    {
        var batch = new[]
        {
            Record("5560125790"),   // legal → kept
            Record("1901012384"),   // pnr-shaped → excluded
            Record("bad"),          // invalid → excluded
            Record("5565021846"),   // legal → kept
        };

        var result = ScbLegalEntityFilter.Apply(batch);

        result.Entries.Count.ShouldBe(2);
        result.ExcludedPersonnummerShaped.ShouldBe(1);
        result.ExcludedInvalid.ShouldBe(1);
    }
}
