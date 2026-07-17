namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationHistory;

/// <summary>
/// #444 (ADR 0087 D2 read-model; DPIA #456 / ADR 0090 D1, Art. 6(1)(b)) — one employer's slice of the
/// signed-in user's OWN application history, grouped by org.nr. The backbone the Företag-hub history
/// hangs on: a per-employer projection over the user's submitted applications joined to public
/// <c>job_ads</c>, keyed on the org.nr the ad carries (ADR 0087 D2 — resolved at READ, never a
/// denormalised snapshot).
///
/// <para>
/// <b>Personnummer guard (FORK C1 mask+flag, ADR 0087 D8(c), CLAUDE.md §5).</b> A sole-proprietorship
/// (enskild firma) org.nr can EQUAL the owner's personnummer, so when the grouped org.nr is
/// personnummer-shaped <see cref="OrganizationNumber"/> is <c>null</c> (the raw value is NEVER
/// surfaced) and <see cref="IsProtectedIdentity"/> is <c>true</c>. The user still identifies the
/// employer by <see cref="CompanyName"/> (resolved at read from public Platsbanken data). For a normal
/// legal-entity org.nr the full number is returned and the flag is <c>false</c>. This is
/// data-minimisation (GDPR Art. 5.1(c)) at the surfacing boundary; the raw value is never logged.
/// </para>
///
/// <para>
/// <b>Owner-scoped only (M2, IDOR).</b> Every row is the CURRENT user's own history — the projection
/// filters on the caller's <c>JobSeekerId</c> and never enumerates another user's applications. Only
/// actually-submitted applications appear (<c>AppliedAt != null</c>); a draft the user never sent is
/// intent, not history (the Art. 6(1)(b) purpose, ADR 0090 D1).
/// </para>
/// </summary>
public sealed record EmployerApplicationHistoryDto(
    string? OrganizationNumber,
    bool IsProtectedIdentity,
    string? CompanyName,
    int ApplicationCount,
    IReadOnlyList<ApplicationHistoryEntryDto> Applications)
{
    /// <summary>
    /// REDACTED (#883). The DTO masks its org.nr at the SURFACING boundary, but a record's
    /// compiler-generated <c>ToString()</c> prints every member — a plain <c>{X}</c> MEL placeholder
    /// would still write <see cref="OrganizationNumber"/> into a log. Defense-in-depth at the log
    /// boundary too (a sole prop's org.nr IS a personnummer, ADR 0087 D8(c); CLAUDE.md §5). Keeps
    /// <see cref="CompanyName"/> + <see cref="ApplicationCount"/>; pinned by
    /// <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() =>
        $"EmployerApplicationHistoryDto({CompanyName}, ApplicationCount={ApplicationCount}, org.nr redacted)";
}

/// <summary>
/// One application in the per-employer history: WHEN the user applied (<see cref="AppliedAt"/>, the
/// first-submit stamp) and the application's CURRENT status name
/// (<c>ApplicationStatus.Name</c>). Minimal by design — no application id, no JobAdId, no job title,
/// no free text, and no contact-person name (ADR 0090 D2 R-A4 firewall: history is never keyed to a
/// named individual).
/// </summary>
public sealed record ApplicationHistoryEntryDto(
    DateTimeOffset AppliedAt,
    string StatusName);
