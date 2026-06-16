using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG C (motor-stresstest) — the STRUCTURAL half of the load-bearing invariant
/// (ADR 0040 Beslut 4, verbatim: "No <c>SavedSearch</c> is ever created without explicit user
/// confirmation"). CTO Fork 4 = 4C: prove the invariant both structurally (here) and at runtime
/// (the corpus sweep in <c>DeriverCorpusStressTests.BearingInvariant_*</c>).
///
/// <para>The structural encoding: <b>no type that touches the deriver (its port or its result)
/// may also create/persist a <c>SavedSearch</c></b>. This is the precise shape of "nothing
/// auto-creates a SavedSearch from derivation output" — and it stays valid through STEG B,
/// because the user-confirmation step breaks the dependency: a future <c>ConfirmDerivedSearch</c>
/// handler consumes the user's CHOSEN ssyk-4 ids (plain input), not the deriver's
/// <c>OccupationDerivationResult</c> / the <c>IOccupationCodeDeriver</c> port. If someone ever
/// wires the deriver's output straight into SavedSearch creation (bypassing confirmation), this
/// test goes RED.</para>
///
/// <para>Today the rule passes because the pipeline is port-only: <c>DeriveOccupationCodesQuery</c>
/// returns proposals and <c>ImportResume</c> stores them on the <c>ParsedResume</c> — neither
/// creates a SavedSearch (grounding-verified).</para>
/// </summary>
public class DerivedSavedSearchInvariantTests
{
    private const string DeriverPort =
        "Jobbliggaren.Application.JobAds.Abstractions.IOccupationCodeDeriver";
    private const string DeriverResult =
        "Jobbliggaren.Application.JobAds.Abstractions.OccupationDerivationResult";
    private const string DeriverCandidate =
        "Jobbliggaren.Application.JobAds.Abstractions.OccupationCandidate";
    private const string SavedSearchDomain = "Jobbliggaren.Domain.SavedSearches";
    private const string DeriverImplNamespace = "Jobbliggaren.Infrastructure.Taxonomy";

    [Fact]
    public void No_type_that_consumes_derivation_output_also_creates_a_SavedSearch()
    {
        // The bearing invariant, structurally: anything depending on the deriver port or its
        // result must NOT also depend on the SavedSearch aggregate. A bridge type would be the
        // signature of auto-creation from derivation (forbidden without confirmation).
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .That()
            .HaveDependencyOnAny(DeriverPort, DeriverResult, DeriverCandidate)
            .ShouldNot()
            .HaveDependencyOn(SavedSearchDomain)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "BÄRANDE INVARIANT (ADR 0040 Beslut 4) STRUKTURELLT BRUTEN: en typ konsumerar " +
            "deriver-output OCH skapar en SavedSearch — det är auto-skapande från derivering " +
            "utan användarbekräftelse. Confirm-handlern (STEG B) ska ta användarvalda ssyk-4-id:n, " +
            "INTE OccupationDerivationResult/IOccupationCodeDeriver. Brytande typer: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void The_deriver_implementation_does_not_depend_on_SavedSearch()
    {
        // The deriver PROPOSES; it must never reach into the SavedSearch aggregate (it cannot
        // create one). Complements the Application-side rule above (defense-in-depth).
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .That()
            .ResideInNamespace(DeriverImplNamespace)
            .ShouldNot()
            .HaveDependencyOn(SavedSearchDomain)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Deriver-impl-namespacet (Infrastructure.Taxonomy) beror på SavedSearch — derivern " +
            "ska FÖRESLÅ, aldrig skapa en sökning: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
