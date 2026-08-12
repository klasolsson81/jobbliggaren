using System.Text.Json;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// Pins <see cref="EmailTemplates.ContactAddress"/> against the address the web app publishes
/// (2026-08-12, Klas-beslut).
///
/// <para>
/// The address has two homes across the stack and no way to keep them equal by construction: three
/// security notices tell people to write to it, and <c>/kontakt</c>, the privacy policy and the terms
/// all state it from <c>messages/{sv,en}/content-legal.json</c>. Drift here is worse than a cosmetic
/// mismatch — a mail could send someone to an address the published policy does not name, on exactly
/// the mails that matter when an account may be compromised, and the policy's copy is the controller
/// contact required by Art. 13(1)(b).
/// </para>
///
/// <para>
/// Same move as <c>EmailPaletteMirrorsDesignTokensTests</c>: the copy is made CHECKABLE rather than
/// merely declared. And the direction matters the same way — the published policy is the statement of
/// record, so when this fails, fix whichever side is wrong deliberately, never the expectation here.
/// </para>
///
/// <para>
/// <b>Why the whole file rather than the one key.</b> <c>contact.email</c> is only one of FIVE places
/// the address appears per language, and the other four are prose. Measured 2026-08-12 by walking the
/// JSON: <c>privacy.sections[0]</c> (the controller contact), <c>terms.sections[0]</c>,
/// <c>accessibility.sections[3]</c> (the accessibility statement) and
/// <c>recruiterNotice.sections[3]</c> ("är du kontaktperson i en annons?", the Art. 17/21 route for
/// people named in ad text). Asserting the key alone would pass while four prose passages still named
/// the old address, which is the shape of every "fix that landed in one place out of N" this lane has
/// produced.
/// </para>
///
/// <para>
/// <b>An earlier version of this list named the terms and the COOKIE POLICY.</b> The cookie policy
/// carries no address at all, and the Art. 15-22 rights paragraph uses a RELATIVE reference
/// ("e-postadressen ovan") rather than the address itself — which is good copy architecture, since it
/// makes that route self-updating, and is exactly why it is not in the list. Two of four names were
/// wrong, and both reviewers measured it independently.
/// </para>
/// </summary>
public class ContactAddressMatchesPublishedContactTests
{
    /// <summary>
    /// Characters that bound a token in this JSON. The quote and comma matter: an address sits inside
    /// a quoted string and often ends a clause, so splitting on whitespace alone would leave
    /// <c>"kontakt@jobbliggaren.se,</c> as one token and fail against itself.
    /// </summary>
    private static readonly char[] TokenSeparators = [' ', '\n', '\r', '\t', '"', ',', '(', ')'];

    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void ContactAddress_MatchesTheAddressPublishedInTheWebApp(string language)
    {
        var json = File.ReadAllText(ContentLegalPath(language));

        // The structured key the /kontakt page renders from.
        using var document = JsonDocument.Parse(json);
        document.RootElement
            .GetProperty("contact")
            .GetProperty("email")
            .GetString()
            .ShouldBe(EmailTemplates.ContactAddress);

        // And every prose passage: NO OTHER address may survive anywhere in the file. Asserted at
        // TOKEN level, not line level. A line-level ShouldContain says "this line mentions our
        // address", which a line carrying both ours and a stranger's satisfies — so the prose claimed
        // the strong property while the code implemented the weak one (code-reviewer Major 4 /
        // security-auditor Minor 1, 2026-08-12). The sibling fact in
        // EmailTemplatesEmailChangedNotificationTests already used this form, and two strengths of one
        // property inside one PR is exactly the drift to avoid.
        foreach (var token in json.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.Contains('@', StringComparison.Ordinal)) continue;

            token.Trim('.', ':', ';').ShouldBe(
                EmailTemplates.ContactAddress,
                $"{language}: the file names an address other than "
                    + $"{EmailTemplates.ContactAddress}: {token}");
        }
    }

    [Fact]
    public void TheOracle_ActuallyReadsBothTranslationFiles()
    {
        // The path helper throws with the path in its message, so a lookup failure is already loud —
        // this fact is not guarding that, and an earlier comment here claimed it was, describing a
        // failure mode the code makes impossible (code-reviewer Major 4). What it DOES guard is the
        // shape the theory above assumes: both files exist, parse, and actually contain an address, so
        // a file emptied or restructured cannot make the token sweep pass by having nothing to sweep.
        foreach (var language in new[] { "sv", "en" })
        {
            var json = File.ReadAllText(ContentLegalPath(language));

            json.ShouldNotBeNullOrWhiteSpace();
            json.ShouldContain("\"contact\"");
            json.ShouldContain('@');
        }
    }

    /// <summary>
    /// Walks up from the test binary until the repo root is found, so the test project's own depth is
    /// not hardcoded. Fails loud and names the path it looked for.
    /// </summary>
    private static string ContentLegalPath(string language)
    {
        var relative = Path.Combine(
            "web", "jobbliggaren-web", "messages", language, "content-legal.json");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {relative} by walking up from {AppContext.BaseDirectory}. "
            + "The contact-address mirror needs the repo checkout to be present.");
    }
}
