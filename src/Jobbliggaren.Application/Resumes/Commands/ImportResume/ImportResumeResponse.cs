using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Application.Resumes.Commands.ImportResume;

/// <summary>
/// The result surfaced to the caller after a CV import (F4-8). Carries the new
/// artifact id plus the explainable parse signals the UX needs (OQ5): the
/// confident-vs-degraded distinction, the personnummer-removal prompt (masked), and
/// the unconfirmed SSYK proposals. No CV-PII (no parsed content, no raw text) crosses
/// this boundary.
/// </summary>
public sealed record ImportResumeResponse(
    Guid ParsedResumeId,
    string DetectedLanguage,
    ParseConfidenceDto Confidence,
    PersonnummerScanDto Personnummer,
    IReadOnlyList<OccupationCandidate> OccupationProposal);

/// <summary>Document + per-section parse confidence (OQ5), explainable not opaque.</summary>
public sealed record ParseConfidenceDto(
    string Overall,
    bool RequiresManualReview,
    string Fallback,
    IReadOnlyList<SectionConfidenceDto> Sections);

/// <summary>One section's confidence verdict + cited (non-PII) evidence.</summary>
public sealed record SectionConfidenceDto(
    string Section,
    string Level,
    IReadOnlyList<string> Evidence);

/// <summary>PII-safe personnummer-scan summary — count + kinds, never a raw value
/// (ADR 0074 Invariant 1).</summary>
public sealed record PersonnummerScanDto(
    bool Found,
    int Count,
    IReadOnlyList<string> Kinds);
