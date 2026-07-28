using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Common;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

/// <summary>
/// Full detail view of a PendingReview <c>ParsedResume</c> staging artifact (F4-8), used to
/// drive the read-only review view (ADR 0112 — the reviewer is the product; the Slutför guide
/// and gap-fill are retired, CV-pivot 5c). Carries the owner's decrypted, loosely parsed CV
/// content faithfully — every field is honest about what the deterministic parser found and
/// nothing is synthesised (CLAUDE.md §5): each experience/education keeps its raw <c>Period</c>
/// string (not a guessed date). This is CV-PII; the handler enforces owner-only access
/// fail-closed (IDOR → 404 + audit) and reads it inside the field-encryption pipeline
/// (Invariant 3). Reuses the parse-summary read-models (<see cref="ParseConfidenceDto"/>,
/// <see cref="PersonnummerScanDto"/>) already defined for the import response.
/// </summary>
/// <param name="BlockReason">
/// Why this file is still a staging artifact rather than a saved CV, or <c>null</c> when
/// nothing in the FILE blocks it (#1060, CTO-bind D4-REBIND). DERIVED per request by
/// <c>AutoPromoteGate</c> — the same evaluator the write path runs — never stored: no column,
/// no migration, and therefore nothing that can go stale the next time a gate changes.
/// <para><b>It rides this DTO's existing personnummer-egress contract and widens it by
/// nothing.</b> The value is a CLOSED enum token (<c>AutoPromoteBlockReason</c>), serialised as
/// the member NAME. It is not free text, it is not a field value, and it cannot echo a
/// personnummer, a file name or any parsed content — the token says WHICH gate fired, never
/// what it saw. That is the classification the 5a CTO-bind ("FE review-view copy and
/// telemetry") and the 5c security-auditor ("an enum token only … no PII") already recorded for
/// it on the import response. The egress it travels is the existing DEK-warm, owner-scoped
/// one; it opens no new surface. A future member that carried a VALUE rather than a gate
/// identity would break this paragraph and must not be added.</para>
/// <para><b><c>null</c> is scoped to the artifact, not to the next submission (CTO-bind D1).</b>
/// The gate's label channel takes the user's upload-form CV name, which this read does not
/// have, so it evaluates the generated default: a personnummer typed into that field is <b>not
/// assessed</b> here. <c>null</c> therefore means "nothing in the file blocks it", never "this
/// will save". Copy rendered off this field must not certify a save.</para>
/// <para>Consumers: the review view renders reason-specific copy so the user learns what her
/// file needs without uploading it again. Never routed on (CLAUDE.md §5 — the endpoint routes
/// on the outcome TYPE, not on a reason string).</para>
/// </param>
public sealed record ParsedResumeDetailDto(
    Guid Id,
    string Status,
    string DetectedLanguage,
    string SourceFileName,
    ParseConfidenceDto Confidence,
    PersonnummerScanDto Personnummer,
    ParsedContentDto Content,
    IReadOnlyList<OccupationProposalDto> OccupationProposals,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? BlockReason);

