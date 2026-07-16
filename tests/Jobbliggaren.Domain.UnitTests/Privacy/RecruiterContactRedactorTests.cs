using System.Text.Json;
using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

/// <summary>
/// The Tier-A detector (#842, ADR 0106 D4/D5). The corpus below is the DISCLOSED partition: what
/// the deterministic detector reaches (email + Swedish phone), what it deliberately does not
/// (names, obfuscation — Tier B's population), and the fixed-point/JSON-safety guarantees the
/// aggregate invariant rests on.
/// </summary>
public class RecruiterContactRedactorTests
{
    // ---- email detection ------------------------------------------------------------------

    [Theory]
    [InlineData("Maila anna@acme.se för mer info.", "anna@acme.se")]
    [InlineData("Kontakt: hakan.sjoberg@vaxjo.se.", "hakan.sjoberg@vaxjo.se")]
    [InlineData("Frågor? rekrytering+jobb@sub.acme-group.se gäller.", "rekrytering+jobb@sub.acme-group.se")]
    [InlineData("Sök via ansökan@åkeriet.se idag.", "ansökan@åkeriet.se")]
    public void Redact_removes_the_email_and_reports_the_span(string text, string expectedRaw)
    {
        var result = RecruiterContactRedactor.Redact(text);

        result.Scrubbed.ShouldNotContain(expectedRaw);
        result.Scrubbed.ShouldContain(RecruiterContactRedactor.Marker);
        var span = result.Found.ShouldHaveSingleItem();
        span.Kind.ShouldBe(ContactKind.Email);
        span.Raw.ShouldBe(expectedRaw);
        span.Normalized.ShouldBe(RecruiterContactRedactor.NormalizeEmail(expectedRaw));
    }

    [Fact]
    public void Redact_does_not_stop_at_trailing_sentence_punctuation()
    {
        // "maila anna@acme.se." — the final period is prose, not the address.
        var result = RecruiterContactRedactor.Redact("maila anna@acme.se.");

        result.Found.ShouldHaveSingleItem().Normalized.ShouldBe("anna@acme.se");
        result.Scrubbed.ShouldBe($"maila {RecruiterContactRedactor.Marker}.");
    }

    [Theory]
    [InlineData("Vi kör node@18.17.0 och vue@3.2.1 i stacken.")] // package pins: digit-only final label
    [InlineData("Följ @acmejobb på sociala medier.")] // handle: no domain dot
    public void Redact_leaves_technical_at_signs_alone(string text)
    {
        var result = RecruiterContactRedactor.Redact(text);

        result.Found.ShouldBeEmpty();
        result.Scrubbed.ShouldBe(text);
    }

    [Fact]
    public void Obfuscated_addresses_are_NOT_detected_and_that_is_the_disclosed_partition()
    {
        // anna(at)acme.se is Tier B's population (ADR 0106 D3: 17 obfuscated ads in 93 469; what
        // serves them is the NAME, via whole-record erasure). A detector that pretended to reach
        // this would be promising recall it cannot measure.
        var result = RecruiterContactRedactor.Redact("Skicka CV till anna(at)acme.se märkt Jobb.");

        result.Found.ShouldBeEmpty();
        result.Scrubbed.ShouldContain("anna(at)acme.se");
    }

    // ---- phone detection ------------------------------------------------------------------

    [Theory]
    [InlineData("Ring 070-123 45 67 vardagar.", "0701234567")]
    [InlineData("Tel: +46 70 123 45 67.", "0701234567")]
    [InlineData("Växel 08-123 456 78.", "0812345678")]
    [InlineData("Sms:a 0701234567 direkt.", "0701234567")]
    [InlineData("Ring +46 (0)8 123 456 78 innan fredag.", "0812345678")]
    public void Redact_removes_the_phone_and_reports_the_normalized_form(
        string text, string expectedNormalized)
    {
        var result = RecruiterContactRedactor.Redact(text);

        var span = result.Found.ShouldHaveSingleItem();
        span.Kind.ShouldBe(ContactKind.Phone);
        span.Normalized.ShouldBe(expectedNormalized);
        result.Scrubbed.ShouldContain(RecruiterContactRedactor.Marker);
        result.Scrubbed.ShouldNotContain("123 45 67");
    }

    [Fact]
    public void Redact_reaches_an_nbsp_separated_phone()
    {
        // The wire (and the un-escaped sanitized payload) carries NBSP between digit
        // groups — written as compiler escapes; literal invisible characters in source
        // are banned.
        var result = RecruiterContactRedactor.Redact("Ring 070\u00A0123\u00A045\u00A067 idag.");

        result.Found.ShouldHaveSingleItem().Normalized.ShouldBe("0701234567");
    }

    [Theory]
    [InlineData("Lön 35 000 kr i månaden.")] // no 0/+46 anchor
    [InlineData("Provision upp till 070 000 kr.")] // anchors, but 6 digits < the 7-digit floor
    [InlineData("Postnummer 123 45 Stockholm.")] // no anchor
    [InlineData("Publicerad 2026-07-16 kl 07:30.")] // dates and times never anchor into 7+ digits
    [InlineData("Org.nr 556012-5790.")] // an org.nr cannot anchor (never starts with 0)
    public void Redact_leaves_non_phone_digit_runs_alone(string text)
    {
        var result = RecruiterContactRedactor.Redact(text);

        result.Found.ShouldBeEmpty();
        result.Scrubbed.ShouldBe(text);
    }

