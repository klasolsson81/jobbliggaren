using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 5 (F4-5) anti-regression — the deterministic "Fast mode" matching
/// engine respects Clean Architecture (ADR 0074 row U5a; senior-cto-advisor
/// Decision 4 = V4-b, bindande; dotnet-architect D1/D2/D4). The port
/// <c>IMatchScorer</c> + its BCL-only result/input types
/// (<c>MatchScore</c>, <c>MatchDimension</c>, <c>CandidateMatchProfile</c>) + the
/// <c>MatchDimensionVerdict</c> enum are Application abstractions (in
/// <c>Matching/Abstractions/</c>, beside <c>JobAds/Abstractions/</c>); the impl
/// <c>MatchScorer</c> is <c>internal sealed</c> in Infrastructure
/// (<c>Jobbliggaren.Infrastructure.Matching</c>). The scorer reads the JobAd title
/// + the STORED shadow columns (EF.Property) and consumes the local NLP tier
/// (Snowball via <c>ITextAnalyzer</c>) for dim-2, but Application/Domain MUST NOT
/// gain any NLP/Npgsql/EF dependency through the port — the port surface stays
/// BCL + Domain-ids only (exactly like <c>IOccupationCodeDeriver</c> /
/// <c>ITaxonomyReadModel</c> / the F4-2 NLP ports).
///
/// CTO Decision 0/3 (Goodhart guard): <c>MatchScore</c> carries NO opaque total
/// (<c>Value: 0-100</c>) and NO skill/requirement dimensions (those are F4-6) —
/// pinned BY SHAPE below so the rejected branches cannot creep in.
///
/// Mirrors OccupationCodeDeriverLayerTests + TextAnalysisLayerTests.
///
/// RED until IMatchScorer + the DTOs/enum ship in Application and
/// MatchScorer ships internal in Infrastructure.
/// </summary>
public class MatchScorerLayerTests
{
    // The scorer impl lives in this Infrastructure namespace (architect DEL 3 /
    // CTO file-plan: src/Jobbliggaren.Infrastructure/Matching/MatchScorer.cs).
    // Referenced as a string so the typeof-free namespace scan below goes RED
    // (requires the type to EXIST and be non-public) without breaking compilation
    // of the rest of the assembly.
    private const string ScorerNamespace = "Jobbliggaren.Infrastructure.Matching";

    // ===============================================================
    // 1. Port + result/input types + enum live in the Application assembly
    // ===============================================================

