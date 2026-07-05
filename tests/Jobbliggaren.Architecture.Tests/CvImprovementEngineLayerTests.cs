using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 10 (F4-10) anti-regression — the deterministic CV-build/improve engine respects
/// Clean Architecture (ADR 0071/0074; CLAUDE.md §2.1/§5). Mirrors
/// <see cref="CvReviewEngineLayerTests"/>. The port <c>ICvImprovementEngine</c> + the
/// result/change/operation/provenance types + the <c>ProposedChangeKind</c>/
/// <c>StructuralTransformKind</c> enums are Application abstractions (in
/// <c>Resumes/Improvement/Abstractions/</c>); the impl <c>CvImprovementEngine</c> and its
/// <c>ICvTransform</c> implementations + context are <c>internal sealed</c> in
/// <c>Jobbliggaren.Infrastructure.Resumes.Improvement</c> (parity CvReviewEngine).
///
/// Load-bearing pins:
///   - the CLOSED THREE-arm provenance union (<c>ChangeProvenance</c> abstract, EXACTLY
///     KnowledgeBankProvenance + StructuralTransformProvenance + UserParameterizedFrameProvenance,
///     all sealed, all in Application) — the no-free-text-arm guarantee (CLAUDE.md §5: no synthesised
///     prose). The third arm (Fas 4b PR-7, ADR 0093 §D2's deliberate widening) is STILL closed: a
///     frame After is a mechanical substitution of user-selected, mechanically-verified inputs into a
///     versioned template, never free prose;
///   - the locked enum member sets (10 + 7);
///   - <c>ProposedChange</c> public-prop allowlist (exactly 9) — no opaque score;
///   - <c>CvImprovementResult</c> public-prop allowlist (exactly 5) — no opaque total (Goodhart);
///   - Application + Domain MUST NOT leak NLP/Npgsql/EF — NOR QuestPDF — through the new port
///     (the QuestPDF guard passes in Phase A and protects Phase B);
///   - <c>ICvRenderer</c>/<c>RenderedCv</c> are Application abstractions; <c>WcagContrast</c>/
///     <c>CvPalette</c>/<c>CvDocumentModel</c>/<c>CvRenderStrings</c> are internal in
///     <c>Infrastructure.Resumes.Rendering</c>.
///
/// RED until the F4-10 Application contract + the Infrastructure impls ship.
/// </summary>
public class CvImprovementEngineLayerTests
{
    private const string EngineNamespace = "Jobbliggaren.Infrastructure.Resumes.Improvement";
    private const string RenderingNamespace = "Jobbliggaren.Infrastructure.Resumes.Rendering";
    private const string ImprovementAbstractions = "Jobbliggaren.Application.Resumes.Improvement.Abstractions";
    private const string RenderingAbstractions = "Jobbliggaren.Application.Resumes.Rendering.Abstractions";

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
    // 1. Port + result/change/provenance/enum types live in Application
    // ===============================================================

