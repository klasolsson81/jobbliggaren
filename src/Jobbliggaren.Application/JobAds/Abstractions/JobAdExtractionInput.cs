namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Input to <see cref="IJobAdKeywordExtractor"/>: the public job-ad text the
/// deterministic extraction reads, plus the employer-stated structured
/// <see cref="Requirements"/> (F4-4b). Carries no <c>raw_payload</c> and no PII —
/// the extractor reads only the already-public headline + free-text description
/// (ADR 0074 invariant 3 does not bite) and pre-linked taxonomy requirement
/// concepts. Application-layer transport record (CLAUDE.md §3.3); the extractor
/// builds the Domain <c>ExtractedTerms</c> from it.
/// <para>
/// v1 (Path C / F4-4b) extracts skills/keywords from <see cref="Title"/> +
/// <see cref="Description"/> and requirement terms from <see cref="Requirements"/>.
/// The 2-arg constructor defaults <see cref="Requirements"/> to empty — the F4-4
/// local backfill (and any source without structured requirements) stays
/// keyword/skill-only, no rework.
/// </para>
/// </summary>
public sealed record JobAdExtractionInput(
    string Title,
    string Description,
    IReadOnlyList<JobAdRequirement> Requirements)
{
    /// <summary>Back-compatible 2-arg construction (no structured requirements).</summary>
    public JobAdExtractionInput(string Title, string Description)
        : this(Title, Description, [])
    {
    }
}
