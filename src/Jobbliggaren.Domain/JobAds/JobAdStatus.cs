using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

public sealed record JobAdStatus
{
    public string Value { get; }

    private JobAdStatus(string value) => Value = value;

    public static readonly JobAdStatus Active = new("Active");
    public static readonly JobAdStatus Expired = new("Expired");
    public static readonly JobAdStatus Archived = new("Archived");

    public static Result<JobAdStatus> FromValue(string value) => value switch
    {
        "Active" => Result.Success(Active),
        "Expired" => Result.Success(Expired),
        "Archived" => Result.Success(Archived),
        _ => Result.Failure<JobAdStatus>(
            DomainError.Validation("JobAdStatus.Invalid", $"Okänd status: {value}"))
    };

    public override string ToString() => Value;
}
