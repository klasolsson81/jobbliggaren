namespace Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — the count of background matches new since the user last opened the
/// matches view. <c>Count == 0</c> when there is no authenticated user, no JobSeeker, or nothing
/// new since the last-seen watermark (all honest). Grade-neutral count (never a magnitude).
/// </summary>
public sealed record MyNewMatchCountDto(int Count)
{
    public static readonly MyNewMatchCountDto Zero = new(0);
}
