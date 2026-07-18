namespace Jobbliggaren.Application.JobAds.Queries.GetRemoteAdCount;

/// <summary>
/// #551 PR-B D7 — the "Distans (N)" facet-hint response. A wrapped <c>{ count: int }</c>
/// (not a bare int) so the wire form is an evolvable JSON object.
/// <para>
/// <b>Own type, never a reused <c>MatchCountPreviewDto</c>/facet dict (CCP/SRP):</b> the
/// facet HINT (facet-excluded remote count) is a genuinely different question from the
/// preview/notis counters (which apply remote as a real filter) — a shared binary shape
/// is structural likeness, not shared knowledge. <c>Count</c> is always concrete (record
/// class, never null → the empty-body trap never fires); FE owns <c>null</c> as its own
/// client loading-state.
/// </para>
/// </summary>
public sealed record RemoteAdCountDto(int Count);
