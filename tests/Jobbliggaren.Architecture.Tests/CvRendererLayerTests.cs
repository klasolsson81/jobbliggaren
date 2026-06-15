using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 10 (F4-10) Phase B anti-regression — the QuestPDF CV renderer respects Clean
/// Architecture (ADR 0071/0074; CLAUDE.md §2.1). The port <c>ICvRenderer</c> + <c>RenderedCv</c>
/// are Application abstractions (BCL-only); the impl <c>CvRenderer</c> + <c>CvDocumentComposer</c>
/// (and the Phase A helpers WcagContrast/CvPalette/CvDocumentModel/CvRenderStrings) are
/// <c>internal sealed</c> in <c>Jobbliggaren.Infrastructure.Resumes.Rendering</c>. The QuestPDF
/// SDK MUST stay confined to Infrastructure (parity the PdfPig/OpenXml confinement) — Application
/// + Domain never reference it. Mirrors <see cref="CvImprovementEngineLayerTests"/>.
/// </summary>
public class CvRendererLayerTests
{
    private const string RenderingNamespace = "Jobbliggaren.Infrastructure.Resumes.Rendering";

    private static readonly Assembly ApplicationAsm =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAsm =
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly DomainAsm =
        typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly;

    [Fact]
    public void CvRenderer_exists_in_Infrastructure_Resumes_Rendering()
    {
        InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == RenderingNamespace)
            .Select(t => t.Name)
            .ShouldContain("CvRenderer",
                $"CvRenderer saknas i {RenderingNamespace} (F4-10 Phase B production-impl ej skriven än — väntad RED).");
    }

    [Fact]
    public void CvRenderer_is_internal_sealed_and_implements_the_port()
    {
        var renderer = InfrastructureAsm.GetTypes()
            .Single(t => t.Namespace == RenderingNamespace && t.Name == "CvRenderer");

        renderer.IsSealed.ShouldBeTrue("CvRenderer ska vara sealed.");
        (renderer.IsPublic || (renderer.IsNested && renderer.IsNestedPublic)).ShouldBeFalse(
            "CvRenderer ska vara internal (Infrastructure-detalj).");
        typeof(Jobbliggaren.Application.Resumes.Rendering.Abstractions.ICvRenderer)
            .IsAssignableFrom(renderer)
            .ShouldBeTrue("CvRenderer ska implementera ICvRenderer.");
    }

    [Fact]
    public void Whole_Rendering_namespace_is_internal()
    {
        // CvRenderer + CvDocumentComposer + WcagContrast + CvPalette + CvDocumentModel +
        // CvRenderStrings are all internal — consumed via the port + DI only (parity CvReviewEngine).
        var publicTypes = InfrastructureAsm.GetTypes()
            .Where(t => t.Namespace == RenderingNamespace)
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicTypes.ShouldBeEmpty(
            $"Rendering-namespace ska vara helt internal. Public: {string.Join(", ", publicTypes!)}");
    }

    [Fact]
    public void Application_and_Domain_must_not_depend_on_QuestPDF()
    {
        var application = Types.InAssembly(ApplicationAsm)
            .ShouldNot().HaveDependencyOn("QuestPDF").GetResult();
        application.IsSuccessful.ShouldBeTrue(
            "Application får inte bero på QuestPDF (renderaren bor i Infrastructure): " +
            $"{string.Join(", ", application.FailingTypeNames ?? [])}");

        var domain = Types.InAssembly(DomainAsm)
            .ShouldNot().HaveDependencyOn("QuestPDF").GetResult();
        domain.IsSuccessful.ShouldBeTrue(
            "Domain får inte bero på QuestPDF: " + string.Join(", ", domain.FailingTypeNames ?? []));
    }

    [Fact]
    public void Port_and_result_are_in_Application_layer()
    {
        foreach (var t in new[]
        {
            typeof(Jobbliggaren.Application.Resumes.Rendering.Abstractions.ICvRenderer),
            typeof(Jobbliggaren.Application.Resumes.Rendering.Abstractions.RenderedCv),
        })
        {
            t.Assembly.ShouldBe(ApplicationAsm, $"{t.Name} ska ligga i Application.");
            t.Namespace.ShouldBe("Jobbliggaren.Application.Resumes.Rendering.Abstractions");
        }
    }
}
