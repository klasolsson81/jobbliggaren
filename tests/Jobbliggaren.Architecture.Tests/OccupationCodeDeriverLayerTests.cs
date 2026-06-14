using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 3 (F4-3) anti-regression — the SSYK-derivation tier respects Clean
/// Architecture (ADR 0074; dotnet-architect §3.4; senior-cto-advisor Decision 5,
/// bindande DoD-obligation). The port <c>IOccupationCodeDeriver</c> + its DTOs
/// (<c>OccupationDerivationResult</c>, <c>OccupationCandidate</c>) + the
/// <c>OccupationMatchKind</c> enum are Application abstractions (beside
/// <c>ITaxonomyReadModel</c>); the impl <c>OccupationCodeDeriver</c> (+ any frozen-
/// map loader helper) is <c>internal sealed</c> in Infrastructure. The deriver
/// consumes the local NLP tier (Snowball via <c>ITextAnalyzer</c>) + the frozen
/// occupation-name→ssyk-4 map, but Application/Domain MUST NOT gain any NLP/Npgsql/
/// EF dependency through it (the port surface stays BCL-only, exactly like
/// ITaxonomyReadModel / the F4-2 NLP ports).
///
/// Mirrors TextAnalysisLayerTests + TaxonomyAclLayerTests.
///
/// RED until IOccupationCodeDeriver + DTOs ship in Application and
/// OccupationCodeDeriver ships internal in Infrastructure.
/// </summary>
public class OccupationCodeDeriverLayerTests
{
    // Fully-qualified port/DTO names — referenced as strings where the types do
    // not yet exist so the rest of the assembly still compiles; the typeof-based
    // facts below are the ones that go RED until the production types land.
    private const string DeriverNamespace = "Jobbliggaren.Infrastructure.Taxonomy";

    // ===============================================================
    // 1. Port + DTOs + enum live in the Application assembly
    // ===============================================================

    [Fact]
    public void IOccupationCodeDeriver_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.IOccupationCodeDeriver);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void OccupationDerivationResult_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationDerivationResult);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void OccupationCandidate_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationCandidate);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void OccupationMatchKind_is_in_Application_layer()
    {
        // The match-kind is an explainability/audit contract concept (how a
        // candidate was derived), not a domain invariant — it lives beside the
        // port, mirroring TextLanguage / SuggestionKind.
        var matchKind = typeof(Jobbliggaren.Application.JobAds.Abstractions.OccupationMatchKind);
        matchKind.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void OccupationMatchKind_is_pure_V2_no_group_label_kind()
    {
        // CTO Decision 1 — V2 only (occupation-name match). V1/V3 group-label
        // fallback is OUT for v1, so the enum must NOT carry an ExactGroupLabel
        // member (a forbidden member would signal the rejected V3 branch crept
        // in). Pure-V2 surface = exactly { ExactOccupationName, StemmedTokenOverlap }.
        var names = Enum.GetNames<
            Jobbliggaren.Application.JobAds.Abstractions.OccupationMatchKind>();

        names.ShouldBe(["ExactOccupationName", "StemmedTokenOverlap"], ignoreOrder: true,
            "OccupationMatchKind ska vara ren V2 (CTO Decision 1): bara " +
            "ExactOccupationName + StemmedTokenOverlap. Ingen ExactGroupLabel " +
            $"(V3 är OUT för v1). Faktiska: [{string.Join(", ", names)}].");
    }

    // ===============================================================
    // 2. The port does NOT live in Infrastructure (it is an Application abstraction)
    // ===============================================================

    [Fact]
    public void IOccupationCodeDeriver_is_not_in_Infrastructure_assembly()
    {
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.IOccupationCodeDeriver);
        port.Assembly.ShouldNotBe(
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 3. The deriver impl (+ its frozen-map loader helper) is internal sealed
    //    in Infrastructure — proven BY NAME so the RED state requires the type
    //    to exist and be non-public. (Complementary to
    //    TaxonomyAclLayerTests.Taxonomy_snapshot_types_are_internal_to_Infrastructure,
    //    which asserts the whole namespace; this one pins the new F4-3 types.)
    // ===============================================================

    [Fact]
    public void OccupationCodeDeriver_and_loader_helpers_are_internal_to_Infrastructure()
    {
        // OccupationCodeDeriver + any frozen-map loader helper must be non-public
        // — paritet med TaxonomyReadModel (internal sealed) och F4-2:s
        // TextAnalysis_impls_are_internal_to_Infrastructure. Matchar på namn så
        // testet går RED tills typen finns OCH är internal (CTO Decision 5 +
        // architect §3.3: internal sealed, singleton). ACL-isolation (ADR 0043).
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var deriverTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == DeriverNamespace
                        && (t.Name.Contains("OccupationCodeDeriver", StringComparison.Ordinal)
                            || t.Name.Contains("OccupationGroupMap", StringComparison.Ordinal)
                            || t.Name.Contains("FrozenOccupation", StringComparison.Ordinal)))
            .ToList();

        // The deriver type must exist (else RED — production not written yet).
        deriverTypes.ShouldContain(
            t => t.Name.Contains("OccupationCodeDeriver", StringComparison.Ordinal),
            "OccupationCodeDeriver saknas i Jobbliggaren.Infrastructure.Taxonomy " +
            "(F4-3 production-impl ej skriven än — väntad RED).");

        var publicDeriverTypes = deriverTypes
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicDeriverTypes.ShouldBeEmpty(
            "OccupationCodeDeriver + frozen-map-loader ska vara internal " +
            $"(ACL-isolation, ADR 0043). Public: {string.Join(", ", publicDeriverTypes!)}");
    }

    // ===============================================================
    // 4. Application MUST NOT depend on the NLP libraries through the deriver
    //    (the port surface stays BCL-only — Snowball lives only in Infrastructure)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries()
    {
        // Re-asserted in F4-3's own arch-test: surfacing the frozen map + the
        // stemmed pass behind IOccupationCodeDeriver must NOT drag Snowball/
        // WeCantSpell across the Application boundary — the deriver impl (in
        // Infrastructure) is the only thing that touches ITextAnalyzer's
        // Snowball-bound impl. Root namespaces: "Snowball" (libstemmer.net),
        // "WeCantSpell" (Hunspell port).
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek (Snowball/WeCantSpell) — F4-3 " +
            "deriver-porten ska vara BCL-only: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 5. Application MUST NOT depend on Npgsql/EF through the deriver
    //    (the deriver reads the in-memory snapshot via ITaxonomyReadModel +
    //    an embedded resource — no EF/Npgsql leaks up to the port)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_EfCore_or_Npgsql()
    {
        // Paritet ADR 0062 (FTS-LINQ kapslad i Infrastructure) + CLAUDE.md §2.1.
        // The deriver consumes ITaxonomyReadModel.GetTreeAsync (already a clean
        // Application DTO) + the frozen map; no EF/Npgsql type may cross into
        // Application via the new port.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "NpgsqlTypes",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Microsoft.EntityFrameworkCore.Relational")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot Npgsql/EF-relational (Clean Arch, CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 6. Domain MUST NOT depend on the deriver / the NLP libraries
    // ===============================================================

    [Fact]
    public void Domain_should_not_depend_on_Application_or_NLP_libraries()
    {
        // Den deterministiska derivationen är en Application-/Infrastructure-
        // angelägenhet; Domain (inkl. personnummer-guarden i Domain/Privacy)
        // förblir NLP-fri och Application-fri.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Application", "Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot Application/NLP-bibliotek: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
