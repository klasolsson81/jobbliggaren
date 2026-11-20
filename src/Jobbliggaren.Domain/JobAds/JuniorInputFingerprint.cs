using System.Security.Cryptography;
using System.Text;

namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #2264 — an absolute fingerprint of everything the junior rules read of an ad (post-scrub title and text, occupation
/// group, experience mark). Ingestion writes it on every source write; the classification stamps the fingerprint of
/// the content it actually classified. Unequal fingerprints mean the verdict is about other content and the ad is
/// classified again — whichever writer raced whichever, the verdict always ends up about the row's own content.
/// </summary>
public static class JuniorInputFingerprint
{
    public const int Length = 64;

    public static string Of(string title, string description, string? occupationGroupConceptId, bool? experienceRequired)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);

        // Length-prefixed fields, so no choice of content can make two different inputs serialise alike.
        var builder = new StringBuilder();
        foreach (var field in new[] { title, description, occupationGroupConceptId ?? "", Flag(experienceRequired) })
            builder.Append(field.Length).Append(':').Append(field).Append('\n');

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Flag(bool? value) => value switch
    {
        true => "required",
        false => "not-required",
        null => "unknown",
    };
}
