using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

/// <summary>
/// PR-8b (8b.1) — <see cref="CvTemplate.AtsSafe"/> is the single honest source for the ATS-safety
/// verdict a template carries: the single-column templates (Klar, Accentlinje) parse cleanly
/// top-to-bottom; the two-column MorkPanel does not ("För människor"). It is advisory, never blocking
/// — an ATS-safe plain version is always generated in parallel. This pins the mapping so a new template
/// must classify itself.
/// </summary>
public class CvTemplateTests
{
    [Fact]
    public void AtsSafe_IsTrue_ForSingleColumnTemplates()
    {
        CvTemplate.Klar.AtsSafe.ShouldBeTrue();
        CvTemplate.Accentlinje.AtsSafe.ShouldBeTrue();
    }

    [Fact]
    public void AtsSafe_IsFalse_ForTheTwoColumnMorkPanel()
    {
        CvTemplate.MorkPanel.AtsSafe.ShouldBeFalse();
    }

    [Fact]
    public void AtsSafe_ClassifiesEveryTemplate_NoTemplateLeftUnclassified()
    {
        // The property is a total function over the closed set — every member returns a verdict
        // (a new template forces an explicit true/false here rather than a silent default).
        foreach (var template in CvTemplate.List)
        {
            _ = template.AtsSafe; // must not throw; the two asserts above pin the concrete verdicts.
        }

        CvTemplate.List.Count(t => t.AtsSafe).ShouldBe(2);
    }
}
