namespace Jobbliggaren.Application.Applications.Queries.GetActivityReport;

/// <summary>
/// One sought job in the AF activity report (issue #316) — a deterministic
/// projection over an application the user submitted in the selected month.
/// Field-by-field by design: Arbetsförmedlingen's form is filled per field, so
/// the FE renders one copy button per non-empty value (never a copy-everything
/// text block, which would flag the report for manual review).
///
/// Job metadata is nullable because the ad ROW can be absent: a cover-letter-only
/// application has no JobAd and no ManualPosting fallback, so it yields no
/// employer/title/location (ADR 0048 — j == null).
/// <para>
/// #805-3 truth-sync: the previous claim ("a soft-deleted JobAd") was false.
/// JobAd.DeletedAt has no writer, so the global query filter never excludes a row
/// (#821); a retracted ad is ARCHIVED (Status = "Archived") and still joins,
/// metadata intact. Absence here means "no ad row", never "ad withdrawn".
/// </para>
/// The FE
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
