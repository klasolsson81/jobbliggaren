namespace Jobbliggaren.Infrastructure.JobAds.SnapshotMisses;

/// <summary>
/// ADR 0032-amendment 2026-05-23 — frånvaro-räknare för JobAd-ExternalId per
/// snapshot-källa. <b>Infrastructure-entitet, EJ Domain, EJ aggregate root.</b>
/// Bookkeeping för retention-strategin — inte del av JobAds ubiquitous language
/// (Evans 2003 §5). Paritet <see cref="Jobbliggaren.Infrastructure.Persistence.UserDataKey"/>
/// (TD-13 C2): mappas via <c>AppDbContext.Set&lt;JobAdSnapshotMiss&gt;()</c>,
/// exponeras ALDRIG via <c>IAppDbContext</c> (CTO-triage FRÅGA 2 2026-05-18; ISP,
/// Martin 2017 kap. 10/22; ADR 0009).
/// <para>
/// PK <c>(Source, ExternalId)</c>. <see cref="MissCount"/>=0 betyder "sågs i
/// senaste komplett snapshot". Increment sker när Active-JobAd:s ExternalId
/// inte fanns i seen-set. Reset sker när ExternalId återigen syns.
/// </para>
/// </summary>
public sealed class JobAdSnapshotMiss
{
    // EF-materialiserings-ctor.
    private JobAdSnapshotMiss()
    {
        Source = string.Empty;
        ExternalId = string.Empty;
    }

    public JobAdSnapshotMiss(string source, string externalId)
    {
        Source = source;
        ExternalId = externalId;
        MissCount = 0;
    }

    public string Source { get; private set; }

    public string ExternalId { get; private set; }

    public int MissCount { get; private set; }

    public DateTimeOffset? FirstMissedAt { get; private set; }

    public DateTimeOffset? LastMissedAt { get; private set; }
}
