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
/// <b>Why the whole file rather than the one key.</b> <c>contact.email</c> is only one of five places
/// the address appears per language: it is also inline in the controller-contact paragraph, the
/// Art. 15-22 rights paragraph, the terms and the cookie policy. Asserting the key alone would pass
/// while four prose passages still named the old address, which is the shape of every "fix that landed
/// in one place out of N" this lane has produced.
/// </para>
/// </summary>
public class ContactAddressMatchesPublishedContactTests
{
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

        // And every prose passage: no other address may survive anywhere in the file. `@` appears in
        // no other form here, so a stray address cannot hide behind different wording.
        foreach (var line in json.Split('\n'))
        {
            if (!line.Contains('@', StringComparison.Ordinal)) continue;

            line.ShouldContain(
                EmailTemplates.ContactAddress,
                customMessage: $"{language}: a line names an address other than "
                    + $"{EmailTemplates.ContactAddress}: {line.Trim()}");
        }
    }

    [Fact]
    public void TheOracle_ActuallyReadsBothTranslationFiles()
    {
        // Without this, a path that stopped resolving would surface as "the key is missing", which
        // reads as a real finding and is not one — the failure mode the palette pin's own oracle fact
        // caught the hard way.
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
