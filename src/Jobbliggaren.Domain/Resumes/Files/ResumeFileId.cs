namespace Jobbliggaren.Domain.Resumes.Files;

/// <summary>
/// Strongly-typed identity for the <see cref="ResumeFile"/> aggregate (Fas 4b PR-9a,
/// ADR 0093 §D5 — original-file binary store). Its own aggregate (not a <c>Resume</c>
/// child) because a stored original has its own lifecycle: retention-coupled to the
/// parsed sibling, Art. 17-cascade-owned, and the future export-history axis (D9).
/// </summary>
public readonly record struct ResumeFileId(Guid Value)
{
    public static ResumeFileId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
