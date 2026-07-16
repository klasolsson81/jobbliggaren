using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

public sealed record JobAdStatus
{
    public string Value { get; }

    private JobAdStatus(string value) => Value = value;

    public static readonly JobAdStatus Active = new("Active");

    // #886 — `Expired` was retired 2026-07-16 (ADR, this decision). It was declared and rendered
    // for the product's entire history without a single writer (`ExpireJobAdsJob` has stamped
    // Archived since its first commit, 34ea5993 — the expiry CAUSE lives in the audit stream as
    // `JobAdsRetentionCompleted.Reason: "expired"`, never as a row state). No value may be
    // declared here without a writer, a distinct invariant, and a decision on historical rows.
    public static readonly JobAdStatus Archived = new("Archived");

    /// <summary>
    /// Terminal. The ad was erased under GDPR Art. 17 (ADR 0106 Tier B, #842): its text is gone
    /// and re-import is refused, so the row survives only as a tombstone.
    /// </summary>
    /// <remarks>
    /// The column is <c>varchar(20)</c> via a value converter — no CHECK constraint, no PG enum
    /// type — so a fourth value costs zero migrations (evidence pack B9).
    /// </remarks>
    public static readonly JobAdStatus Erased = new("Erased");

    // Fail-loud by design: a row carrying a value outside {Active, Archived, Erased} — including
    // the retired "Expired", which no writer ever produced — surfaces as a Validation failure at
    // the value converter instead of silently masquerading as a live state (#886).
    public static Result<JobAdStatus> FromValue(string value) => value switch
    {
        "Active" => Result.Success(Active),
        "Archived" => Result.Success(Archived),
        "Erased" => Result.Success(Erased),
        _ => Result.Failure<JobAdStatus>(
            DomainError.Validation("JobAdStatus.Invalid", $"Okänd status: {value}"))
    };

    public override string ToString() => Value;
}
