using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1735, #1737 — the login-challenge mail's variants (ADR 0142 D2). What each one carries is the point: a
/// code and a link for an account within its budget, a link only past it, a code and no link for a new
/// address while registration is open, and no credential at all for the rest.
/// </summary>
public sealed class EmailTemplatesLoginChallengeTests
{
    private const string BaseUrl = "https://jobbliggaren.se";
    private const string Token = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"; // gitleaks:allow

    private static readonly LoginLinkToken Link = LoginLinkToken.FromRaw(Token);

    private static EmailTemplates.EmailContent Render(LoginChallengeEmail content) =>
        EmailTemplates.LoginChallenge(BaseUrl, content);

    public static TheoryData<string> Variants() =>
        ["code-and-link", "link-only", "registration-closed", "pending-deletion", "new-account-code",
            "new-account-code-limit-reached"];

    private static EmailTemplates.EmailContent RenderVariant(string variant) => variant switch
    {
        "code-and-link" => Render(new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw("042917"), Link)),
        "link-only" => Render(new LoginChallengeEmail.LinkOnly(Link)),
        "registration-closed" => Render(new LoginChallengeEmail.RegistrationClosed()),
        "pending-deletion" => Render(new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19))),
        "new-account-code" => Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("042917"))),
        "new-account-code-limit-reached" => Render(new LoginChallengeEmail.NewAccountCodeLimitReached()),
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
    [InlineData("new-account-code-limit-reached")]
    public void The_variants_without_a_credential_carry_no_link_and_no_code(string variant)
    {
        var rendered = RenderVariant(variant);

        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.PlainTextBody.ShouldNotContain("inloggningskod är");
        rendered.PlainTextBody.ShouldNotMatch("[0-9]{6}");
        rendered.HtmlBody.ShouldNotMatch("[0-9]{6}");
    }

    [Fact]
    public void NewAccountCode_carries_the_code_once_and_no_link()
    {
        var rendered = Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("042917")));

        // A magic link is for an existing account only (ADR 0142 D1), and "Koden finns bara i det här
        // meddelandet" is true only while that holds.
        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.HtmlBody.ShouldNotContain("token=");

        // Once in each body, so never in the preheader, which an inbox and a lock screen preview.
        rendered.Subject.ShouldNotContain("042917");
        rendered.PlainTextBody.Split("042917").Length.ShouldBe(2);
        rendered.HtmlBody.Split("042917").Length.ShouldBe(2);
    }

    [Theory]
    [InlineData("new-account-code")]
    [InlineData("new-account-code-limit-reached")]
    public void A_mail_to_a_new_address_carries_the_whole_art_14_notice_and_names_no_account(string variant)
    {
        var text = RenderVariant(variant).PlainTextBody;

        // The source, both legal bases, the processor, the controller,
        // the rights with the contact address, and the authority.
        text.ShouldContain("Adressen har angetts på vår inloggningssida");
        text.ShouldContain("artikel 6.1 b");
        text.ShouldContain("artikel 6.1 f");
        text.ShouldContain("Scaleway SAS");
        text.ShouldContain("Personuppgiftsansvarig är Klas Olsson");
        text.ShouldContain("rätt att invända");
        text.ShouldContain(EmailTemplates.ContactAddress);
        text.ShouldContain("imy.se");

        text.ShouldNotContain("hör av oss");
        text.ShouldNotContain("sparar den inte");
        text.ShouldNotContain("ditt konto");
    }

    [Fact]
    public void NewAccountCode_states_a_retention_that_depends_on_whether_the_account_is_created()
    {
        var text = Unwrapped(Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("042917"))).PlainTextBody);

        text.ShouldContain("högst 15 minuter medan koden gäller");
        text.ShouldContain("högst 10 minuter till");
        text.ShouldContain("högst ett dygn");
        text.ShouldContain("Skapar du kontot blir adressen kontots adress och sparas så länge kontot finns.");
        text.ShouldContain("Skapar du inget konto finns adressen inte kvar hos oss efter tiderna ovan.");

        // The closed mail's unconditional sentence is false for a recipient who goes on to create the account.
        text.ShouldNotContain("Därefter finns den inte kvar hos oss");

        LoginChallengePolicy.GrantTtl.ShouldBe(TimeSpan.FromMinutes(10), "the copy's 'högst 10 minuter till'");
    }

    [Fact]
    public void NewAccountCodeLimitReached_says_why_there_is_no_code_and_keeps_the_unconditional_retention()
    {
        var text = Unwrapped(Render(new LoginChallengeEmail.NewAccountCodeLimitReached()).PlainTextBody);

        text.ShouldContain("Mejlet innehåller ingen kod");
        text.ShouldContain("Försök igen om ett dygn.");
        text.ShouldContain("högst 15 minuter");
        text.ShouldContain("högst ett dygn");

        // True here and false in NewAccountCode: without a code there is no grant and no account.
        text.ShouldContain(
            "Därefter finns den inte kvar hos oss. Det här meddelandet innehåller ingen kod, så inget konto kan "
            + "skapas med det.");
    }

    [Fact]
    public void The_two_mails_without_a_credential_for_an_unknown_address_share_one_legal_basis_paragraph()
    {
        var closed = Unwrapped(Render(new LoginChallengeEmail.RegistrationClosed()).PlainTextBody);
        var limit = Unwrapped(Render(new LoginChallengeEmail.NewAccountCodeLimitReached()).PlainTextBody);
        const string basis = "Den används bara för att skicka det här meddelandet och för att begränsa hur många "
            + "meddelanden som kan skickas till den.";

        closed.ShouldContain(basis);
        limit.ShouldContain(basis);

        // The code-bearing mail's purpose is wider, so it must not claim "bara".
        Unwrapped(Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("042917"))).PlainTextBody)
            .ShouldNotContain(basis);
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
    [InlineData("new-account-code")]
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
        [nameof(LoginChallengeEmail.NewAccountCode)] = new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("042917")),
        [nameof(LoginChallengeEmail.NewAccountCodeLimitReached)] = new LoginChallengeEmail.NewAccountCodeLimitReached(),
    };

    // The plain body is hard-wrapped; a sentence is asserted on its words, not on where a line breaks.
    private static string Unwrapped(string body) =>
        string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