    [Fact]
    public void A_candidate_never_eats_across_a_line_break()
    {
        // Newlines are NOT separators: an anchored digit at a line end must not swallow the next
        // line's list item (a date, a wage figure) into one giant "phone".
        var text = "Anställningsgrad: 0\n2026-07-16 tillträde.";

        var result = RecruiterContactRedactor.Redact(text);

        result.Found.ShouldBeEmpty();
        result.Scrubbed.ShouldBe(text);
    }

    [Fact]
    public void Redact_reaches_a_phone_separated_by_LITERAL_nbsp_escape_sequences()
    {
        // A JSON-escaped payload spells NBSP as the six characters backslash-u-0-0-a-0 (every
        // stock JavaScriptEncoder escapes U+00A0 — measured 2026-07-16). Without the detection
        // shadow this phone was scrubbed from description but SURVIVED in raw_payload — the gap
        // test-writer's funnel assertion (g) caught. The escape is built from char arithmetic so
        // no tool layer can decode it prematurely.
        var esc = new string((char)0x5C, 1) + "u00a0";
        var payload = "{\"text\":\"Ring 073" + esc + "042" + esc + "11" + esc + "22 idag.\"}";

        var result = RecruiterContactRedactor.Redact(payload);

        var span = result.Found.ShouldHaveSingleItem();
        span.Kind.ShouldBe(ContactKind.Phone);
        // Raw is the SHADOW slice: the escape's own zeroes must never pollute the digits.
        span.Normalized.ShouldBe("0730421122");
        result.Scrubbed.ShouldNotContain("0421122");
        result.Scrubbed.ShouldNotContain(esc);
        Should.NotThrow(() => JsonDocument.Parse(result.Scrubbed));
    }

    [Fact]
    public void An_email_with_a_digit_local_part_is_not_double_detected_as_a_phone()
    {
        var result = RecruiterContactRedactor.Redact("Svar till 0701234567@acme.se senast fredag.");

        var span = result.Found.ShouldHaveSingleItem();
        span.Kind.ShouldBe(ContactKind.Email);
        result.Scrubbed.ShouldNotContain("0701234567");
    }

    // ---- the marker's load-bearing guarantees ----------------------------------------------

    [Fact]
    public void The_marker_keeps_every_guarantee_the_invariant_rests_on()
    {
        var marker = RecruiterContactRedactor.Marker;

        marker.ShouldNotContain("@"); // not re-detectable as an email
        marker.Any(char.IsAsciiDigit).ShouldBeFalse(); // not re-detectable as a phone
        marker.ShouldNotContain("\""); // JSON-structural — would break the raw_payload text scrub
        marker.ShouldNotContain("\\");
        marker.ShouldNotContain("—"); // em-dash (CLAUDE.md §10)
    }

    [Fact]
    public void Redact_is_a_fixed_point_over_its_own_output()
    {
        var once = RecruiterContactRedactor.Redact(
            "Kontakta anna@acme.se eller ring 070-123 45 67. Märk ansökan REK-12.");

        var twice = RecruiterContactRedactor.Redact(once.Scrubbed);

        twice.Found.ShouldBeEmpty();
        twice.Scrubbed.ShouldBe(once.Scrubbed);
    }

    [Fact]
    public void Scrubbing_a_json_payload_as_text_keeps_it_parseable()
    {
        // ADR 0106 D4: raw_payload is scrubbed AS TEXT; the marker contains no JSON-structural
        // character, so replacing a substring inside a string value leaves the document valid.
        const string payload = """
            {"id":"ext-1","description":{"text":"Maila anna@acme.se eller ring 070-123 45 67."},"employer":{"name":"Acme AB"}}
            """;

        var result = RecruiterContactRedactor.Redact(payload);

        result.Found.Count.ShouldBe(2);
        Should.NotThrow(() => JsonDocument.Parse(result.Scrubbed));
        result.Scrubbed.ShouldNotContain("anna@acme.se");
        result.Scrubbed.ShouldNotContain("070-123 45 67");
    }

    // ---- the recogniser owns the normalization (#844) --------------------------------------

    [Theory]
    [InlineData("Anna.Karlsson@ACME.SE", "anna.karlsson@acme.se")]
    [InlineData("  anna@acme.se. ", "anna@acme.se")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void NormalizeEmail_is_the_single_canonical_form(string? input, string? expected)
        => RecruiterContactRedactor.NormalizeEmail(input).ShouldBe(expected);

    [Theory]
    [InlineData("+46 70 123 45 67", "0701234567")]
    [InlineData("0046701234567", "0701234567")]
    [InlineData("070-123 45 67", "0701234567")]
    [InlineData("46701234567", "0701234567")] // country form without '+' — full length disambiguates
    [InlineData("inga siffror", null)]
    [InlineData(null, null)]
    public void NormalizePhone_is_the_single_canonical_form(string? input, string? expected)
        => RecruiterContactRedactor.NormalizePhone(input).ShouldBe(expected);

    // ---- never throws -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_handles_absent_text(string? text)
    {
        var result = RecruiterContactRedactor.Redact(text);

        result.Scrubbed.ShouldBe(string.Empty);
        result.Found.ShouldBeEmpty();
    }
}
