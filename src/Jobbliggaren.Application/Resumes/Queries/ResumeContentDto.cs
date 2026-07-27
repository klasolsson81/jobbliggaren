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
/// read-side personnummer redaction, and that is deliberate rather than an omission
/// (security-auditor, 2026-07-27). Three grounds, and the first is the one that actually
/// carries it: <b>every write surface that can put a non-null <c>Preamble</c> on a
/// <c>ResumeContent</c> either runs <c>ResumeContentPersonnummerGuard</c> over the value that
/// lands, or carries forward a value this guard already scanned at its only ingress</b>
/// (<c>UpdateMasterContent</c>, write-once) — the first enforced by a sink-keyed architecture
/// tripwire (<c>ResumeContentPersonnummerGuardTests</c>, #499/#650, fail-closed with an empty
/// exemption list) rather than by discipline. Three paths do NOT go through this DTO, and all
/// three are covered: <c>Resume.Create</c> via <c>ResumeContent.Empty</c> (its handler runs the
/// same guard on the name it passes, and <c>Preamble</c> is null by construction),
/// <c>UpdateMasterContent</c>'s carry-forward (already scanned), and <c>CreateTailored</c>
/// (nulls it deliberately).
///
/// Second, the staging arm is genuinely a different
/// guarantee class: a FLAGGED parse persists there (only promote is gated), so it needs
/// fail-closed suppression on read. Third, nothing else on this transport is read-redacted
/// either — <c>PersonalInfo.FullName</c>, <c>Summary</c> and every description travel verbatim;
/// the belt-and-braces sites (<c>GetResumeAtsText</c>) are DERIVED one-way views, a different
/// class from the content transport itself.</para>
/// <para>An earlier revision of this paragraph supported the same conclusion by counting
/// <c>new ResumeContent(</c> occurrences in <c>src/</c>. That count is accurate and does not
/// mean what it was made to mean — <c>ResumeContent.Empty</c> constructs via a target-typed
/// <c>new(...)</c>, which the grep cannot see. The guarantee never rested on an ingress count,
/// so it does not need one.</para>
/// <para>Adding a second redactor here would be two normalisers of the product's
/// highest-priority PII rule. And on the one hazard specific to this field — a personnummer
/// straddling a subtracted fragment, which <c>PreambleResidue.Subtract</c> can splice into a
/// string that is NOT a substring of <c>RawText</c> and that the import scan therefore never
/// saw — the canonical arm's write-side control is <b>stronger</b> than staging's primary one,
/// not weaker.</para>
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