    [Fact]
    public void IMatchScorer_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.Matching.Abstractions.IMatchScorer);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void MatchScore_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchScore);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void MatchDimension_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchDimension);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void CandidateMatchProfile_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Abstractions.CandidateMatchProfile);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void MatchDimensionVerdict_is_in_Application_layer()
    {
        // The verdict is an explainability/honesty contract concept (how a
        // dimension scored), not a domain invariant — it lives beside the port,
        // mirroring OccupationMatchKind / TextLanguage.
        var verdict = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchDimensionVerdict);
        verdict.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 2. Goodhart guard pinned BY SHAPE (CTO Decision 0 + Decision 3)
    // ===============================================================

    [Fact]
    public void MatchDimensionVerdict_is_the_locked_four_member_set()
    {
        // CTO Decision 3 — exactly { Match, Partial, NoMatch, NotAssessed }.
        // A forbidden extra member (e.g. a numeric-band kind) would signal a
        // rejected scoring shape crept in.
        var names = Enum.GetNames<
            Jobbliggaren.Application.Matching.Abstractions.MatchDimensionVerdict>();

        names.ShouldBe(["Match", "Partial", "NoMatch", "NotAssessed"], ignoreOrder: true,
            "MatchDimensionVerdict ska vara exakt { Match, Partial, NoMatch, " +
            "NotAssessed } (CTO Decision 3). Faktiska: " +
            $"[{string.Join(", ", names)}].");
    }

    [Fact]
    public void MatchDimension_carries_only_verdict_plus_matched_and_missing_no_numeric_score()
    {
        // CTO Decision 0/3 (Goodhart guard): MatchDimension exposes a verdict +
        // two cited-evidence string lists (Matched, Missing) and NOTHING numeric.
        // A `Score`/`Value`/`Intensity` property would be the opaque-number
        // anti-pattern (CLAUDE.md §5; rejected V3-b/V3-c). Asserted on the public
        // instance-property surface of the record.
        var dimension = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchDimension);

        var propNames = dimension
            .GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract") // compiler-generated on records
            .Select(p => p.Name)
            .ToList();

        propNames.ShouldBe(["Verdict", "Matched", "Missing"], ignoreOrder: true,
            "MatchDimension ska bära exakt { Verdict, Matched, Missing } — ingen " +
            "numerisk Score/Value/Intensity (Goodhart guard, CLAUDE.md §5). " +
            $"Faktiska: [{string.Join(", ", propNames)}].");
    }

    [Fact]
    public void MatchScore_carries_exactly_the_four_F4_5_dimensions_no_total_no_skill_or_requirement()
    {
        // CTO Decision 0: NO top-level numeric total (Value: 0-100) and NO
        // skill/requirement dimensions (those are F4-6). The thin-vertical shape
        // is exactly the four bound dimensions.
        var score = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchScore);

        var propNames = score
            .GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToList();

        propNames.ShouldBe(
            ["SsykOverlap", "TitleSimilarity", "RegionFit", "EmploymentFit"],
            ignoreOrder: true,
            "MatchScore ska bära exakt de fyra F4-5-dimensionerna utan opak total " +
            "(Value) och utan skill/requirement (F4-6, CTO Decision 0). Faktiska: " +
            $"[{string.Join(", ", propNames)}].");
    }

    // ===============================================================
    // 3. The port does NOT live in Infrastructure (it is an Application abstraction)
    // ===============================================================

    [Fact]
    public void IMatchScorer_is_not_in_Infrastructure_assembly()
    {
        var port = typeof(Jobbliggaren.Application.Matching.Abstractions.IMatchScorer);
        port.Assembly.ShouldNotBe(
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 4. The scorer impl is internal sealed in Infrastructure — proven BY NAME
    //    so the RED state requires the type to exist and be non-public
    //    (CTO Decision 4 = V4-b; parity OccupationCodeDeriver internal sealed).
    // ===============================================================

    [Fact]
    public void MatchScorer_impl_is_internal_to_Infrastructure()
    {
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var scorerTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == ScorerNamespace
                        && t.Name.Contains("MatchScorer", StringComparison.Ordinal))
            .ToList();

        // The scorer type must exist (else RED — production not written yet).
        scorerTypes.ShouldContain(
            t => t.Name.Contains("MatchScorer", StringComparison.Ordinal),
            "MatchScorer saknas i Jobbliggaren.Infrastructure.Matching " +
            "(F4-5 production-impl ej skriven än — väntad RED).");

        var publicScorerTypes = scorerTypes
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicScorerTypes.ShouldBeEmpty(
            "MatchScorer ska vara internal (CTO Decision 4 = V4-b; parity " +
            $"OccupationCodeDeriver). Public: {string.Join(", ", publicScorerTypes!)}");
    }

    [Fact]
    public void MatchScorer_impl_is_sealed()
    {
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var scorer = infrastructureAsm.GetTypes()
            .Single(t => t.Namespace == ScorerNamespace
                         && t.Name == "MatchScorer");

        scorer.IsSealed.ShouldBeTrue(
            "MatchScorer ska vara sealed (internal sealed, paritet OccupationCodeDeriver).");
    }

    [Fact]
    public void MatchScorer_impl_implements_the_port()
    {
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var scorer = infrastructureAsm.GetTypes()
            .Single(t => t.Namespace == ScorerNamespace
                         && t.Name == "MatchScorer");

        typeof(Jobbliggaren.Application.Matching.Abstractions.IMatchScorer)
            .IsAssignableFrom(scorer)
            .ShouldBeTrue(
                "MatchScorer ska implementera IMatchScorer (porten i Application).");
    }

    // ===============================================================
    // 5. Application MUST NOT depend on the NLP libraries through the scorer
    //    (the port surface stays BCL-only — Snowball lives only in Infrastructure)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries()
    {
        // Surfacing the dim-2 stemmed-title pass behind IMatchScorer must NOT drag
        // Snowball/WeCantSpell across the Application boundary — the scorer impl
        // (in Infrastructure) is the only thing that touches ITextAnalyzer's
        // Snowball-bound impl. Root namespaces: "Snowball" (libstemmer.net),
        // "WeCantSpell" (Hunspell port).
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek (Snowball/WeCantSpell) — F4-5 " +
            "match-porten ska vara BCL-only: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 6. Application MUST NOT depend on Npgsql/EF through the scorer
    //    (the scorer reads shadow columns via EF.Property in Infrastructure only —
    //    no EF/Npgsql leaks up to the port surface; parity ADR 0062)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_EfCore_or_Npgsql()
    {
        // Paritet ADR 0062 (FTS-LINQ kapslad i Infrastructure) + CLAUDE.md §2.1.
        // The scorer's shadow-column-load (EF.Property<string?>) lives in
        // Infrastructure; no EF/Npgsql type may cross into Application via the
        // new port. JobAdId (the only Domain type on the port) is BCL-bound.
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
    // 7. Domain MUST NOT depend on the scorer / the NLP libraries
    // ===============================================================

    [Fact]
    public void Domain_should_not_depend_on_Application_or_NLP_libraries()
    {
        // The deterministic match-scoring is an Application-/Infrastructure-
        // concern; Domain (inkl. JobAd-aggregatet + personnummer-guarden i
        // Domain/Privacy) förblir NLP-fri och Application-fri.
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
