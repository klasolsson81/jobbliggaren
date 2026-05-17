using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Applications;

/// <summary>
/// Manuellt angiven jobbmetadata för en ansökan som inte är kopplad till en
/// JobAd-annons (Application.JobAdId == null). Value object ägt av
/// Application-aggregatet (ADR 0048 Beslut d — skrivvägen vidgas ej till
/// cross-aggregat). En ManualPosting är per definition Source=Manual; någon
/// Source-property finns medvetet inte (dead axis, CLAUDE.md §5.1) —
/// read-vägen projicerar literalen "Manual".
/// </summary>
public sealed record ManualPosting
{
    public string Title { get; }
    public string Company { get; }
    public string? Url { get; }
    public DateTimeOffset? ExpiresAt { get; }

    private ManualPosting(string title, string company, string? url, DateTimeOffset? expiresAt)
    {
        Title = title;
        Company = company;
        Url = url;
        ExpiresAt = expiresAt;
    }

    public static Result<ManualPosting> Create(
        string? title, string? company, string? url, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.TitleRequired", "Jobbtitel är obligatorisk."));
        if (title.Length > 300)
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.TitleTooLong", "Jobbtitel får vara max 300 tecken."));

        if (string.IsNullOrWhiteSpace(company))
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.CompanyRequired", "Företag är obligatoriskt."));
        if (company.Length > 200)
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.CompanyTooLong", "Företag får vara max 200 tecken."));

        string? normalizedUrl = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            // TD-80 — IDENTISK scheme-whitelist som JobAd.ValidateCore. Samma
            // OWASP A01-yta; duplicera ej slarvigt — samma regel verbatim.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
                || (parsedUri.Scheme != Uri.UriSchemeHttp
                    && parsedUri.Scheme != Uri.UriSchemeHttps))
                return Result.Failure<ManualPosting>(
                    DomainError.Validation("ManualPosting.UrlInvalid",
                        "Annonslänk måste vara en giltig http(s)-URL."));
            if (url.Length > 2000)
                return Result.Failure<ManualPosting>(
                    DomainError.Validation("ManualPosting.UrlTooLong",
                        "Annonslänk får vara max 2000 tecken."));
            normalizedUrl = url.Trim();
        }

        return Result.Success(new ManualPosting(title.Trim(), company.Trim(), normalizedUrl, expiresAt));
    }
}
