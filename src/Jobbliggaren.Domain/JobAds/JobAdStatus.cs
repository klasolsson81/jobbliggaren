using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

public sealed record JobAdStatus
{
    public string Value { get; }

    private JobAdStatus(string value) => Value = value;

    public static readonly JobAdStatus Active = new("Active");
    public static readonly JobAdStatus Expired = new("Expired");
    public static readonly JobAdStatus Archived = new("Archived");

    /// <summary>
    /// Terminal. The ad was erased under GDPR Art. 17 (ADR 0106 Tier B, #842): its text is gone
    /// and re-import is refused, so the row survives only as a tombstone.
    /// </summary>
    /// <remarks>
    /// The column is <c>varchar(20)</c> via a value converter — no CHECK constraint, no PG enum
    /// type — so a fourth value costs zero migrations (evidence pack B9).
    /// <para>
    /// Erased is terminal by construction: <see cref="JobAd.UpdateFromSource"/> refuses on it,
    /// which is what makes the erasure durable against the nightly snapshot sync and the
    /// 10-minute stream. Without that refusal the funnel rewrites the body within ≤24 h and the
    /// erasure is undone — the F-A defect this contract exists to close.
    /// </para>
    /// </remarks>
    public static readonly JobAdStatus Erased = new("Erased");

    public static Result<JobAdStatus> FromValue(string value) => value switch
    {
        "Active" => Result.Success(Active),
        "Expired" => Result.Success(Expired),
        "Archived" => Result.Success(Archived),
        "Erased" => Result.Success(Erased),
        _ => Result.Failure<JobAdStatus>(
            DomainError.Validation("JobAdStatus.Invalid", $"Okänd status: {value}"))
    };

    public override string ToString() => Value;
}
