using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.SavedSearches.Events;

/// <summary>
/// Provenance event (ADR 0040 Beslut 4): a <c>SavedSearch</c> was created via the CV-derivation
/// <b>confirm</b> flow — the user explicitly confirmed deterministically-derived ssyk-4
/// occupation groups (the engine PROPOSES, the user CONFIRMS; never auto-created). It records the
/// source CV (<paramref name="SourceParsedResumeId"/>, optional) for audit and distinguishes a
/// CV-derived search from a manually-built one.
/// <para>ADR 0040 Beslut 3 defers a <i>stored</i> DerivedFromResumeId reference to a future
/// supersession-ADR, so this provenance rides on the event ONLY — no column, no migration (parity
/// with <c>Resume.CreateFromParsed</c>'s provenance-event-only pattern, STEG A).</para>
/// <para><b>Security contingency (security-auditor B4):</b> <paramref name="SourceParsedResumeId"/>
/// is recorded UNVERIFIED — acceptable only because it is event-only (never persisted as a
/// queryable column, never read back, never surfaced cross-user). If a future supersession-ADR
/// promotes it to a stored/queryable column, the owning handler MUST first verify the source CV
/// belongs to the caller (owner-scoped IDOR check).</para>
/// </summary>
public sealed record SavedSearchDerivedFromResumeDomainEvent(
    SavedSearchId SavedSearchId,
    JobSeekerId JobSeekerId,
    Guid? SourceParsedResumeId,
    DateTimeOffset OccurredAt) : IDomainEvent;
