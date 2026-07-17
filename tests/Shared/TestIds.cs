namespace Jobbliggaren.TestSupport;

/// <summary>
/// Random-but-safe test identifiers for text that flows through the ingest redaction funnel
/// (<c>JobAd.Import</c> → <c>ApplyContactRedaction</c> → <c>RecruiterContactRedactor</c>).
/// Linked into test projects the same way as <see cref="TestFacets"/> (see the
/// <c>Compile Include</c> items in the test csproj files).
///
/// <para>
/// <b>Why a raw <c>Guid.NewGuid().ToString("N")</c> is not a safe test id.</b> The phone
/// detector anchors on a leading <c>0</c> whose preceding character is not a letter, digit or
/// hyphen — and a quote, a space or a <c>%</c> is none of those. When the GUID hex happens to
/// start with <c>0</c> followed by six or more decimal digits before the first a–f letter
/// (≈0.4 % of draws), the run normalizes to 7–11 digits, passes the candidate gate, and the
/// span is scrubbed AND promoted as an <c>ExtractedFromBody</c> contact. A test that embedded
/// the hex in a seeded title, description or payload then fails on an assertion about text or
/// contacts it never wrote — green in isolation, red once every ~250 CI runs (the 2026-07-17
/// PR #921 incident; the same class as the GUID-hex-title incident recorded in
/// <c>PhoneRegex</c>'s own comment).
/// </para>
///
/// <para>
/// <b>The letter prefix is the entire mechanism.</b> A phone candidate must START at a
/// <c>0</c> or <c>+46</c>; every id built here starts with <see cref="Prefix"/> (an ASCII
/// letter), so index 0 can never anchor — and every LATER <c>0</c> in the id is preceded by a
/// hex character (a letter or digit), which the detector's lookbehind refuses. The guarantee
/// is positional, not probabilistic; <c>RecruiterContactRedactorTests</c> pins it against the
/// real redactor with the known-bad hex as counterfactual.
/// </para>
/// </summary>
internal static class TestIds
{
    /// <summary>
    /// The disarming first character. MUST remain an ASCII letter — the redactor's phone
    /// anchor (<c>0</c>/<c>+46</c> at candidate start) is what it exists to make unreachable.
    /// Pinned by <c>RecruiterContactRedactorTests</c>; changing it to a digit turns every
    /// caller back into a lottery ticket.
    /// </summary>
    internal const string Prefix = "x";

    /// <summary>A full-entropy external id (33 chars: prefix + 32 hex), uniqueness of a GUID.</summary>
    internal static string ExternalId() => Prefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// A short unique token for embedding in seeded titles/descriptions (prefix +
    /// <paramref name="hexChars"/> hex). Same collision envelope as the truncated-GUID tokens
    /// it replaces — the prefix adds safety, not entropy.
    /// </summary>
    internal static string Token(int hexChars = 12) =>
        Prefix + Guid.NewGuid().ToString("N")[..hexChars];
}
