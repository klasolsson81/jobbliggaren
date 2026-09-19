using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1735 — the login-challenge mail's variants (ADR 0142 D2). What each one carries is the point: a code
/// and a link for an account within its budget, a link only past it, and no credential at all for closed
/// registration or pending deletion.
/// </summary>
public sealed class EmailTemplatesLoginChallengeTests
{
    private const string BaseUrl = "https://jobbliggaren.se";
    private const string Token = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"; // gitleaks:allow

    private static readonly LoginLinkToken Link = LoginLinkToken.FromRaw(Token);

    private static EmailTemplates.EmailContent Render(LoginChallengeEmail content) =>
        EmailTemplates.LoginChallenge(BaseUrl, content);

    public static TheoryData<string> Variants() =>
        ["code-and-link", "link-only", "registration-closed", "pending-deletion"];

    private static EmailTemplates.EmailContent RenderVariant(string variant) => variant switch
    {
        "code-and-link" => Render(new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw("042917"), Link)),
        "link-only" => Render(new LoginChallengeEmail.LinkOnly(Link)),
        "registration-closed" => Render(new LoginChallengeEmail.RegistrationClosed()),
        "pending-deletion" => Render(new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19))),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    [Fact]
    public void CodeAndLink_carries_the_code_and_the_login_link_with_the_token_as_its_only_parameter()
    {
        var rendered = Render(new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw("042917"), Link));

        rendered.PlainTextBody.ShouldContain("042917");
        rendered.HtmlBody.ShouldContain("042917");

        // The link stands alone on its line, so nothing follows the token. Lines are compared rather than
        // "\n" matched: a raw string literal renders the line endings of its source file.
        rendered.PlainTextBody.Split('\n').Select(line => line.TrimEnd('\r'))
            .ShouldContain($"{BaseUrl}/logga-in/lank?token={Token}");
    }

    [Fact]
    public void LinkOnly_carries_the_link_and_no_code()
    {
        var rendered = Render(new LoginChallengeEmail.LinkOnly(Link));

        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/logga-in/lank?token={Token}");
        rendered.PlainTextBody.ShouldNotContain("inloggningskod är");
        rendered.PlainTextBody.ShouldContain("ingen kod");
    }

    [Theory]
    [InlineData("registration-closed")]
    [InlineData("pending-deletion")]
    public void The_variants_without_a_credential_carry_no_link_and_no_code(string variant)
    {
        var rendered = RenderVariant(variant);

        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.PlainTextBody.ShouldNotContain("inloggningskod är");
    }

    [Fact]
    public void RegistrationClosed_carries_the_art_14_notice_and_promises_no_later_contact()
    {
        var text = Render(new LoginChallengeEmail.RegistrationClosed()).PlainTextBody;

        // Recipient class (3): the source, both legal bases, the controller, the rights and the authority.
        text.ShouldContain("Adressen har angetts på vår inloggningssida");
        text.ShouldContain("artikel 6.1 b");
        text.ShouldContain("artikel 6.1 f");
        text.ShouldContain("Personuppgiftsansvarig är Klas Olsson");
        text.ShouldContain(EmailTemplates.ContactAddress);
        text.ShouldContain("imy.se");

        // Keeping the address to write back later would be the waitlist ADR 0083 retired, and the
        // change-email notice's "vi sparar den inte hos oss" is false here (security-auditor Q20).
        text.ShouldNotContain("hör av oss");
        text.ShouldNotContain("sparar den inte");
    }

    [Fact]
    public void RegistrationClosed_states_the_retention_it_actually_has()
    {
        var text = Render(new LoginChallengeEmail.RegistrationClosed()).PlainTextBody;

        text.ShouldContain($"högst {(int)LoginChallengePolicy.ChallengeTtl.TotalMinutes} minuter");
        text.ShouldContain("högst ett dygn");
        LoginChallengePolicy.CodeBudget.Window.ShouldBe(
            TimeSpan.FromHours(24), "the copy's 'högst ett dygn' is the longest-lived fingerprint, the code budget");
    }

    [Fact]
    public void PendingDeletion_names_the_earliest_date_and_the_way_back()
    {
        var rendered = Render(new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19)));

        rendered.PlainTextBody.ShouldContain("tidigast 2026-10-19");
        rendered.PlainTextBody.ShouldContain(EmailTemplates.ContactAddress);
    }

    [Theory]
    [InlineData("code-and-link")]
    [InlineData("link-only")]
    public void Every_credential_bearing_variant_states_the_lifespan_from_the_policy(string variant)
    {
        RenderVariant(variant).PlainTextBody
            .ShouldContain($"{(int)LoginChallengePolicy.ChallengeTtl.TotalMinutes} minuter");
    }

    [Fact]
    public void Every_variant_of_the_closed_hierarchy_has_a_template()
    {
        // The dispatcher ends in an UnreachableException, so a variant added without a case would compile and
        // fail only in the consumer, as a mail that is never sent.
        var variants = typeof(LoginChallengeEmail).GetNestedTypes()
            .Where(type => type.IsSubclassOf(typeof(LoginChallengeEmail)))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        OneOfEach.Keys.Order(StringComparer.Ordinal).ShouldBe(variants);
        foreach (var content in OneOfEach.Values)
            Should.NotThrow(() => Render(content));
    }

    private static readonly Dictionary<string, LoginChallengeEmail> OneOfEach = new()
    {
        [nameof(LoginChallengeEmail.CodeAndLink)] = new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw("042917"), Link),
        [nameof(LoginChallengeEmail.LinkOnly)] = new LoginChallengeEmail.LinkOnly(Link),
        [nameof(LoginChallengeEmail.RegistrationClosed)] = new LoginChallengeEmail.RegistrationClosed(),
        [nameof(LoginChallengeEmail.PendingDeletion)] = new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19)),
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_variant_keeps_the_civic_tone(string variant)
    {
        var rendered = RenderVariant(variant);

        foreach (var forbidden in new[] { "!", "—" })
        {
            rendered.Subject.ShouldNotContain(forbidden);
            rendered.PlainTextBody.ShouldNotContain(forbidden);
        }
    }

    [Fact]
    public void The_login_link_does_not_double_the_slash_when_the_base_url_ends_with_one()
    {
        var rendered = EmailTemplates.LoginChallenge(BaseUrl + "/", new LoginChallengeEmail.LinkOnly(Link));

        rendered.PlainTextBody.ShouldNotContain("//logga-in");
    }
}