/// <summary>The loosely parsed CV content — best-effort, often partial; never synthesised.
/// <para>Personnummer-egress contract: only <see cref="Preamble"/> is rendered inline on the
/// review, so only it carries the mapper's personnummer guard. <c>Profile</c> and the
/// <c>RawText</c> fields travel verbatim but are NOT rendered here — they reach the client only via
/// the preview / ATS-text surfaces, which redact at their own egress. A future inline render of any
/// of them must add the same guard <see cref="Preamble"/> has (security-auditor + code-reviewer,
/// 5c-b).</para></summary>
/// <param name="Preamble">
/// Text the CV carried ABOVE its first heading that no contact extractor claimed — verbatim and
/// UNCLASSIFIED (#844, ADR 0109). <c>null</c> when the preamble was fully accounted for by
/// name / e-mail / phone / location extraction (the common case). The engine does NOT claim
/// this is a profile: it is shown back with a neutral label so the owner can decide what it is,
/// and no rule grades it (ADR 0109 §1 — the engine describes, the user classifies). This is the
/// only <c>ParsedContentDto</c> field rendered inline on the review view, so the mapper guards it
/// with the highest-priority personnummer control in two layers (CLAUDE.md §5): PRIMARY — the
/// carrier is suppressed to <c>null</c> when the parse is flagged (<c>Personnummer.Found</c>), the
/// categorical Domain binding (<c>PreambleResidue</c>, #844 — a residue subtracts no personnummer,
/// and redaction re-scans the reconstructed carrier, not the flagged RawText); SECONDARY —
/// <c>PersonnummerRedactor</c> on the unflagged path (belt-and-braces, parity <c>GetResumeAtsText</c>).
/// ADR 0109 Amendment (5c-b): the adopt/classify action is FAS-DEFERRED — the Slutför guide that
/// once hosted it is retired (ADR 0112), so the affordance is display-only; the path to adopt the
/// text is to give it a heading in the file and upload again.
/// <para>ADR 0109 Amendment (2026-07-27, #1060): this residue no longer blocks auto-promote —
/// <c>AutoPromoteBlockReason.UnclassifiedPreamble</c> is retired — and the text is projected onto
/// <c>ResumeContent.Preamble</c> at promote, so the affordance follows the CV instead of holding it
/// in staging.</para>
/// <para>This DTO's own two-layer guard is unchanged and still load-bearing HERE, and the promoted
/// surface deliberately does NOT copy it. The two arms are not in the same guarantee class: a
/// flagged parse PERSISTS (only promote is gated), so the staging egress needs fail-closed
/// suppression on read; canonical content is guaranteed clean at the WRITE boundary by
/// <c>ResumeContentPersonnummerGuard</c>, which is architecture-enforced on every content write
/// surface and scans the preamble too. A second read-side redactor over there would be two
/// normalisers of the product's highest-priority PII rule, which is worse than one.</para>
/// </param>
public sealed record ParsedContentDto(
    ParsedContactDto Contact,
    string? Profile,
    IReadOnlyList<ParsedExperienceDto> Experiences,
    IReadOnlyList<ParsedEducationDto> Educations,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages,
    IReadOnlyList<ParsedSectionDto> Sections,
    string? Preamble);

/// <summary>
/// A section the CV has that is not one of the six typed kinds — "Projekt", "Referenser" (#815).
/// The heading is the user's own line, verbatim: it is content to show back, never a discriminator.
/// </summary>
public sealed record ParsedSectionDto(
    string Heading,
    IReadOnlyList<ParsedSectionEntryDto> Entries);

/// <summary>One entry inside a free section. <c>Title</c> is null when the entry has none — the
/// parser does not invent one (ADR 0071).</summary>
public sealed record ParsedSectionEntryDto(string? Title, IReadOnlyList<string> Lines);

public sealed record ParsedContactDto(string? FullName, string? Email, string? Phone, string? Location);

/// <summary>One experience entry — best-effort structured fields plus the verbatim entry
/// text. <c>Period</c> is the raw parsed string (e.g. "2021–2024"), never a guessed date: the
/// backend never invents dates on a PII field (DQ3-3a).</summary>
public sealed record ParsedExperienceDto(string? Title, string? Organization, string? Period, string RawText);

public sealed record ParsedEducationDto(string? Institution, string? Degree, string? Period, string RawText);

/// <summary>An unconfirmed SSYK occupation-group proposal (ADR 0040 Beslut 4 — the user
/// confirms downstream; never auto-selected). Non-PII (taxonomy id + labels).
/// <see cref="ApproximateYears"/> (ADR 0079-amendment) is the CV-derived ~years of experience
/// attributed to this group at import (null = "not stated"); it seeds the wizard's per-occupation
/// year input (PR-4). A non-PII integer projection — the raw periods stay DEK-encrypted.</summary>
public sealed record OccupationProposalDto(string ConceptId, string Label, string MatchedOn, int? ApproximateYears);
