using System.Text;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Detects and removes recruiter contact details (email + Swedish phone) from ad free text
/// (#842 Tier A, ADR 0106 D4/D5; CTO re-bind R1). Sibling of <see cref="PersonnummerRedactor"/>:
/// static, <c>GeneratedRegex</c>, deterministic, never throws, idempotent. Returns BOTH the
/// scrubbed text and the detected spans, because the scrub is a MIGRATION to a safe carrier, not a
/// destruction — every span is promoted into the ad's structured <c>AdContacts</c> field by the
/// aggregate (re-bind R1(b): refusing to promote would delete a contact the user needs, in the name
/// of a minimisation we are not achieving).
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection surface is email + Swedish phone. Nothing else, deliberately.</b> A person-name is
/// not deterministically detectable and never will be (no NER — ADR 0106 D5 rejection, re-bind V6);
/// textual obfuscation (<c>anna(at)acme.se</c>) is Tier B's population. The detector is imperfect
/// BY DESIGN and the public disclosure says so (D12). Its recall is MEASURED (D11), not asserted.
/// </para>
/// <para>
/// <b>The marker is a fixed point.</b> Each detected span is replaced by <see cref="Marker"/>, which
/// contains no <c>@</c>, no digits, no JSON-structural character (<c>"</c>, <c>\</c>) and no
/// em-dash — so re-running the redactor over scrubbed text detects nothing and the nightly
/// re-ingest cannot grow or re-mangle it. The same property lets the sanitized
/// <c>raw_payload</c> be scrubbed AS TEXT while remaining valid JSON (ADR 0106 D4; pinned by a
/// test). The Swedish literal is stored ad data, not UI copy — the recorded §5 exception
/// (same as <c>Company.Erased</c>).
/// </para>
/// <para>
/// <b>The recogniser owns the question, the split and the normalization.</b>
/// <see cref="NormalizeEmail"/>/<see cref="NormalizePhone"/> are THE canonical forms;
/// <see cref="ContactSpan.Normalized"/> carries them, and <c>AdContacts</c>' merge/dedup compares
/// on them without re-normalizing. A rule with two normalisers is two rules (#844, veto ×4).
/// </para>
/// <para>
/// <b>Phone anchor keeps the false positives out.</b> Candidates must start with <c>0</c> or
/// <c>+46</c> and normalize to 7–11 digits — salaries (<c>35 000</c>), postal codes
/// (<c>123 45</c>) and dates (<c>2026-07-16</c>) never anchor; short runs (<c>070 000 kr</c> = 6
/// digits) fail the floor. An org.nr caught as collateral would be a GOOD false positive
/// (minimisation), but cannot anchor either (org.nr never starts with 0). A personnummer written
/// <c>070712-3456</c> (born July 2007) IS phone-shaped and gets redacted — over-redaction of PII
/// is the bound fail-safe direction.
/// </para>
/// </remarks>
public static partial class RecruiterContactRedactor
{
    /// <summary>
    /// The replacement marker (ADR 0106 D4 — stored data, not UI copy; §5 exception recorded).
    /// MUST stay free of <c>@</c>, digits, <c>"</c>, <c>\</c> and em-dash: those guarantees are
    /// what make the scrub a fixed point and the scrubbed <c>raw_payload</c> valid JSON. Pinned by
    /// <c>RecruiterContactRedactorTests</c>.
    /// </summary>
    public const string Marker = "[kontaktuppgift borttagen, se annonsen hos Arbetsförmedlingen]";

    /// <summary>
    /// Scrubs <paramref name="text"/> and reports every detected span. Never throws; null/empty
    /// input yields an empty result. Emails are replaced before phones so a digit-bearing email
    /// local part (<c>0701234567@acme.se</c>) cannot be double-detected by the phone pass.
    /// </summary>
    public static ContactRedactionResult Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new ContactRedactionResult(text ?? string.Empty, []);

        List<ContactSpan>? found = null;

        // Detection runs over a LENGTH-PRESERVING shadow of the text (NBSP forms normalized to
        // spaces — see DetectionShadow); replacement happens in the ORIGINAL at the same offsets.
        var emailShadow = DetectionShadow(text);
        var afterEmails = ReplaceMatches(
            text, EmailRegex().Matches(emailShadow), ContactKind.Email, ref found);

