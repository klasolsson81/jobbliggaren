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
/// boundary (BCL-only port surface). The ISpellChecker consumer-allowlist
/// starts EMPTY (no F4-2 consumer) and is extended additively at F4-9 — the
/// YAGNI surface is observable and guarded, not silent (CTO fråga 3, bindande).
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
        // SnowballSwedishStemmer / SwedishTextAnalyzer / HunspellSwedishSpellChecker
        // (and any future helper) must be non-public — the Snowball/Hunspell-bound
        // code may not be referenced from Application/Api/Worker (ACL-isolation,
        // paritet med Taxonomy- och JobAdSearch-impl:erna).
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
    // 5. ISpellChecker consumer-allowlist — starts EMPTY (CTO fråga 3,
    //    bindande). Mirrors TaxonomyAclLayerTests.Only_*_consume_* but
    //    asserts ShouldBeEmpty() — the YAGNI port has no F4-2 consumer,
    //    so no type may inject it yet. Extended additively at F4-9.
    // ===============================================================

    [Fact]
    public void No_type_outside_the_impl_and_DI_consumes_ISpellChecker()
    {
        // Build the actual constructor-consumer list across Application AND
        // Infrastructure (the impl HunspellSwedishSpellChecker and the
        // AddTextAnalysis DI registration are NOT constructor-consumers of the
        // PORT — they construct the impl, they don't inject ISpellChecker). So
        // any constructor that takes ISpellChecker is a premature consumer.
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

        consumers.ShouldBeEmpty(
            "ISpellChecker har ingen F4-2-konsument (CTO fråga 3 — tom allowlist " +
            "bindande). Första konsument tråds in vid F4-9; uppdatera då detta " +
            $"arch-test ADDITIVT. Oväntade konsumenter nu: {string.Join(", ", consumers)}");
    }
}
