using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// ADR 0175 D2 (#1663) — the nightly Branschbevakning scan's one read: which ACTIVE ads of a criterion's
/// materialised member companies were ingested inside a window. Worker-facing; the Api-facing reads stay on
/// <see cref="ICompanyWatchBrowseQuery"/>.
///
/// <para>
/// <b>Its own interface, the same implementing class.</b> <see cref="ICompanyWatchBrowseQuery"/>'s "one port"
/// rule exists so a count and a page cannot drift apart; that defence is the implementation's single
/// <c>FROM/WHERE</c> constant, state gate and binding routine, and this read is built from exactly those. So
/// the predicate authority stays one, while the Api surface does not grow a method only the Worker calls.
/// </para>
///
/// <para>
/// <b>Ad ids only.</b> No organisation number leaves the port (the ADR 0139 firewall): the member set is
/// Infrastructure-internal, and a hit can only point at an ad whose org.nr the member write already proved is
/// not personnummer-shaped.
/// </para>
///
/// <para>
/// <b>No LIMIT and no refusal, deliberately, and AGENTS.md §5's ban on unpaginated list fetches does not
/// apply.</b> The read is a Worker batch of ids, never the answer to a request, and it is bounded by what it
/// reads: the window is at most the scan's cold-start floor, a criterion at most
/// <c>CompanyWatchCriterionMember.MaxPerCriterion</c> companies, a user at most
/// <see cref="CompanyWatchCriterion.MaxPerUser"/> criteria. A cap would be a refusal, and a refusal would
/// freeze the scan's watermark.
/// </para>
///
/// <para>
/// <b>Gate, members and ads are read in ONE statement</b>, so the read sees one materialisation: the member
/// replace is one transaction, which is why the scan takes no share of the materialisation lock. An answer
/// that is not <see cref="CriterionMaterialisationState.Materialised"/> carries no ids, and the scan must not
/// read it as "nothing new" — that would advance the watermark over a window nobody read.
/// </para>
/// </summary>
public interface ICompanyWatchCriterionAdScan
{
    /// <summary>
    /// The criterion's ACTIVE member-company ads with <c>sinceExclusive &lt; created_at &lt;= untilInclusive</c>,
    /// ordered by <c>created_at</c>, then id. <paramref name="fingerprint"/> is the predicate the criterion has
    /// NOW; a member set written for another predicate answers
    /// <see cref="CriterionMaterialisationState.NotMaterialised"/>, as does one older than the read age.
    /// <paramref name="sinceExclusive"/> must be earlier than <paramref name="untilInclusive"/>.
    /// </summary>
    ValueTask<MaterialisedNewAdIds> ScanNewActiveAdIdsAsync(
        CompanyWatchCriterionId criterionId,
        CriteriaFingerprint fingerprint,
        DateTimeOffset sinceExclusive,
        DateTimeOffset untilInclusive,
        CancellationToken cancellationToken);
}

/// <summary>
/// ADR 0175 D2 — the scan window's ad ids, or the reason there are none. <see cref="Ids"/> is non-null exactly
/// under <see cref="CriterionMaterialisationState.Materialised"/>; an empty list there is an honest "nothing
/// new". There is no refused arm: the read is bounded by its window, not by a set cap.
/// </summary>
public sealed record MaterialisedNewAdIds(
    CriterionMaterialisationState State, IReadOnlyList<JobAdId>? Ids)
{
    public IReadOnlyList<JobAdId>? Ids { get; } =
        (State == CriterionMaterialisationState.Materialised) == (Ids is not null)
            ? Ids
            : throw new ArgumentException(
                "En id-mängd finns exakt när materialiseringen är gjord: utan den vore en tom mängd ett "
                + "påhittat \"inget nytt\" som flyttar vattenmärket.",
                nameof(Ids));

    public static MaterialisedNewAdIds Resolved(IReadOnlyList<JobAdId> ids) =>
        new(CriterionMaterialisationState.Materialised, ids);

    public static MaterialisedNewAdIds TooBroad { get; } =
        new(CriterionMaterialisationState.TooBroad, null);

    public static MaterialisedNewAdIds NotMaterialised { get; } =
        new(CriterionMaterialisationState.NotMaterialised, null);
}
