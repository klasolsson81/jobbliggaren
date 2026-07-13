using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds;

public sealed record Company
{
    public string Name { get; }

    private Company(string name) => Name = name;

    /// <summary>
    /// The company of an erased ad (ADR 0106 Tier B, #842). Not a real company — a tombstone
    /// marker, never rendered (an erased ad returns 410 from every read path).
    /// </summary>
    /// <remarks>
    /// <see cref="JobAd.Erase"/> must clear the company name, not only the body. An
    /// <i>enskild firma</i>'s company name <b>is</b> a natural person's name (which is also why
    /// <c>organization_number</c> may be a personnummer — see
    /// <c>JobTechSearchResponse.JobTechEmployer</c>), and the erasure command matches against
    /// <c>raw_payload</c>, which carries <c>employer.name</c>. Without this, a request naming
    /// that person would match her ad, erase it, and leave her name sitting in
    /// <c>job_ads.company_name</c> — while we told her it was erased. That is the #842 defect
    /// class, and it is the reason the erasure claim must be "we deleted the carrier", not
    /// "we deleted the fields we thought of".
    /// <para>
    /// The Swedish literal is stored data, not UI copy, so it does not belong in
    /// <c>messages/sv.json</c> (ADR 0106 D4 binds the same exception for the redaction marker).
    /// </para>
    /// </remarks>
    public static Company Erased { get; } = new("[raderad]");

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
