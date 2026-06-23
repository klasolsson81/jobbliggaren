using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// Derives the CV-attributed ~years of experience per SSYK-4 occupation group at import
/// (ADR 0079-amendment, exp-per-occ PR-2 — deterministic, NO AI/LLM). A SEPARATE attribution
/// pass that does NOT modify the shipped union deriver (<c>IOccupationCodeDeriver.DeriveManyAsync</c>
/// is closed for modification — OCP): the union pass deliberately discards the
/// experience-entry → group mapping, so this pass re-derives EACH experience entry's
/// group(s) from its Title + Organization and joins on concept-id to re-establish the link
/// the union dropped.
/// <para>
/// <b>Attribution (Klas-val, ADR 0079-amendment):</b> a group's ~years is the
/// <b>merged-interval union</b> of every contributing experience entry's parsed span
/// ("lifetime in the field"), never double-counting overlapping/concurrent roles. Year
/// granularity; "present" resolves to the injected clock's year (never <c>DateTime.Now</c>).
/// EXPERIENCE entries only — education periods are study years, never work experience, so an
/// education-sourced group is absent from the result (→ honest "not stated"/null at the
/// call-site). An entry with no parseable period contributes nothing.
/// </para>
/// <para>
/// <b>PII boundary (ADR 0074 Inv. 3):</b> the input <see cref="ParsedExperience"/> carries
/// CV-PII (Title/Organization/Period) but is only ever called inside the import DEK pipeline
/// that already holds the warmed owner key; only the non-PII result (concept-id + an integer)
/// is projected onto the staging artifact (the #159 ProposedSkill precedent).
/// </para>
/// </summary>
public interface IOccupationExperienceDeriver
{
    /// <summary>
    /// Returns a map of SSYK-4 group concept-id → ~years of experience, present ONLY for
    /// groups with at least one experience entry carrying a parseable period. An absent
    /// concept-id means "not stated" (null at the call-site). Never throws on unparseable
    /// input; never auto-confirms; persists nothing.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, int>> DeriveApproximateYearsAsync(
        IReadOnlyList<ParsedExperience> experiences, CancellationToken cancellationToken);
}
