using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #311 D6 / ADR 0087 — org.nr-surfacings-grind (security-auditor-rekommenderad
/// drift-guard, PR-2). org.nr (arbetsgivarens organisationsnummer) är i PR-2 en
/// REN FILTER-INPUT: den läses bara i <c>JobAdSearchComposition.ApplyFilter</c>:s
/// WHERE och ekas ALDRIG tillbaka. Detta är en säkerhets-invariant, inte en
/// stilpreferens: en enskild firmas org.nr KAN vara ett personnummer (CLAUDE.md §5,
/// högsta-prioritets-guarden), så org.nr får inte surfas oflaggat förrän PR-2b inför
/// personnummer-guarden + den obligatoriska security-auditor-grinden (ADR 0087 D6/D7,
/// Variant B). Dessa två tester gör "noll org.nr surfat"-kontraktet falsifierbart: en
/// framtida PR som lägger org.nr i list-DTO:n eller gör employer till en facett-dimension
/// failar bygget i stället för att tyst läcka. (Jfr drift-guard-mönstret #291.)
/// </summary>
public class EmployerOrgNumberSurfaceGuardTests
{
    // Substrängar som indikerar att org.nr läckt in i en utgående DTO. org.nr lagras
    // verbatim i shadow-kolumnen organization_number; varje DTO-fält vars namn bär
    // "organization"/"orgnr"/"orgnumber" vore en surfacing-regression.
    private static readonly string[] OrgNumberFieldMarkers =
        ["organization", "orgnr", "orgnumber"];

    [Fact]
    public void JobAdDto_does_not_expose_organization_number()
    {
        // JobAdDto är den ENDA DTO:n som list- (GET /api/v1/job-ads) och detalj-vägen
        // (GET /{id}) returnerar. org.nr får aldrig projiceras hit (ToDto() är SPOT-
        // chokepointen). Filter-input ≠ output (GDPR Art. 5(1)(c) data-minimering).
        var leaking = typeof(JobAdDto)
            .GetProperties()
            .Where(p => OrgNumberFieldMarkers.Any(m =>
                p.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        leaking.ShouldBeEmpty(
            "JobAdDto får inte exponera org.nr — det är ren filter-input i PR-2 "
            + "(ADR 0087 D6, CONTAINED). En enskild firmas org.nr kan vara ett "
            + "personnummer; surfacing kräver PR-2b:s personnummer-guard + "
            + "security-auditor-grind. Hittade läckande fält: "
            + string.Join(", ", leaking));
    }

    [Fact]
    public void FacetDimension_does_not_include_Employer()
    {
        // ADR 0087 D6: employer-count får INTE foldas in i IJobAdSearchQuery
        // (FacetCountsAsync). En FacetDimension.Employer skulle surfa org.nr som
        // facett-count-nycklar ({org.nr: antal}) — exakt den surfacing PR-2b äger.
        // Den separata disambiguerings-projektionen (med company_name) är den enda
        // tillåtna arbetsgivar-count-ytan, och den landar i PR-2b bakom guarden.
        var names = Enum.GetNames<FacetDimension>();

        names.ShouldNotContain(
            "Employer",
            "FacetDimension får inte ha en Employer-medlem i PR-2 (ADR 0087 D6 — "
            + "employer-count får ej foldas in i IJobAdSearchQuery; org.nr-surfacing "
            + "via disambiguerings-projektionen + guard landar i PR-2b).");
    }
}
