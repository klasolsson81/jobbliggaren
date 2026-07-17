using Jobbliggaren.Application.Resumes.Commands.ImportResume;

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
    DateTimeOffset UpdatedAt);

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
/// text is to give it a heading in the file and upload again (auto-promote, which blocks on this
/// exact residue via <c>AutoPromoteBlockReason.UnclassifiedPreamble</c>).
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
