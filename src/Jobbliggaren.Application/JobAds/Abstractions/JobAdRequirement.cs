using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// An employer-stated structured requirement parsed from a JobTech ad
/// (<c>must_have</c>/<c>nice_to_have</c>) — F4-4b (ADR 0071/0074/0075). The ACL
/// (<c>PlatsbankenJobSource</c>) translates the already-taxonomy-linked JobTech
/// concept into this neutral Application transport; the extractor maps it to a
/// Domain <see cref="ExtractedTerm"/> with <see cref="ExtractedTermKind.Requirement"/>.
/// <para>
/// v1 scope (CTO Decision 1A): only the <c>skills</c> sub-array becomes a
/// requirement (its <see cref="ConceptId"/> shares the skill namespace with the
/// title/description skill extraction + a future parsed CV, so it is directly
/// comparable). <see cref="Source"/> is <see cref="ExtractedTermSource.MustHave"/>
/// or <see cref="ExtractedTermSource.NiceToHave"/>; <see cref="Weight"/> is the
/// JobTech relevance weight floored to a finite, non-negative value at the ACL
/// boundary (JobTech <c>weight</c> can be null).
/// </para>
/// </summary>
public sealed record JobAdRequirement(
    ExtractedTermSource Source,
    string ConceptId,
    string Label,
    double Weight);
