using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 9 (F4-9) anti-regression — the deterministic CV-review engine respects Clean
/// Architecture (ADR 0071/0074; CLAUDE.md §2.1/§5). The port <c>ICvReviewEngine</c> + the
/// result/evidence types + the <c>CriterionVerdict</c>/<c>RenderProfile</c> enums are
/// Application abstractions (in <c>Resumes/Review/Abstractions/</c>, beside the matching +
/// knowledge-bank ports); the impl <c>CvReviewEngine</c> and its <c>ICriterionRule</c>
/// implementations are <c>internal sealed</c> in
/// <c>Jobbliggaren.Infrastructure.Resumes.Review</c> (parity MatchScorer / RubricProvider).
///
/// The engine consumes the local NLP tier (Snowball via ITextAnalyzer) + the knowledge bank
/// in Infrastructure, but Application/Domain MUST NOT gain any NLP/Npgsql/EF dependency
/// through the port — the port surface stays BCL + Domain (ParsedResume) only.
///
/// Goodhart guard (CLAUDE.md §5, parity MatchScore / RubricCriterion): <c>CvReviewResult</c>
/// carries NO opaque total/score — pinned BY the property-name allowlist so the rejected
/// "single number" shape cannot creep in (the int counts AssessedCount/TotalCount are
/// explicitly allowed).
///
/// Mirrors MatchScorerLayerTests + KnowledgeBankLayerTests.
///
/// RED until ICvReviewEngine + the result types ship in Application and CvReviewEngine ships
/// internal sealed in Infrastructure.
/// </summary>
public class CvReviewEngineLayerTests
{
    private const string EngineNamespace = "Jobbliggaren.Infrastructure.Resumes.Review";

    private static readonly Assembly ApplicationAsm =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAsm =
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

