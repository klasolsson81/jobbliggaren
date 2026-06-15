namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// Which rendering profile the CV is reviewed for (Fas 4 STEG 9, F4-9). Selects both
/// which rubric criteria are in scope and which category-weight blend applies
/// (BUILD §8.1 — separate ATS-optimised vs visual profiles, content criteria shared):
/// <list type="bullet">
/// <item><see cref="Ats"/> — assesses criteria with profile <c>Both</c> or <c>AtsOnly</c>
/// (Content/Structure/Language/ATS), blended via the rubric's ATS category weights.</item>
/// <item><see cref="Visual"/> — assesses criteria with profile <c>Both</c> or
/// <c>VisualOnly</c> (Content/Structure/Language/Visual), blended via the visual weights.</item>
/// </list>
/// </summary>
public enum RenderProfile
{
    Ats,
    Visual,
}
