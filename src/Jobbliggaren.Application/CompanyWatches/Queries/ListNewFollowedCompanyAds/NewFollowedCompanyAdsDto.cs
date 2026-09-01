using Jobbliggaren.Application.JobAds.Queries;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;

/// <summary>
/// One ad on the new-followed-ads surface. <c>MatchesYou == null</c> means NOT ASSESSED — the user
/// has stated no occupation, so the grade predicate is inert and claiming either answer would be a
/// fabrication. Same nullable semantics as <c>CompanyWatchDto.MatchingAdCount</c>: a null is silence,
/// never a false zero.
///
/// <para>
/// The flag is a per-row FIELD rather than a sibling id-set because a field cannot fall out of sync
/// with its row. <c>/jobb</c>'s sibling-set form (<c>JobAdMatchBatchDto</c>) exists only because that
/// list DTO is anonymously cacheable and must stay per-user-free (ADR 0076 Decision 5); this surface
/// is auth-gated and per-user by construction, so that reason does not carry here.
/// </para>
///
/// <para>
/// <b>Goodhart (ADR 0071, C-E2):</b> a boolean is not a score. §5 forbids "a match score as an opaque
/// number"; this bears no magnitude, and the house already surfaces strictly more per ad — the named
/// <c>MatchGrade</c> plus seven dimension verdicts on <c>/jobb</c> — with the same predicate
/// surfaced aggregated as <c>CompanyWatchDto.MatchingAdCount</c>.
/// </para>
/// </summary>
public sealed record NewFollowedAdRow(JobAdDto Ad, bool? MatchesYou);

/// <summary>
/// #1576 — the ads behind the Översikt number, newest first.
///
/// <para>
/// <b><see cref="MatchingAssessed"/> is a PAGE-GLOBAL fact, carried rather than derived.</b> The
/// server answers a whole page from one profile read, so assessability is all-or-none — but a
/// client reconstructing it from the rows has to choose between <c>some</c> and <c>every</c>, and
/// those disagree exactly when the invariant breaks. <c>some</c> would then count the unknown rows
/// as non-matching and report a subset as the total. Carrying the flag removes the choice.
/// </para>
///
/// <para>
/// <b><see cref="AcknowledgedThrough"/> is the whole reason this is not a bare list.</b> The rail
/// watermark is compared against <c>FollowedCompanyAdHit.CreatedAt</c> — the SCAN clock — while an
/// ad's own <c>CreatedAt</c> is its ingest time. The scan only admits ads newer than its last run and
/// stamps the hit at scan time, so <c>hit.CreatedAt &gt; ad.CreatedAt</c> ALWAYS. A client that
/// computed a window from the ad timestamps would therefore hand back a value BELOW every hit it just
/// read, and the count would never reset. The server computes the window where the query runs; the
/// client hands it back verbatim (the original ISO string — <c>Date.parse</c> truncates milliseconds).
/// </para>
///
/// <para>
/// <b>Why the window is the max over the OLDEST rows.</b> The fetch is ordered oldest-first and
/// capped, so acknowledging its max leaves everything newer ABOVE the watermark, to be returned on the
/// next visit. Fetching the newest N instead would acknowledge past everything older and swallow it
/// permanently — the watermark is monotonic, so a swallowed hit is unrecoverable. That silent loss is
/// the defect class #1576 exists to close, which is why this surface is capped rather than paginated.
/// </para>
/// </summary>
public sealed record NewFollowedCompanyAdsDto(
    IReadOnlyList<NewFollowedAdRow> Rows,
    bool MatchingAssessed,
    DateTimeOffset? AcknowledgedThrough,
    bool Truncated)
{
    /// <summary>
    /// No authenticated user, no active follows, or nothing new since the watermark — all honest, and
    /// all indistinguishable to the caller by design (an empty surface says the same thing in each).
    /// <c>AcknowledgedThrough</c> is null: there is no window to acknowledge, so the client writes
    /// nothing and the watermark does not move.
    /// </summary>
    public static readonly NewFollowedCompanyAdsDto Empty =
        new([], MatchingAssessed: false, AcknowledgedThrough: null, Truncated: false);
}
