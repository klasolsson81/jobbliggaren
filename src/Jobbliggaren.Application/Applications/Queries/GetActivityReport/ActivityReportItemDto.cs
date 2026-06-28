namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// One sought job in the AF activity report (issue #316) — a deterministic
/// projection over an application the user submitted in the selected month.
/// Field-by-field by design: Arbetsförmedlingen's form is filled per field, so
/// the FE renders one copy button per non-empty value (never a copy-everything
/// text block, which would flag the report for manual review).
///
/// Job metadata is nullable because the source can be absent: a soft-deleted
/// JobAd (ADR 0048 — j == null, no ManualPosting fallback) or the degenerate
/// cover-letter-only application both yield no employer/title/location. The FE
/// shows a neutral "—" and no copy button for an empty field; we never invent
/// data and never surface an opaque taxonomy concept-id as a location.
/// </summary>
public sealed record ActivityReportItemDto(
    Guid ApplicationId,
    DateTimeOffset AppliedAt,   // "Datum sökt" — first transition into Submitted
    string? Employer,           // "Arbetsgivare"
    string? Title,              // "Jobbtitel"
    string? Location,          // "Ort" — resolved municipality label; null when unavailable
    string? Source,            // "Platsbanken" | "LinkedIn" | "Manual" (drives "Hur du sökte" default)
    string? Url);              // optional "Länk till annons"
