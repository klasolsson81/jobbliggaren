using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds.Events;

namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #2264 (ADR 0187) — what the junior rules said about one ad, and what an admin decided about it. Its own aggregate,
/// keyed by the ad's id (senior-cto-advisor, 2026-11-20): the verdict answers to the rule file and the decision to the
/// admin, while <see cref="JobAd"/> mirrors the source, so neither the ingest funnel nor the lifecycle writers can ever
/// reach the verdict or the decision.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two writers, disjoint members.</b> The Worker's sweep creates the assessment and re-classifies it; the admin
/// command decides. A re-classification never touches the decision, and a decision never touches the verdict.
/// </para>
/// <para>
/// <b>The fingerprint is stamped, never computed here.</b> It is the <c>JobAd.JuniorInputFingerprint</c> the sweep read
/// in the same statement as the text it classified, so the hash keeps one home (<c>JobAd.SetSourcePayload</c>). Null is a
/// state: an ad written before the fingerprint existed is classified once, and again after its next source write.
/// </para>
/// </remarks>
public sealed class JobAdJuniorAssessment : AggregateRoot<JobAdId>
{
    public JuniorVerdict Verdict { get; private set; }
    public string RuleId { get; private set; } = null!;
    public string RuleSetVersion { get; private set; } = null!;
    public string? ClassifiedFingerprint { get; private set; }
    public DateTimeOffset ClassifiedAt { get; private set; }
    public JuniorDecision? Decision { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    // EF Core constructor
    private JobAdJuniorAssessment() { }

    private JobAdJuniorAssessment(JobAdId jobAdId) : base(jobAdId) { }

    /// <summary>The ad this assessment is about (the aggregate's key).</summary>
    public JobAdId JobAdId => Id;

    /// <summary>The first verdict for an ad.</summary>
    public static JobAdJuniorAssessment Classify(
        JobAdId jobAdId, JuniorClassification classification, string? classifiedFingerprint, IDateTimeProvider clock)
    {
        var assessment = new JobAdJuniorAssessment(jobAdId);
        assessment.Reclassify(classification, classifiedFingerprint, clock);
        return assessment;
    }

    /// <summary>A new verdict over new content or new rules. Never touches <see cref="Decision"/>.</summary>
    public void Reclassify(JuniorClassification classification, string? classifiedFingerprint, IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(clock);
        if (classifiedFingerprint is not null
            && (classifiedFingerprint.Length != JuniorInputFingerprint.Length
                || !classifiedFingerprint.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
            throw new ArgumentException("A junior fingerprint is a SHA-256 in lower-case hex.", nameof(classifiedFingerprint));

        Verdict = classification.Verdict;
        RuleId = classification.RuleId;
        RuleSetVersion = classification.RuleSetVersion;
        ClassifiedFingerprint = classifiedFingerprint;
        ClassifiedAt = clock.UtcNow;
    }

    /// <summary>
    /// An admin's decision: it outranks every rule and survives every re-classification. <see langword="null"/> hands
    /// the ad back to the rules.
    /// </summary>
    public Result Decide(JuniorDecision? decision, IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (decision is { } d && !Enum.IsDefined(d))
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown junior decision.");

        var now = clock.UtcNow;
        Decision = decision;
        DecidedAt = decision is null ? null : now;
        RaiseDomainEvent(new JobAdJuniorDecidedDomainEvent(Id, decision, now));
        return Result.Success();
    }
}
