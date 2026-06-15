namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Strongly-typed identity for the <see cref="ParsedResume"/> staging aggregate
/// (F4-8, ADR 0074 — CV import &amp; parse). Distinct from <c>ResumeId</c>: a
/// parsed import has its own review/promote/discard lifecycle and is never the
/// canonical <c>Resume</c> (CTO Decision 1 = Variant A).
/// </summary>
public readonly record struct ParsedResumeId(Guid Value)
{
    public static ParsedResumeId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
