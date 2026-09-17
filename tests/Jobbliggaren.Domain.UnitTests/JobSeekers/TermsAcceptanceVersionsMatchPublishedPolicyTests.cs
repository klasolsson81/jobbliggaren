using System.Text.Json;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// Pins <see cref="TermsAcceptance.CurrentTermsVersion"/> and
/// <see cref="TermsAcceptance.CurrentPrivacyPolicyVersion"/> against the "Senast uppdaterad" dates the
/// web app publishes in <c>messages/{sv,en}/content-legal.json</c> (ADR 0142 D6, #1736).
///
/// <para>
/// The stamp a seeker is registered with says which version of the terms she accepted and which
/// version of the privacy policy was current then — so the constants and the copy must name the same
/// date, and nothing keeps them equal by construction: Domain reads no files. Same move as
/// <c>ContactAddressMatchesPublishedContactTests</c>: the copy is made CHECKABLE rather than declared,
/// and the published copy is the statement of record — when this fails, fix whichever side is wrong
/// deliberately, never the expectation here.
/// </para>
///
/// <para>
/// <b>Structural, not a token sweep.</b> The precedent asserts that no other address survives anywhere
/// in its file. The analogue here — no other date anywhere — is false: the file carries an
/// <c>updated</c> date for five documents (privacy, terms, cookies, accessibility, recruiter notice),
/// so a file-wide sweep would fail against three unrelated ones. The two keys are read by path, and the
/// assertion is <c>EndsWith</c> because the prefix is localized prose ("Senast uppdaterad: " /
/// "Last updated: ").
/// </para>
/// </summary>
public class TermsAcceptanceVersionsMatchPublishedPolicyTests
{
    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void TermsVersion_EndsThePublishedTermsUpdatedLine(string language)
    {
        Updated(language, "terms").ShouldEndWith(
            TermsAcceptance.CurrentTermsVersion,
            customMessage: $"{language}: terms.updated does not end with TermsAcceptance.CurrentTermsVersion");
    }

    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void PrivacyPolicyVersion_EndsThePublishedPrivacyUpdatedLine(string language)
    {
        Updated(language, "privacy").ShouldEndWith(
            TermsAcceptance.CurrentPrivacyPolicyVersion,
            customMessage: $"{language}: privacy.updated does not end with TermsAcceptance.CurrentPrivacyPolicyVersion");
    }

    [Fact]
    public void TheConstants_AreIsoDates()
    {
        // EndsWith would also pass for a copy and a constant that drifted to the same non-date; the
        // shape D6 names ("the ISO date already in the copy") is pinned on its own.
        foreach (var version in new[]
                 {
                     TermsAcceptance.CurrentTermsVersion,
                     TermsAcceptance.CurrentPrivacyPolicyVersion,
                 })
        {
            DateOnly.TryParseExact(version, "yyyy-MM-dd", out _).ShouldBeTrue(version);
        }
    }

    private static string Updated(string language, string document)
    {
        using var json = JsonDocument.Parse(ContentLegalMessages.ReadAllText(language));
        var updated = json.RootElement.GetProperty(document).GetProperty("updated").GetString();

        updated.ShouldNotBeNullOrWhiteSpace($"{language}: {document}.updated is missing");
        return updated!;
    }
}
