using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Lifecycle of a <see cref="ParsedResume"/> staging artifact (F4-8, ADR 0074).
/// The looser staging invariant ("a parse always materialises, even a degraded or
/// personnummer-flagged one") — deliberately distinct from the strict canonical
/// <c>Resume</c> invariants (CTO Decision 1). Promotion to a canonical <c>Resume</c>
/// is a later, user-confirmed step (F4-9/F4-10); F4-8 only persists the artifact in
/// <see cref="PendingReview"/>.
/// </summary>
public sealed class ParsedResumeStatus : SmartEnum<ParsedResumeStatus>
{
    /// <summary>Parsed and persisted, awaiting the user's review (and personnummer
    /// removal, if flagged) before it can be promoted.</summary>
    public static readonly ParsedResumeStatus PendingReview = new("PendingReview", 1);

    /// <summary>The user confirmed and promoted the import to a canonical
    /// <c>Resume</c> (transition built in F4-9/F4-10).</summary>
    public static readonly ParsedResumeStatus Promoted = new("Promoted", 2);

    /// <summary>The user rejected the import. Retained (soft-deleted) for audit
    /// until the staging-retention sweep prunes it (see tech-debt retention TD).</summary>
    public static readonly ParsedResumeStatus Discarded = new("Discarded", 3);

    private ParsedResumeStatus(string name, int value) : base(name, value) { }
}
