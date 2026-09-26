using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.UnitTests.Common.Behaviors;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Mediator;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Fluent;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

[Collection("QuestPdfRendering")]
public class QuestPdfConfigurationTests
{
    private const string Sentinel = "PRIVATE-CV-SENTINEL-1868";

    [Fact]
    public async Task Apply_LayoutFailure_DoesNotExposeDocumentTextToLogging()
    {
        var logger = Substitute.For<ILogger<LoggingBehavior<TestCommand, string>>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var behavior = new LoggingBehavior<TestCommand, string>(logger);
        MessageHandlerDelegate<TestCommand, string> next = (_, _) =>
        {
            RenderImpossibleLayout();
            return ValueTask.FromResult("unexpected success");
        };

        try
        {
            QuestPDF.Settings.EnableDetailedLayoutErrors = true;
            QuestPdfConfiguration.Apply();
            var safe = await Should.ThrowAsync<DocumentLayoutException>(() =>
                behavior.Handle(new TestCommand("render"), next, TestContext.Current.CancellationToken).AsTask());
            var logged = logger.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call => call.GetArguments()[3])
                .OfType<Exception>()
                .ShouldHaveSingleItem();
            logged.ShouldBeSameAs(safe);
            logged.ToString().ShouldNotContain(Sentinel);

            QuestPDF.Settings.EnableDetailedLayoutErrors = true;
            var detailed = Should.Throw<DocumentLayoutException>(RenderImpossibleLayout);
            detailed.ToString().ShouldContain(Sentinel);
        }
        finally
        {
            QuestPdfConfiguration.Apply();
        }
    }

    [Fact]
    public void Apply_MissingGlyph_PreservesPermissiveRendering()
    {
        QuestPdfConfiguration.Apply();
        var bytes = Document.Create(document => document.Page(page =>
            page.Content().Text("Svenska åäö \U0010FFFF").FontFamily("Lato"))).GeneratePdf();
        bytes.ShouldNotBeEmpty();
    }

    private static void RenderImpossibleLayout()
    {
        // Deliberately unreachable CV geometry exercises safe degradation of the library's
        // diagnostic path. The sentinel is synthetic; this is not a production-layout claim.
        Document.Create(document => document.Page(page =>
        {
            page.Size(100, 100);
            page.Content().Width(200).Text(Sentinel);
        })).GeneratePdf();
    }
}

