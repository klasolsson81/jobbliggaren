using System.Collections.Frozen;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Privacy;

namespace Jobbliggaren.Application.JobAds.Publication;

/// <summary>
/// The one gate between an ad's stored application link and the anonymous public read (#2214 PR 1b,
/// ADR 0166 D10 trigger 19): it gives the stored value, or null when the link is a contact channel.
/// It never rewrites the value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Null when any of these holds</b>, in this order:
/// <list type="bullet">
/// <item>the value no longer passes <see cref="ApplicationUrl.FromSource"/>, which runs again here so
/// that a tightened write rule reaches every stored row at once;</item>
/// <item>the host, after IDN mapping, is not plain LDH ASCII, so a host the vocabulary cannot be
/// compared with is never admitted;</item>
/// <item>the host, or a host it is a subdomain of, is a profile, social or messaging host
/// (<see cref="ContactHosts"/>) or a link shortener (<see cref="Shorteners"/>), whose target we cannot
/// see;</item>
/// <item>the host is one that also serves personal pages (<see cref="AllowedFirstSegments"/>) and the
/// path, as <see cref="Uri.AbsolutePath"/> resolves it, is not one of its job or company paths;</item>
/// <item>Tier A's recogniser (<see cref="RecruiterContactRedactor"/>) finds a span in any of the
/// value's <see cref="ApplicationUrl.Readings"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Only the public read asks.</b> The signed-in detail keeps every link <c>FromSource</c> admits,
/// as it shows the contact block (ADR 0106), so this is a rule of one surface and lives here, beside
/// <see cref="JobAdPublication"/>. A false positive costs the link on the public page alone; the page
/// then links to the ad itself. Nothing here logs.
/// </para>
/// <para>
/// <b>The lists narrow the surface in opposite directions.</b> A host added to <see cref="ContactHosts"/>
/// or <see cref="Shorteners"/> nulls more links; a segment added to <see cref="AllowedFirstSegments"/>
/// admits more. <c>PublicApplicationLinkTests</c> pins every member of all three.
/// </para>
/// </remarks>
internal static class PublicApplicationLink
{
    /// <summary>Hosts of a person's profile, posts or messages, or of a personal booking page.</summary>
    internal static readonly FrozenSet<string> ContactHosts = new[]
    {
        "facebook.com", "fb.com", "fb.me", "m.me", "messenger.com",
        "instagram.com", "instagr.am", "threads.net", "threads.com", "snapchat.com", "tiktok.com",
        "x.com", "twitter.com", "bsky.app",
        "wa.me", "wa.link", "whatsapp.com", "t.me", "telegram.me", "signal.me", "signal.group",
        "calendly.com",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Link shorteners: the target is not in the link and can change after the import.</summary>
    internal static readonly FrozenSet<string> Shorteners = new[]
    {
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "rb.gy", "is.gd", "buff.ly", "lnkd.in",
        "cutt.ly", "t.ly", "shorturl.at", "tiny.cc", "rebrand.ly", "s.id",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Hosts that serve job and company pages beside personal ones, with the only first path segments
    /// admitted on them. Any other path, an empty one included, is a person's page or may be.
    /// </summary>
    internal static readonly FrozenDictionary<string, FrozenSet<string>> AllowedFirstSegments =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            ["linkedin.com"] = new[] { "jobs", "company" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static string? Admit(ApplicationUrl? applicationUrl)
    {
        if (applicationUrl is null || ApplicationUrl.FromSource(applicationUrl.Value) is not { } current)
            return null;

        if (!Uri.TryCreate(current.Value, UriKind.Absolute, out var uri))
            return null;

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!IsLdhAscii(host))
            return null;

        if (Listed(host, ContactHosts.Contains) is not null || Listed(host, Shorteners.Contains) is not null)
            return null;

        if (Listed(host, AllowedFirstSegments.ContainsKey) is { } restricted
            && !IsAllowedPath(uri.AbsolutePath, AllowedFirstSegments[restricted]))
            return null;

        foreach (var reading in ApplicationUrl.Readings(current.Value))
        {
            if (RecruiterContactRedactor.Redact(reading).Found.Count > 0)
                return null;
        }

        return current.Value;
    }

    /// <summary>The host itself, or the nearest domain it is a subdomain of, that the list holds; or null.</summary>
    private static string? Listed(string host, Func<string, bool> holds)
    {
        for (var candidate = host; ;)
        {
            if (holds(candidate))
                return candidate;

            var dot = candidate.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
                return null;
            candidate = candidate[(dot + 1)..];
        }
    }

    private static bool IsAllowedPath(string absolutePath, FrozenSet<string> allowedFirstSegments)
    {
        var segments = absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        return segments.Length > 0 && allowedFirstSegments.Contains(segments[0]);
    }

    private static bool IsLdhAscii(string host) =>
        host.Length > 0 && host.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-');
}
