using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.TextAnalysis;

/// <summary>
/// Every CV language has its text analysis and its rendered labels (#2341): a
/// <see cref="ResumeLanguage"/> added without them fails here, not in a review or a render.
/// </summary>
public class TextLanguageMapTests
{
    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in ResumeLanguage.List)
        {
            data.Add(language.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void For_ShouldCoverEveryResumeLanguage(string language)
    {
        var cvLanguage = ResumeLanguage.FromName(language);

        Should.NotThrow(() => TextLanguageMap.For(cvLanguage));
        Should.NotThrow(() => CvRenderStrings.For(cvLanguage));
    }

    [Fact]
    public void For_ShouldReadEachLanguageWithItsOwnAnalysis()
    {
        TextLanguageMap.For(ResumeLanguage.Sv).ShouldBe(TextLanguage.Swedish);
        TextLanguageMap.For(ResumeLanguage.En).ShouldBe(TextLanguage.English);
    }
}