    [Fact]
    public void ICvImprovementEngine_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ICvImprovementEngine);
        port.Assembly.ShouldBe(ApplicationAsm);
        port.Namespace.ShouldBe(ImprovementAbstractions);
    }

    [Fact]
    public void Result_change_and_provenance_types_are_in_Application_layer()
    {
        foreach (var t in new[]
        {
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.CvImprovementResult),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ProposedChange),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ProposedReplacement),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.StructuralOperation),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ChangeProvenance),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.KnowledgeBankProvenance),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.StructuralTransformProvenance),
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.UserParameterizedFrameProvenance),
        })
        {
            t.Assembly.ShouldBe(ApplicationAsm,
                $"{t.Name} ska ligga i Application (Resumes.Improvement.Abstractions).");
        }
    }

    [Fact]
    public void ICvImprovementEngine_is_not_in_Infrastructure_assembly()
    {
        typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ICvImprovementEngine)
            .Assembly.ShouldNotBe(InfrastructureAsm);
    }

    // ===============================================================
    // 2. Locked enum sets (10 ProposedChangeKind + 7 StructuralTransformKind)
    // ===============================================================

    [Fact]
    public void ProposedChangeKind_is_the_locked_ten_member_set()
    {
        // Fas 4b PR-7 (#656, ADR 0093 §D2): FrameRewrite is the tenth member — a weak/unmeasured line
        // rewritten via a deterministic sentence/measure frame from user-selected, mechanically
        // verified inputs (A1/A2/C3). Still a locked set, one member per deterministic transform.
        Enum.GetNames<Jobbliggaren.Application.Resumes.Improvement.Abstractions.ProposedChangeKind>()
            .ShouldBe(
                ["ClicheReplacement", "WeakVerbUpgrade", "DateNormalization", "SectionReorder",
                 "HeadingNormalization", "PersonnummerStrip", "PhotoStrip", "GpaStrip",
                 "AtsSanitization", "FrameRewrite"],
                ignoreOrder: true,
                "ProposedChangeKind ska vara exakt de tio låsta medlemmarna.");
    }

    [Fact]
    public void StructuralTransformKind_is_the_locked_seven_member_set()
    {
        Enum.GetNames<Jobbliggaren.Application.Resumes.Improvement.Abstractions.StructuralTransformKind>()
            .ShouldBe(
                ["ReformatDate", "NormalizeHeadingCase", "RemovePersonnummer", "RemovePhotoReference",
                 "RemoveGpa", "StripNonStandardChars", "ReorderSection"],
                ignoreOrder: true,
                "StructuralTransformKind ska vara exakt de sju låsta medlemmarna.");
    }

    // ===============================================================
    // 3. The CLOSED three-arm provenance union — no free-text arm (§5 no-synthesis pin)
    // ===============================================================

    [Fact]
    public void ChangeProvenance_is_abstract()
    {
        typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ChangeProvenance)
            .IsAbstract.ShouldBeTrue("ChangeProvenance ska vara abstrakt (en sluten union-bas).");
    }

    [Fact]
    public void ChangeProvenance_has_exactly_the_three_sealed_subtypes_in_Application()
    {
        // The closed-union no-free-text-arm guarantee: provenance can ONLY be a KB pointer, a
        // structural-transform pointer, or a user-parameterized frame pointer (FrameId + verb +
        // the user's mechanically-verified slot inputs) — there is no "free text rationale" arm
        // by which the determinism could smuggle in synthesised prose (CLAUDE.md §5). The third
        // arm is ADR 0093 §D2's DELIBERATE widening (Fas 4b PR-7), still closed by shape.
        var baseType = typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ChangeProvenance);

        var subtypes = ApplicationAsm.GetTypes()
            .Where(t => t != baseType && baseType.IsAssignableFrom(t))
            .ToList();

        subtypes.Select(t => t.Name).ShouldBe(
            ["KnowledgeBankProvenance", "StructuralTransformProvenance", "UserParameterizedFrameProvenance"],
            ignoreOrder: true,
            "ChangeProvenance ska ha EXAKT de tre sealed-armarna (ingen fri-text-arm).");
        subtypes.ShouldAllBe(t => t.IsSealed, "Alla tre provenance-armarna ska vara sealed.");
    }

    // ===============================================================
    // 4. Goodhart / allowlist pins — ProposedChange (9) + CvImprovementResult (5)
    // ===============================================================

    [Fact]
    public void ProposedChange_carries_exactly_its_nine_explainability_properties()
    {
        PublicInstancePropNames(
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ProposedChange))
            .ShouldBe(
                ["TargetId", "Kind", "Category", "CriterionId", "Evidence", "Replacement",
                 "Operation", "Rationale", "Provenance"],
                ignoreOrder: true,
                "ProposedChange ska bära exakt de nio properties (ingen opak score/total).");
    }

    [Fact]
    public void CvImprovementResult_carries_no_opaque_total_only_versions_profile_and_changes()
    {
        // Parity CvReviewResult: an explainable propose-result is the stamped KB/rubric
        // versions + the profile + the change list — NOT a single opaque "improvement score".
        PublicInstancePropNames(
            typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.CvImprovementResult))
            .ShouldBe(
                ["ClicheListVersion", "VerbMappingVersion", "RubricVersion", "Profile", "Changes"],
                ignoreOrder: true,
                "CvImprovementResult får INTE bära en opak total (Goodhart, CLAUDE.md §5).");
    }

    // ===============================================================
    // 5. The impl is internal sealed in Infrastructure (proven BY NAME ⇒ RED needs it to exist)
    // ===============================================================

    [Fact]
    public void CvImprovementEngine_exists_in_Infrastructure_Resumes_Improvement()
    {
        var names = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == EngineNamespace)
            .Select(t => t.Name)
            .ToList();

        names.ShouldContain("CvImprovementEngine",
            $"CvImprovementEngine saknas i {EngineNamespace} (F4-10 production-impl ej skriven än — väntad RED).");
    }

    [Fact]
    public void Engine_transforms_and_context_are_internal_to_Infrastructure()
    {
        // The whole Resumes.Improvement namespace (engine + ICvTransform impls + context) is
        // internal — consumed via the port + DI only (parity CvReviewEngine).
        var publicTypes = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == EngineNamespace)
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicTypes.ShouldBeEmpty(
            "CvImprovementEngine + transforms + context ska vara internal (parity CvReviewEngine). " +
            $"Public: {string.Join(", ", publicTypes!)}");
    }

    [Fact]
    public void CvImprovementEngine_is_sealed()
    {
        var engine = InfrastructureAsm.GetTypes()
            .Single(t => t.Namespace == EngineNamespace && t.Name == "CvImprovementEngine");

        engine.IsSealed.ShouldBeTrue("CvImprovementEngine ska vara sealed (internal sealed).");
    }

    [Fact]
    public void CvImprovementEngine_implements_the_port()
    {
        var engine = InfrastructureAsm.GetTypes()
            .Single(t => t.Namespace == EngineNamespace && t.Name == "CvImprovementEngine");

        typeof(Jobbliggaren.Application.Resumes.Improvement.Abstractions.ICvImprovementEngine)
            .IsAssignableFrom(engine)
            .ShouldBeTrue("CvImprovementEngine ska implementera ICvImprovementEngine.");
    }

    // ===============================================================
    // 6. Application/Domain MUST NOT leak NLP / Npgsql / EF / QuestPDF through the port
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries_through_the_improvement_port()
    {
        var result = Types.InAssembly(ApplicationAsm)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek genom improvement-porten: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_EfCore_or_Npgsql_through_the_improvement_port()
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
            "Application läcker mot Npgsql/EF-relational genom improvement-porten (CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_and_Domain_should_not_depend_on_QuestPDF()
    {
        // Phase A guard that protects Phase B: the QuestPDF IDocument renderer must stay
        // confined to Infrastructure — Application owns only ICvRenderer/RenderedCv (BCL).
        // This passes today (no QuestPDF reference anywhere) and red-flags the moment a Phase
        // B renderer drags QuestPDF across the Application/Domain boundary.
        var application = Types.InAssembly(ApplicationAsm)
            .ShouldNot().HaveDependencyOn("QuestPDF").GetResult();
        application.IsSuccessful.ShouldBeTrue(
            "Application får inte bero på QuestPDF (renderaren bor i Infrastructure, Phase B): " +
            $"{string.Join(", ", application.FailingTypeNames ?? [])}");

        var domain = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot().HaveDependencyOn("QuestPDF").GetResult();
        domain.IsSuccessful.ShouldBeTrue(
            "Domain får inte bero på QuestPDF: " +
            $"{string.Join(", ", domain.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_should_not_depend_on_the_improvement_or_rendering_namespaces()
    {
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ImprovementAbstractions,
                RenderingAbstractions,
                EngineNamespace,
                RenderingNamespace,
                "Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot improvement/rendering-namespace eller NLP-bibliotek (Clean Arch, CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 7. Renderer BCL-only parts (Phase A) — ports in Application, helpers internal in Infra
    // ===============================================================

    [Fact]
    public void ICvRenderer_and_RenderedCv_are_in_Application_layer()
    {
        foreach (var t in new[]
        {
            typeof(Jobbliggaren.Application.Resumes.Rendering.Abstractions.ICvRenderer),
            typeof(Jobbliggaren.Application.Resumes.Rendering.Abstractions.RenderedCv),
        })
        {
            t.Assembly.ShouldBe(ApplicationAsm,
                $"{t.Name} ska ligga i Application (Resumes.Rendering.Abstractions).");
            t.Namespace.ShouldBe(RenderingAbstractions);
        }
    }

    [Fact]
    public void Rendering_helpers_are_internal_to_Infrastructure()
    {
        // WcagContrast / CvPalette / CvDocumentModel / CvRenderStrings are internal helpers in
        // Infrastructure.Resumes.Rendering. (The CvRenderer/IDocument internal-sealed checks
        // are Phase B — intentionally omitted here so this file references NO QuestPDF type.)
        foreach (var typeName in new[]
        {
            "WcagContrast", "CvPalette", "CvDocumentModel", "CvRenderStrings",
        })
        {
            var type = InfrastructureAsm.GetTypes()
                .SingleOrDefault(t => t.Namespace == RenderingNamespace && t.Name == typeName);

            type.ShouldNotBeNull(
                $"{typeName} saknas i {RenderingNamespace} (F4-10 Phase A ej skriven än — väntad RED).");
            (type!.IsPublic || (type.IsNested && type.IsNestedPublic)).ShouldBeFalse(
                $"{typeName} ska vara internal (Infrastructure-detalj).");
        }
    }
}
