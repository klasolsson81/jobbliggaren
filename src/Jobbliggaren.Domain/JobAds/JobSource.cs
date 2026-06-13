using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

public sealed record JobSource
{
    public string Value { get; }

    private JobSource(string value) => Value = value;

    public static readonly JobSource Manual = new("Manual");
    public static readonly JobSource Platsbanken = new("Platsbanken");
    public static readonly JobSource LinkedIn = new("LinkedIn");

    public static Result<JobSource> FromValue(string value) => value switch
    {
        "Manual" => Result.Success(Manual),
        "Platsbanken" => Result.Success(Platsbanken),
        "LinkedIn" => Result.Success(LinkedIn),
        _ => Result.Failure<JobSource>(
            DomainError.Validation("JobSource.Invalid", $"Okänd källa: {value}"))
    };

    public override string ToString() => Value;
}