        // Fresh shadow after the email pass — the inserted markers shifted every offset.
        var phoneShadow = DetectionShadow(afterEmails);
        var afterPhones = ReplaceMatches(
            afterEmails, PhoneRegex().Matches(phoneShadow), ContactKind.Phone, ref found);

        return found is null
            ? new ContactRedactionResult(text, [])
            : new ContactRedactionResult(afterPhones, found);
    }

    /// <summary>
    /// The recogniser's canonical VIEW of the text (#844: the recogniser owns the question, the
    /// split and the normalization — including how whitespace forms are read). Length-preserving,
    /// so a match's offsets map 1:1 back to the original: a real NBSP (U+00A0) becomes one space,
    /// and the six-character LITERAL escape sequence (backslash, u, 0, 0, a-or-A, 0) becomes six
    /// spaces. The second form is how a JSON-escaped payload spells NBSP — every stock
    /// <c>JavaScriptEncoder</c> (including <c>UnsafeRelaxedJsonEscaping</c> — measured 2026-07-16)
    /// escapes U+00A0, so an NBSP-separated phone inside <c>raw_payload</c> reads as digits
    /// separated by escape sequences. Without the shadow, that phone was scrubbed from
    /// <c>description</c> but SURVIVED in the payload copy (found by test-writer's assertion (g)).
    /// Spans carry the SHADOW slice as <see cref="ContactSpan.Raw"/> — the escape's own zeroes
    /// must never leak into the digit normalization.
    /// </summary>
    /// <remarks>
    /// Accepted over-read, fail-safe direction: JSON text spelling a REAL backslash before
    /// <c>u00a0</c> (a doubled backslash in the document, i.e. a literal backslash plus the five
    /// characters u00a0 in the decoded value) is read as the NBSP form too. Over-normalizing a
    /// separator can only widen detection — over-redaction of PII-adjacent text is the bound
    /// posture (ADR 0106 D5).
    /// </remarks>
    private static string DetectionShadow(string text)
    {
        const char Nbsp = (char)0x00A0;

        char[]? chars = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == Nbsp)
            {
                chars ??= text.ToCharArray();
                chars[i] = ' ';
                continue;
            }

            if (c == '\\'
                && i + 5 < text.Length
                && text[i + 1] == 'u'
                && text[i + 2] == '0'
                && text[i + 3] == '0'
                && (text[i + 4] == 'a' || text[i + 4] == 'A')
                && text[i + 5] == '0')
            {
                chars ??= text.ToCharArray();
                for (var k = 0; k < 6; k++)
                    chars[i + k] = ' ';
                i += 5;
            }
        }

        return chars is null ? text : new string(chars);
    }

    /// <summary>
    /// Canonical email comparison form: trimmed, trailing dots dropped, lower-cased invariant.
    /// Null/blank → null (the <c>JobAdFacets</c> blank-is-absence lesson).
    /// </summary>
    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var trimmed = email.Trim().TrimEnd('.');
        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Canonical Swedish phone comparison form: digits only, international prefix folded
    /// (<c>+46 70 …</c> / <c>0046 70 …</c> → <c>070…</c>). Null/blank or digit-free → null.
    /// This is a COMPARISON form for dedup, not a validity claim — declared numbers from the wire
    /// are normalized with the same function so the promote step cannot disagree with the
    /// detector about what "the same number" is.
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var digits = new StringBuilder(phone.Length);
        foreach (var c in phone)
        {
            if (char.IsAsciiDigit(c))
                digits.Append(c);
        }

        if (digits.Length == 0)
            return null;

        var s = digits.ToString();
        if (s.StartsWith("0046", StringComparison.Ordinal))
            return CollapseTrunkZero("0" + s[4..]);
        // A national 0-form number never begins with 4 (area codes start with 0), so a 46-prefixed
        // run of full length is unambiguously the country form — with or without its '+'.
        if (s.StartsWith("46", StringComparison.Ordinal) && s.Length >= 10)
            return CollapseTrunkZero("0" + s[2..]);
        return s;
    }

    // "+46 (0)8 123 456 78" carries the parenthesized trunk zero INSIDE the country form — folding
    // 46→0 then yields "00812345678". No national number starts 00 (the international 00-prefix is
    // folded before this runs), so a leading double zero is always that duplicated trunk zero.
    private static string CollapseTrunkZero(string folded) =>
        folded.StartsWith("00", StringComparison.Ordinal) ? folded[1..] : folded;

    // Matches come from the SHADOW (the recogniser's canonical view); replacement slices the
    // ORIGINAL at the same offsets (the shadow is length-preserving, so they align). Spans carry
    // the shadow slice — the escape form's own zeroes must never reach the digit normalization.
    private static string ReplaceMatches(
        string original,
        MatchCollection matches,
        ContactKind kind,
        ref List<ContactSpan>? found)
    {
        if (matches.Count == 0)
            return original;

        StringBuilder? buffer = null;
        var consumedThrough = 0;

        foreach (Match match in matches)
        {
            if (kind == ContactKind.Phone && !IsValidPhoneCandidate(match.Value))
                continue;

            var normalized = kind == ContactKind.Email
                ? NormalizeEmail(match.Value)
                : NormalizePhone(match.Value);
            if (normalized is null)
                continue;

            found ??= [];
            found.Add(new ContactSpan(match.Value, normalized, kind));

            buffer ??= new StringBuilder(original.Length);
            buffer.Append(original, consumedThrough, match.Index - consumedThrough).Append(Marker);
            consumedThrough = match.Index + match.Length;
        }

        if (buffer is null)
            return original;

        buffer.Append(original, consumedThrough, original.Length - consumedThrough);
        return buffer.ToString();
    }

    // The regex proposes; the digit-count gate disposes (the PersonnummerScanner shape: a cheap
    // candidate pattern, then a strict validator, so only REAL phone-shaped runs are touched).
    // 7–11 digits in the normalized 0-form is the bound envelope (ADR 0106 D5).
    private static bool IsValidPhoneCandidate(string candidate)
    {
        var normalized = NormalizePhone(candidate);
        return normalized is { Length: >= 7 and <= 11 };
    }

    // In-text email, WHATWG-shaped and adapted for scanning (ADR 0106 D5): åäö and any letter via
    // \p{L} in the local part and domain labels; the FINAL label must start with a letter and be
    // ≥2 chars, which keeps package versions ("node@18.17.0") and digit-dotted junk out while
    // over-matching a sentence junction ("…@acme.se.Vi") — over-redaction is the bound fail-safe
    // direction. The lookbehind stops a partial re-match inside a longer token.
    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}@._%+-])[\p{L}\p{Nd}._%+-]+@[\p{L}\p{Nd}-]+(?:\.[\p{L}\p{Nd}-]+)*\.\p{L}[\p{L}\p{Nd}-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // Swedish phone candidates: anchored on a leading 0 or +46 (the anchor IS the false-positive
    // control — salaries, postal codes and dates never start there). The lookbehind refuses an anchor GLUED to a letter, digit or hyphen: a hex id (Facets-0123456...) or a product code (SKU-A0123456) is an identifier, never a phone; a real Swedish phone form is preceded by whitespace, spaced punctuation or line start. (Found live: a GUID-hex test title anchored, was scrubbed at ingest, and the test's title lookup found nothing.) Then 6–12 further digits with
    // optional separators: space, tab, NBSP (via the \u00A0 REGEX escape — a literal invisible
    // character in source is banned), hyphen, parens (the +46 (0)8 form). NOT in the class,
    // deliberately: dots and slashes (version strings, dates) and newlines (a candidate must not
    // eat across a line break into the next list item). The digit-count gate above enforces the
    // 7–11 envelope on the normalized form.
    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}\-])(?:\+46|0)(?:[ \t\u00A0()\-]*\d){6,12}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
}

/// <summary>
/// The redactor's result (#842 Tier A): the scrubbed text plus every detected span, in document
/// order per kind (emails first — the replacement order above). <see cref="Found"/> is what the
/// aggregate promotes into <c>AdContacts</c>; discarding it would be destruction, not migration.
/// </summary>
public sealed record ContactRedactionResult(string Scrubbed, IReadOnlyList<ContactSpan> Found);
