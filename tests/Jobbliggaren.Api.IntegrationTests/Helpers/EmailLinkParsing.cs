namespace Jobbliggaren.Api.IntegrationTests.Helpers;

/// <summary>
/// #981 — parse an emailed link the way a browser + the Next.js App Router do, so an integration test can
/// drive the REAL activation / confirm link (rendered by <c>EmailTemplates</c>) through the endpoint's
/// System.Text.Json <see cref="System.Guid"/> binder — the seam where the N-format-uid bug lived. A test
/// that POSTs a <see cref="System.Guid"/> OBJECT never crosses that seam: STJ serializes a Guid to the
/// dashed "D" form, so the compact "N" form the template emitted was never exercised (which is exactly
/// why the pre-existing end-to-end tests stayed green while every real activation 400'd).
/// </summary>
internal static class EmailLinkParsing
{
    /// <summary>
    /// Extract the query parameters of the first <paramref name="path"/> link in an email body, as the
    /// values LITERALLY appear in the URL (still percent-encoded, '+' not yet decoded). The link occupies
    /// a single line, so it is read from just after "<paramref name="path"/>?" up to the next whitespace.
    /// </summary>
    public static Dictionary<string, string> ExtractLinkQuery(string body, string path)
    {
        var marker = path + "?";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"No '{path}' link found in the email body.");

        start += marker.Length;
        var end = start;
        while (end < body.Length && !char.IsWhiteSpace(body[end]))
            end++;
        var query = body[start..end];

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                result[pair] = string.Empty;
            else
                result[pair[..eq]] = pair[(eq + 1)..];
        }
        return result;
    }

    /// <summary>
    /// Decode a query value exactly as a browser's <c>URLSearchParams</c> / Next's <c>useSearchParams</c>
    /// does: application/x-www-form-urlencoded semantics — '+' becomes a space FIRST, then percent-
    /// sequences are decoded. A Base64Url token ([A-Za-z0-9_-]) survives unchanged; a raw-base64 '+' would
    /// be corrupted into a space (the transport #981 warned about — the token is pinned url-safe elsewhere).
    /// </summary>
    public static string BrowserDecodeQueryValue(string raw)
        => Uri.UnescapeDataString(raw.Replace('+', ' '));
}
