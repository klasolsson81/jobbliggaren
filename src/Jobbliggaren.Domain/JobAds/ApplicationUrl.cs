using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// The employer's own application page for an imported ad: JobTech's
/// <c>application_details.url</c> ("Address to employers application web adress", AdFields.md).
/// It is a different address from <see cref="JobAd.Url"/>, which is the ad's page at the source.
/// </summary>
/// <remarks>
/// <see cref="FromSource"/> is total: whatever the wire carries yields a value or null, never a
/// failure. A rejected address must not cost the user the ad, and a failed import or update is
/// <c>Skipped</c> by the upsert handler. What it refuses:
/// <list type="bullet">
/// <item>Anything but an absolute http(s) URL with a host. The value is rendered as an
/// <c>href</c> in an authenticated session, so <c>javascript:</c>, <c>data:</c> and the rest are
/// default-denied (the TD-80 rule <see cref="JobAd.Url"/> follows).</item>
/// <item>An <c>@</c> or <c>%40</c> anywhere. That covers <c>mailto:</c>, userinfo and an email
/// address embedded in the path or query, all of which would put a recruiter's address on the
/// row; <c>application_details</c> is kept out of <c>raw_payload</c> for the same reason.</item>
/// <item>A validated personnummer in any of the address's <see cref="Readings"/> (CLAUDE.md §5).</item>
/// <item>A control character anywhere inside it. A URL parser drops a tab or a line break before it reads
/// an address, so the stored string would not be the address a page links to.</item>
/// <item>A host <see cref="Uri.IdnHost"/> cannot map.</item>
/// </list>
/// The phone detector is deliberately not applied: a numeric applicant-tracking id is
/// phone-shaped to it, and a false positive would hide a valid link. The public job page applies
/// it on top of this rule (<c>PublicApplicationLink</c>, #2214 PR 1b), where the cost is the link
/// on that page alone.
/// </remarks>
public sealed record ApplicationUrl
{
    public const int MaxLength = 2000;

    public string Value { get; }

    private ApplicationUrl(string value) => Value = value;

    public static ApplicationUrl? FromSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value.Length > MaxLength || value.Any(char.IsControl))
            return null;

        if (value.Contains('@', StringComparison.Ordinal)
            || value.Contains("%40", StringComparison.Ordinal))
            return null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !HasMappableHost(uri))
            return null;

        foreach (var reading in Readings(value))
        {
            if (PersonnummerScanner.ScanWithGaps(reading).Count > 0)
                return null;
        }

        return new ApplicationUrl(value);
    }

    /// <summary>
    /// The address as a reader of the link can read it: as stored, percent-decoded, and form-decoded
    /// (<c>+</c> as a space, then percent-decoded), each as it is and with any tab or line break removed, and each once. A token behind
    /// <c>%2D</c>, <c>%20</c> or <c>+</c> is still that token to whoever reads the <c>href</c>, so a rule
    /// that looks for one looks in every reading. The one home of these forms (ADR 0166 D10 trigger 19
    /// names them). <see cref="Uri.UnescapeDataString(string)"/> throws only on null.
    /// </summary>
    public static IReadOnlyList<string> Readings(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] forms = [value, Uri.UnescapeDataString(value), Uri.UnescapeDataString(value.Replace('+', ' '))];
        return forms.Concat(forms.Select(WithoutTabOrLineBreak))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string WithoutTabOrLineBreak(string reading) =>
        string.Concat(reading.Where(c => c is not ('\t' or '\n' or '\r')));

    private static bool HasMappableHost(Uri uri)
    {
        try
        {
            var host = uri.IdnHost;
            return host.Length > 0 && host.All(char.IsAscii);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reconstructs a persisted value (EF materialisation only). The column was written through
    /// <see cref="FromSource"/>, so it is not re-validated.
    /// </summary>
    public static ApplicationUrl FromTrusted(string value) => new(value);
}
