namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// What the erasure request did. <b>Never a bare <c>rowsAffected</c> again.</b>
/// </summary>
/// <remarks>
/// The old endpoint answered every request with <c>200 OK, rowsAffected: 0</c> and no code path
/// distinguished "erased nothing" from "erased something" — so the runbook told the operator to
/// confirm erasure regardless, and 0 was the only value it could ever return. Art. 12(3) requires
/// us to tell the data subject <i>what we did</i>. That duty is discharged by this discriminator,
/// not by a number whose meaning nobody could recover.
/// </remarks>
public enum ErasureOutcome
{
    /// <summary>
    /// We hold nothing matching this identifier — and because both channels ran (FTS AND
    /// substring, over title/description/raw_payload), this is now a statement that is <b>true</b>
    /// when we make it. It is the sentence the old mechanism could not honestly say and said anyway.
    /// </summary>
    NoMatchingDataHeld,

    /// <summary>Nothing was written. The counts are what a real run <i>would</i> do.</summary>
    DryRun,

    /// <summary>Ads were erased. Irreversible.</summary>
    AdsErased,
}

/// <summary>
/// Per-surface counts, generated from <see cref="ErasureCascadeRegistry"/>. Reported for BOTH
/// "matched" and "erased" — and the gap between them is the disclosure.
/// </summary>
/// <remarks>
/// A saved search that mentions the recruiter is <c>Matched</c> but never <c>Erased</c>, so the
/// two numbers differ and the operator cannot miss it. That is deliberate: the alternative is a
/// single number that quietly means "what we felt like erasing", which is the shape of every
/// defect in this issue.
/// </remarks>
public sealed record ErasureSurfaceCounts(
    int JobAds,
    int RecentJobSearches,
    int SavedSearches)
{
    public static ErasureSurfaceCounts None { get; } = new(0, 0, 0);

    public int Total => JobAds + RecentJobSearches + SavedSearches;
}

/// <summary>
/// The Art. 17 erasure response (ADR 0106 D8, #842).
/// </summary>
/// <param name="RequestId">Correlates this reply with its <c>audit_log</c> row.</param>
/// <param name="Outcome">What happened. See <see cref="ErasureOutcome"/>.</param>
/// <param name="DryRun">True ⇒ nothing was written.</param>
/// <param name="Matched">What the two-channel scan found, per surface.</param>
/// <param name="Erased">
/// What was actually destroyed, per surface. On a dry run this is
/// <see cref="ErasureSurfaceCounts.None"/>. <c>Matched.SavedSearches &gt; Erased.SavedSearches</c>
/// is expected and is disclosed, never silently reconciled.
/// </param>
/// <param name="ErasedExternalIds">
/// Arbetsförmedlingen's ids for the erased ads — not personal data, and the accountability spine
/// (Art. 5(2)/30). This is what an auditor, and the recruiter, can check us against.
/// </param>
public sealed record EraseRecruiterAdsResponse(
    Guid RequestId,
    ErasureOutcome Outcome,
    bool DryRun,
    ErasureSurfaceCounts Matched,
    ErasureSurfaceCounts Erased,
    IReadOnlyList<string> ErasedExternalIds);
