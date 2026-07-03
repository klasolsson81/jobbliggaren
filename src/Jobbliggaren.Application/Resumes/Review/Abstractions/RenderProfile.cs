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

/// <summary>
/// Fail-loud name validation for the <see cref="RenderProfile"/> query input (#478 Low). A single
/// SPOT the four query validators (review / improve / render-cv / render-resume) share, so "what is
/// a valid profile string" lives in one place next to the enum it guards.
/// </summary>
public static class RenderProfileNames
{
    // Enum.GetNames allocates a fresh array per call — cache the two member names once. Array.IndexOf
    // below uses EqualityComparer<string>.Default (ordinal, case-sensitive) and is allocation-free.
    private static readonly string[] Names = Enum.GetNames<RenderProfile>();

    /// <summary>True only when <paramref name="value"/> is the EXACT (case-sensitive) name of a
    /// <see cref="RenderProfile"/> member — "Ats" or "Visual". Unlike <c>Enum.TryParse</c> this
    /// rejects numeric strings ("2", and even "0"/"1" that map to defined members), undefined
    /// numeric values, and case variants ("ats"), so a bad profile is a caught client bug, never
    /// silently coerced into an undefined enum that yields an empty review/render (the fail-loud
    /// contract the validators promise). "Both" is a <c>RubricProfile</c> member, not a
    /// <see cref="RenderProfile"/>, so it is correctly rejected.</summary>
    public static bool IsValidName(string? value) =>
        value is not null && Array.IndexOf(Names, value) >= 0;
}
