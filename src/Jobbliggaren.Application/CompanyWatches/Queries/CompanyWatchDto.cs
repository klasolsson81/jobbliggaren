namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — an owner-facing view of one company follow.
///
/// <para>
/// <b>Personnummer guard (FORK C1 mask+flag, ADR 0087 D8(c)).</b> When the watched org.nr is
/// personnummer-shaped (a potential enskild-firma org.nr that equals the owner's national
/// identity number), <see cref="OrganizationNumber"/> is <c>null</c> (the raw 10-digit value is
/// NEVER surfaced) and <see cref="IsProtectedIdentity"/> is <c>true</c>. The user still identifies
/// the watch by <see cref="CompanyName"/> (resolved at read from public Platsbanken data). For a
/// normal legal-entity org.nr the full number is returned and the flag is <c>false</c>. This is
/// data-minimisation (GDPR Art. 5.1(c)) at the surfacing boundary; the raw value is never logged.
/// </para>
///
/// <para>
/// <b><see cref="ActiveAdCount"/> — "X aktiva annonser just nu" (#447, ADR 0087 D2).</b> The number
/// of currently-active public job ads for the watched employer (<c>status='Active'</c> for this
/// org.nr, over the global soft-delete query filter — ADR 0048). This is a derived count over PUBLIC
/// Platsbanken data — it carries NO user-PII, so it is surfaced even when the org.nr is masked (a
/// sole-prop still shows its public open-role count). It is a plain <c>int</c>, never derived from or
/// exposing the raw org.nr. Zero when the employer has no active ads (or none are ingested yet).
/// </para>
///
/// <para>
/// <b><see cref="MatchingAdCount"/> — "X matchande annonser" (#452, ADR 0087 D5-tillägg).</b> Of the
/// employer's currently-active ads, how many match this user's Fast match profile at grade >= Good
/// (Good/Strong) — computed at READ by the SAME shared <c>GradeRankExpression</c> /jobb uses, so the
/// hub count can never diverge from what /jobb shows (sort==grade coherence, ADR 0079). The
/// company-watch SCAN stays scorer-free and <c>FollowedCompanyAdHit</c> gains no grade column — the
/// grade is a derived read label only. <b>Nullable = honest not-assessed:</b> <c>null</c> when the
/// user has stated no occupation (empty SSYK profile) — a hard <c>0</c> would falsely read as "this
/// employer has no matching ads for you" when the truth is "state your occupations" (the FE renders
/// that nudge, parity /jobb and <c>GetMyMatchCount</c>). A non-null value (including <c>0</c>) means
/// assessed. A count of ADS over a named grade threshold, never an opaque match score (Goodhart,
/// ADR 0071); carries no user-PII and never exposes the raw org.nr.
/// </para>
///
/// <para>
/// <b><see cref="Filter"/> — the per-watch notification filter (bevakning F4b, RF-2).</b> <c>null</c>
/// means no filter, mirroring the domain's canonical representation (the NULL jsonb column) — there is
/// deliberately no redundant <c>hasFilter</c> bool beside it, which would be two representations of one
/// truth. The FE needs it for two things: to pre-fill the filter editor, and to disclose in the row's
/// RESTING state that this watch is filtered (BC-9′). The resting-state disclosure is load-bearing, not
/// polish: an active filter narrows the notifications AND the Översikt rail count, and when every watch
/// suppresses everything no digest email is sent at all — so this row is the ONLY surface that can carry
/// the RF-13 transparency guarantee in that case.
/// </para>
///
/// <para>
/// The counts above are deliberately NOT filter-aware (RF-8): they answer "does this employer post ads
/// I match?" (a follow-DECISION signal), while the filter answers "which of them should notify me". Three
/// scopes of ONE grade definition, each independently explainable.
/// </para>
/// </summary>
public sealed record CompanyWatchDto(
    Guid Id,
    string? OrganizationNumber,
    bool IsProtectedIdentity,
    string? CompanyName,
    DateTimeOffset FollowedAt,
    int ActiveAdCount,
    int? MatchingAdCount,
    WatchFilterDto? Filter)
{
    /// <summary>
    /// REDACTED (#883). The DTO masks its org.nr at the SURFACING boundary, but a record's
    /// compiler-generated <c>ToString()</c> prints every member — a plain <c>{X}</c> MEL placeholder
    /// would still write <see cref="OrganizationNumber"/> into a log. Defense-in-depth at the log
    /// boundary too (a sole prop's org.nr IS a personnummer, ADR 0087 D8(c); CLAUDE.md §5). Keeps
    /// <see cref="Id"/> + <see cref="CompanyName"/>; pinned by <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() =>
        $"CompanyWatchDto(Id={Id}, CompanyName={CompanyName}, org.nr redacted)";
}

/// <summary>
/// The user's notification filter for ONE followed company (bevakning F4b). Carries the two DISJOINT
/// JobTech geo namespaces as they are stored — a whole-län pick lives in <see cref="Regions"/> and is
/// never expanded into its municipalities (see <c>WatchFilterSpec</c>) — plus the fixed-floor
/// "endast matchande" flag.
///
/// <para>
/// <b>No org.nr member (D8), and no grade value (Goodhart/C-E2).</b> The concept-ids are taxonomy
/// references, not personal identifiers; the LABELS are resolved FE-side from the taxonomy tree the ort
/// picker already holds, so there is exactly one label authority and no second one to drift from it.
/// </para>
/// </summary>
public sealed record WatchFilterDto(
    IReadOnlyList<string> Municipalities,
    IReadOnlyList<string> Regions,
    bool OnlyMatched);
