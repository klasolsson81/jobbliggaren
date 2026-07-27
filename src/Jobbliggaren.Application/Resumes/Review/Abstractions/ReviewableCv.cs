namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The unified, source-agnostic content view the review rules read (Fas 4b PR-4,
/// ADR 0093 §D8 — "reviewable content", the first leg of the D8 triple). A superset
/// projection over BOTH source shapes: the tolerant staging <c>ParsedResumeContent</c>
/// and the strict canonical <c>ResumeContent</c>. Member names follow the staging
/// content's established rule-surface vocabulary (Contact/Profile/Experience/Education).
/// CV-PII in transit — never persisted, never logged (ADR 0074 Invariant 3).
/// </summary>
/// <param name="Preamble">
/// #844 — text the source CV carried ABOVE its first heading that no contact extractor claimed:
/// verbatim, UNCLASSIFIED, and asserted to be nothing. A rule may only ask WHETHER it exists (A8
/// withdraws its "Profiltext saknas helt." claim when it does), never treat it as prose.
///
/// <para><b>It must never enter <c>ReviewText.AllProse</c>.</b> That corpus feeds A7 (clichés), A9
/// (soft skills) and the language rules — grading an address block or OCR noise as the user's
/// writing is exactly the misclaim this field exists to prevent, and routing it there would
/// reintroduce, through the back door, the auto-classification the design refused.</para>
///
/// <para>On the CANONICAL arm, <c>null</c> for a TEMPLATE-origin CV — by construction, not from
/// inability: an app-built CV is emitted by the linearizer with every section under a heading
/// (ADR 0097 §2), so it has no region above its first one. An IMPORT-origin CV does have one, and
/// since #1060 it rides <c>ResumeContent.Preamble</c>, so ADR 0109 §5's table applies identically
/// on both arms and A8 never claims a summary "saknas helt" about text the product is holding.</para>
///
/// <para>It is NOT part of the canonical arm's <c>LinearText</c>, so a rule cannot cite a span
/// inside it there. A8 is structural-only and unaffected; a future rule that wants to quote the
/// preamble must decide deliberately to put it in the citation substrate, which is a decision
/// about grading unclassified text and therefore ADR 0109 §1's subject.</para>
/// </param>
public sealed record ReviewableCv(
    ReviewableContact? Contact,
    string? Profile,
    IReadOnlyList<ReviewableExperience> Experience,
    IReadOnlyList<ReviewableEducation> Education,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages,
    string? Preamble);

/// <summary>Contact fields, all optional — staging tolerates gaps; B3 verdicts on them.</summary>
public sealed record ReviewableContact(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location);

/// <summary>
/// One work-experience entry in the unified view. The two arms fill it differently and
/// <see cref="TextIsDescriptionOnly"/> records which contract <see cref="Text"/> honors:
/// staging supplies the segmenter's verbatim block (header line + period line +
/// description; <c>false</c>) with the freeform <see cref="PeriodText"/>; canonical
/// supplies the pure description (<c>true</c>) with structured
/// <see cref="StartDate"/>/<see cref="EndDate"/> (open end = ongoing) — and, since
/// honest date absence (CTO-bind 5a-pre), a date-less canonical entry also carries
/// <see cref="PeriodText"/> (the verbatim RawPeriod) so the period stays recoverable.
/// </summary>
public sealed record ReviewableExperience(
    string? Title,
    string? Organization,
    string? PeriodText,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Text,
    bool TextIsDescriptionOnly);

/// <summary>One education entry — A10 verdicts on institution + degree presence.</summary>
public sealed record ReviewableEducation(
    string? Institution,
    string? Degree);
