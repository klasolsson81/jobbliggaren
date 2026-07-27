namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for the canonical CV content. The Fas 4b AppCopy superset fields
/// (<see cref="Languages"/>, <see cref="SkillGroups"/>, <see cref="Sections"/>, ADR 0095)
/// are <b>optional with a null default</b> so a pre-superset client (which omits them)
/// deserialises cleanly; the mapper coalesces null to an empty list. The four original
/// fields keep their established all-required contract.
///
/// <para><b>The preamble travels on the CONTENT transport, on both lifecycle arms, and is
/// never added to <c>CvReviewDto</c>.</b> Staging already carries it on
/// <c>ParsedResumeDetailDto.Preamble</c>, so this DTO is the symmetric home and the promoted
/// review page reads it off the fetch it already makes. The review DTO is shared by both arms
/// and its own contract requires that a field mean the same thing on both paths, so putting it
/// there would force the staging arm to carry the same string twice on one page. The review
/// ENGINE gets it through <c>CvReviewContext</c>/<c>ReviewableCv</c>, never through a DTO — so
/// no review transport ever needs it.</para>
/// </summary>
/// <param name="Preamble">
/// #1060 — the verbatim, UNCLASSIFIED text an IMPORTED CV carried above its first heading
/// (#844, ADR 0109). Null on a template-origin CV and on legacy content. Display-only: shown
/// back under a neutral label that does not claim it is a profile, never rendered into
/// <c>/render</c> or the ATS view, never graded.
/// <para>Unlike the staging egress (<c>GetParsedResumeMapper</c>), this one carries NO
/// read-side personnummer redaction, and that is deliberate rather than an omission. The two
/// arms are not in the same guarantee class: a flagged parse PERSISTS (only promote is gated),
/// so staging needs fail-closed suppression on read; canonical content is guaranteed clean at
/// the WRITE boundary by <c>ResumeContentPersonnummerGuard</c>, which is architecture-enforced
/// on every content write surface and now scans this field too. Adding a second redactor here
/// would be two normalisers of the product's highest-priority PII rule.</para>
/// </param>
public sealed record ResumeContentDto(
    PersonalInfoDto PersonalInfo,
    IReadOnlyList<ExperienceDto> Experiences,
    IReadOnlyList<EducationDto> Educations,
    IReadOnlyList<SkillDto> Skills,
    string? Summary,
    IReadOnlyList<SpokenLanguageDto>? Languages = null,
    IReadOnlyList<SkillGroupDto>? SkillGroups = null,
    IReadOnlyList<ResumeSectionDto>? Sections = null,
    string? Preamble = null);
