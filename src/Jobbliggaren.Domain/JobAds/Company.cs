using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

public sealed record Company
{
    public string Name { get; }

    private Company(string name) => Name = name;

    public static Result<Company> Create(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Company>(
                DomainError.Validation("Company.NameRequired", "Företagsnamn är obligatoriskt."));
        if (name.Length > 200)
            return Result.Failure<Company>(
                DomainError.Validation("Company.NameTooLong", "Företagsnamn får vara max 200 tecken."));

        return Result.Success(new Company(name.Trim()));
    }
}
