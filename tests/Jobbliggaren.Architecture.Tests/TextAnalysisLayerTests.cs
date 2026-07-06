using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 2 (F4-2) anti-regression — the local NLP-tier respects Clean
/// Architecture (ADR 0074; CTO-dom in-block 4; architect §5). The three ports
/// (<c>IStemmer</c>, <c>ITextAnalyzer</c>, <c>ISpellChecker</c>) + the
/// <c>TextLanguage</c> enum are Application abstractions (mirroring
/// ITaxonomyReadModel / IJobAdSearchQuery); the three impls are
/// <c>internal sealed</c> in Infrastructure; the NLP libraries (Snowball,
/// WeCantSpell.Hunspell) MUST NOT leak across the Application or Domain
/// boundary (BCL-only port surface).
///
/// <para>F4-9 update: the impls are renamed to the language-agnostic
/// <c>SnowballStemmer</c> / <c>LocalTextAnalyzer</c> / <c>HunspellSpellChecker</c>
/// (English support is wired at F4-8/9, TextLanguage contract). The internal-to-
/// Infrastructure namespace scan below is by NAMESPACE (<c>Jobbliggaren.Infrastructure
/// .TextAnalysis</c>), not by type name, so the rename keeps it green without edits.
/// The ISpellChecker consumer-allowlist is now EXACTLY <c>{CvReviewEngine}</c>: Fas 4b PR-6a
/// (#655) added the SEPARATE C7 spelling criterion (Stavning maskinell kontroll), whose rule
/// reads the checker through the engine — the first and only legitimate consumer. C1 (genuine
/// spelling+grammar) STAYS NotAssessedV1 (ADR 0071 OQ3); C7 never claims the grammar half nor
/// the critical slot. The allowlist stays a closed, observable set (CTO fråga 3, bindande) —
/// extend ADDITIVELY, never widen to a blanket rule.</para>
///
/// Mirrors TaxonomyAclLayerTests + JobAdSearchLayerTests.
/// </summary>
public class TextAnalysisLayerTests
{
    // ===============================================================
    // 1. Ports + TextLanguage live in the Application assembly
    // ===============================================================

    [Fact]
    public void IStemmer_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.Common.Abstractions.TextAnalysis.IStemmer);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void ITextAnalyzer_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.Common.Abstractions.TextAnalysis.ITextAnalyzer);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void ISpellChecker_is_in_Application_layer()
    {
        var port = typeof(Jobbliggaren.Application.Common.Abstractions.TextAnalysis.ISpellChecker);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void TextLanguage_is_in_Application_layer_not_Domain()
    {
        // CTO amendment fråga 2 — TextLanguage is an NLP-tier contract concept
        // (analysis policy), not a domain invariant. It lives beside the ports.
        var textLanguage = typeof(Jobbliggaren.Application.Common.Abstractions.TextAnalysis.TextLanguage);
        textLanguage.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 2. Impls are internal to Infrastructure (no public NLP types)
    // ===============================================================

    [Fact]
    public void TextAnalysis_impls_are_internal_to_Infrastructure()
    {
        // SnowballStemmer / LocalTextAnalyzer / HunspellSpellChecker (F4-9 rename from the
        // Swedish-only F4-2 names; and any future helper) must be non-public — the
        // Snowball/Hunspell-bound code may not be referenced from Application/Api/Worker
        // (ACL-isolation, paritet med Taxonomy- och JobAdSearch-impl:erna). Scanned by
        // NAMESPACE so the rename does not break this guard.
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var publicTextAnalysisTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "Jobbliggaren.Infrastructure.TextAnalysis"
                        && (t.IsPublic || (t.IsNested && t.IsNestedPublic)))
            .Select(t => t.FullName)
            .ToList();

        publicTextAnalysisTypes.ShouldBeEmpty(
            "NLP-tier-impl:erna ska vara internal (ACL-isolation, ADR 0074). " +
            $"Public: {string.Join(", ", publicTextAnalysisTypes!)}");
    }

    // ===============================================================
    // 3. Application MUST NOT depend on the NLP libraries (BCL-only ports)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries()
    {
        // The single most important new watchdog — proves the BCL-only port
        // surface. Snowball.* (libstemmer.net) and WeCantSpell.Hunspell.* must
        // never cross the Application boundary; only Infrastructure impls touch
        // them. Root namespaces: "Snowball" (libstemmer.net), "WeCantSpell"
        // (Hunspell port).
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek (Snowball/WeCantSpell): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 4. Domain MUST NOT depend on the NLP libraries
    // ===============================================================

    [Fact]
    public void Domain_should_not_depend_on_NLP_libraries()
    {
        // The NLP tier may NEVER touch Domain. The personnummer-guard in
        // Domain/Privacy is pure regex/BCL and stays NLP-free.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain läcker mot NLP-bibliotek (Snowball/WeCantSpell): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 5. ISpellChecker consumer-allowlist — EXACTLY {CvReviewEngine} at Fas 4b
    //    PR-6a (CTO fråga 3, bindande — a CLOSED, named set, not a blanket rule).
    //    Mirrors TaxonomyAclLayerTests.Only_*_consume_*. The C7 spelling criterion
    //    (#655) is the first legitimate consumer: its rule reads the checker through
    //    the engine's ctor-injected ISpellChecker. C1 (genuine spelling+grammar)
    //    STAYS NotAssessedV1 (ADR 0071 OQ3) — C7 is the SEPARATE machine-spelling
    //    criterion, not C1's promotion. Extend this allowlist ADDITIVELY, never widen.
    // ===============================================================

    [Fact]
    public void Only_the_review_engine_consumes_ISpellChecker()
    {
        // Build the actual constructor-consumer list across Application AND
        // Infrastructure (the impl HunspellSpellChecker and the
        // AddTextAnalysis DI registration are NOT constructor-consumers of the
        // PORT — they construct the impl, they don't inject ISpellChecker). The
        // ONLY legitimate consumer is CvReviewEngine (the C7 spelling criterion);
        // any OTHER constructor that takes ISpellChecker is a premature/unexpected consumer.
        var port = typeof(Jobbliggaren.Application.Common.Abstractions.TextAnalysis.ISpellChecker);

        var assemblies = new[]
        {
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
        };

        var consumers = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == port)))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        // EXACTLY the two legitimate C7 consumers (ordered by name):
        //   - CvReviewEngine: the DI consumer — ctor-injects the ISpellChecker port.
        //   - CriterionEvaluationContext: the internal per-criterion bundle whose primary ctor
        //     CARRIES the already-resolved checker to the rules (parity how Analyzer/Cliches/Verbs
        //     reach the rules — not a DI injection, a data-carrier record the engine builds).
        // Any OTHER type taking ISpellChecker in a ctor is a premature/unexpected coupling.
        consumers.ShouldBe(
            ["CriterionEvaluationContext", "CvReviewEngine"],
            "ISpellChecker har EXAKT två legitima konsumenter (C7 Stavning, #655): CvReviewEngine " +
            "(DI) + CriterionEvaluationContext (bär checkern till reglerna). C7 är den SEPARATA " +
            "maskinella stavningskriterien — C1 stannar NotAssessedV1 (ADR 0071 OQ3). En annan " +
            "konsument är en oväntad koppling; utöka allowlisten ADDITIVT (namngiven mängd, " +
            $"aldrig en blankett-regel). Faktiska konsumenter: {string.Join(", ", consumers)}");
    }
}