    private static string[] PublicInstancePropNames(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToArray();

    // ===============================================================
    // 1. Port + result/evidence/enum types live in the Application assembly
    // ===============================================================

    [Fact]
    public void ICvReviewEngine_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.ICvReviewEngine);
        port.Assembly.ShouldBe(ApplicationAsm);
        port.Namespace.ShouldBe("Jobbliggaren.Application.Resumes.Review.Abstractions");
    }

    [Fact]
    public void Result_and_evidence_types_are_in_Application_layer()
    {
        foreach (var t in new[]
        {
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.CvReviewResult),
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.CvCategoryResult),
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.CvCriterionVerdict),
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.CitedEvidence),
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.TextSpan),
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.TextSpanEvidence),
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.StructuralEvidence),
        })
        {
            t.Assembly.ShouldBe(ApplicationAsm,
                $"{t.Name} ska ligga i Application (Resumes.Review.Abstractions).");
        }
    }

    [Fact]
    public void ICvReviewEngine_is_not_in_Infrastructure_assembly()
    {
        typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.ICvReviewEngine)
            .Assembly.ShouldNotBe(InfrastructureAsm);
    }

    // ===============================================================
    // 2. Locked enum sets (parity MatchDimensionVerdict / CriterionAssessability)
    // ===============================================================

    [Fact]
    public void CriterionVerdict_is_the_locked_four_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.Resumes.Review.Abstractions.CriterionVerdict>()
            .ShouldBe(["Pass", "Warn", "Fail", "NotAssessed"], ignoreOrder: true,
                "CriterionVerdict ska vara exakt { Pass, Warn, Fail, NotAssessed }.");
    }

    [Fact]
    public void RenderProfile_is_the_locked_two_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.Resumes.Review.Abstractions.RenderProfile>()
            .ShouldBe(["Ats", "Visual"], ignoreOrder: true);
    }

    // ===============================================================
    // 3. Goodhart guard pinned BY SHAPE — CvReviewResult has NO opaque total
    // ===============================================================

    [Fact]
    public void CvReviewResult_carries_no_opaque_total_only_counts_and_lists()
    {
        // CLAUDE.md §5 / parity MatchScore: a CV verdict is explainable — category counts +
        // per-category bands + the verdict lists, NOT a single opaque "score" number. The
        // int counts AssessedCount/TotalCount are allowed (they are honest denominators, not
        // a Goodhart score). Pinned by the exact property-name list.
        PublicInstancePropNames(
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.CvReviewResult))
            .ShouldBe(
                ["RubricVersion", "Profile", "Categories", "Verdicts", "CriticalFails",
                 "AssessedCount", "TotalCount"],
                ignoreOrder: true,
                "CvReviewResult får INTE bära en opak total/score (Goodhart, CLAUDE.md §5).");
    }

    [Fact]
    public void CvCriterionVerdict_carries_its_five_explainability_properties()
    {
        // CriterionId, Category, Verdict, Evidence, NotAssessedReason — the cited-evidence
        // contract (Inv.2). No numeric score property.
        PublicInstancePropNames(
            typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.CvCriterionVerdict))
            .ShouldBe(
                ["CriterionId", "Category", "Verdict", "Evidence", "NotAssessedReason"],
                ignoreOrder: true);
    }

    // ===============================================================
    // 4. The impl is internal sealed in Infrastructure (proven BY NAME so the
    //    RED state requires the type to EXIST and be non-public)
    // ===============================================================

    [Fact]
    public void CvReviewEngine_exists_in_Infrastructure_Resumes_Review()
    {
        var names = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == EngineNamespace)
            .Select(t => t.Name)
            .ToList();

        names.ShouldContain("CvReviewEngine",
            $"CvReviewEngine saknas i {EngineNamespace} (F4-9 production-impl ej skriven än — väntad RED).");
    }

    [Fact]
    public void Engine_and_rules_are_internal_to_Infrastructure()
    {
        // The whole Resumes.Review namespace (engine + ICriterionRule impls + context) is
        // internal — consumed via the port + DI only (parity MatchScorer / RubricProvider).
        var publicTypes = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == EngineNamespace)
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicTypes.ShouldBeEmpty(
            "CvReviewEngine + reglerna ska vara internal (parity MatchScorer / RubricProvider). " +
            $"Public: {string.Join(", ", publicTypes!)}");
    }

    [Fact]
    public void CvReviewEngine_is_sealed()
    {
        var engine = InfrastructureAsm.GetTypes()
            .Single(t => t.Namespace == EngineNamespace && t.Name == "CvReviewEngine");

        engine.IsSealed.ShouldBeTrue("CvReviewEngine ska vara sealed (internal sealed).");
    }

    [Fact]
    public void CvReviewEngine_implements_the_port()
    {
        var engine = InfrastructureAsm.GetTypes()
            .Single(t => t.Namespace == EngineNamespace && t.Name == "CvReviewEngine");

        typeof(Jobbliggaren.Application.Resumes.Review.Abstractions.ICvReviewEngine)
            .IsAssignableFrom(engine)
            .ShouldBeTrue("CvReviewEngine ska implementera ICvReviewEngine.");
    }

    // ===============================================================
    // 5. Application/Domain MUST NOT leak NLP / Npgsql / EF through the port
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries()
    {
        // Surfacing the review behind ICvReviewEngine must NOT drag Snowball/WeCantSpell
        // across the Application boundary — the engine impl (in Infrastructure) is the only
        // thing that touches the Snowball-bound ITextAnalyzer + the Hunspell spell-checker.
        var result = Types.InAssembly(ApplicationAsm)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek (Snowball/WeCantSpell) genom review-porten: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_EfCore_or_Npgsql()
    {
        var result = Types.InAssembly(ApplicationAsm)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "NpgsqlTypes",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Microsoft.EntityFrameworkCore.Relational")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot Npgsql/EF-relational genom review-porten (CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_should_not_depend_on_the_review_engine_or_NLP_libraries()
    {
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Application.Resumes.Review.Abstractions",
                "Jobbliggaren.Infrastructure.Resumes.Review",
                "Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot review-engine/NLP-bibliotek (Clean Arch, CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
