using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth;
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
            "new-account-code-limit-reached", "reauthentication-code", "address-change-code"];

    private static EmailTemplates.EmailContent RenderVariant(string variant) => variant switch
    {
        "code-and-link" => Render(new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw("042917"), Link)),
        "link-only" => Render(new LoginChallengeEmail.LinkOnly(Link)),
        "registration-closed" => Render(new LoginChallengeEmail.RegistrationClosed()),
        "pending-deletion" => Render(new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19))),
        "new-account-code" => Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw("042917"))),
        "new-account-code-limit-reached" => Render(new LoginChallengeEmail.NewAccountCodeLimitReached()),
        "reauthentication-code" => Render(new LoginChallengeEmail.ReauthenticationCode(LoginCode.FromRaw("042917"))),
        "address-change-code" => Render(AddressChange()),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static LoginChallengeEmail.AddressChangeCode AddressChange() => new(LoginCode.FromRaw("042917"));

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

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

    [Fact]
    public void ReauthenticationCode_carries_the_code_once_no_link_no_art_14_notice_and_the_way_back()
    {
        // #1739 — to the account holder's own address, which the account already holds: no Art. 14 block (the
        // recipient is not class (3)), no link (a link yields a session, never a re-authentication), and the
        // contact address for the case where it was not the holder who asked, since no self-service
        // logout-everywhere exists for a passwordless account.
        var rendered = Render(new LoginChallengeEmail.ReauthenticationCode(LoginCode.FromRaw("042917")));

        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.HtmlBody.ShouldNotContain("token=");
        rendered.PlainTextBody.ShouldContain("ingen länk");

        rendered.Subject.ShouldNotContain("042917");
        rendered.PlainTextBody.Split("042917").Length.ShouldBe(2);
        rendered.HtmlBody.Split("042917").Length.ShouldBe(2);

        var text = Unwrapped(rendered.PlainTextBody);
        text.ShouldContain("ditt konto");
        text.ShouldContain("radera kontot, byta e-postadress eller byta lösenord");
        text.ShouldContain(EmailTemplates.ContactAddress);
        text.ShouldNotContain("artikel 6.1");
        text.ShouldNotContain("Personuppgiftsansvarig");
        text.ShouldNotContain("alla enheter");
    }

    [Fact]
    public void AddressChangeCode_carries_the_code_once_and_no_link()
    {
        var rendered = Render(AddressChange());

        rendered.Subject.ShouldBe("Bekräfta din nya e-postadress");
        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.HtmlBody.ShouldNotContain("token=");
        rendered.PlainTextBody.ShouldContain("Mejlet innehåller ingen länk.");

        rendered.Subject.ShouldNotContain("042917");
        rendered.PlainTextBody.Split("042917").Length.ShouldBe(2);
        rendered.HtmlBody.Split("042917").Length.ShouldBe(2);
    }

    [Fact]
    public void AddressChangeCode_carries_the_whole_art_14_notice_word_for_word_in_both_parts()
    {
        // Recipient class (3), and the notice is not conditioned on anything: at send time nobody knows whether
        // the recipient is the account holder or a stranger. Pinned whole in both parts, because the two are
        // hand-maintained copies and drift is the failure mode; the rights paragraph is pinned up to its address
        // tail, the one place the parts differ by design.
        var rendered = Render(AddressChange());

        const string sourceAndBasis =
            "Adressen har vi fått från en användare som angav den för bytet. Vi berättar inte vem det är, "
            + "eftersom det skulle vara en uppgift om en annan person. Adressen används för att skicka det här "
            + "meddelandet, för att begränsa hur många meddelanden som kan skickas till den, för att kontrollera "
            + "att den som äger adressen godkänner bytet, och som kontots nya adress om bytet slutförs. Grunden är "
            + "berättigat intresse (artikel 6.1 f): en adress ska inte kunna kopplas till ett konto utan att den "
            + "som äger den bekräftar det.";
        const string ignoring =
            "Bortser du från meddelandet ändras ingenting: adressen kopplas aldrig till kontot. Koden slutar gälla "
            + "efter 15 minuter.";
        const string retentionAndProcessor =
            "Vi sparar adressen skyddad i högst 15 minuter medan koden gäller. Använder du koden sparas den i "
            + "högst 10 minuter till, medan bytet slutförs. Avtryck av adressen sparas i högst ett dygn för "
            + "att begränsa hur många meddelanden som kan skickas till den. Slutförs bytet blir adressen kontots "
            + "adress och sparas så länge kontot finns. Slutförs det inte finns adressen inte kvar hos oss efter "
            + "tiderna ovan. E-posten levereras av Scaleway SAS i Frankrike, som behandlar meddelandet för att "
            + "kunna leverera det. I personuppgiftsbiträdesavtalet har leverantören åtagit sig att behandlingen "
            + "sker inom EU.";

        foreach (var part in new[] { Unwrapped(rendered.PlainTextBody), Unwrapped(Tag.Replace(rendered.HtmlBody, " ")) })
        {
            part.ShouldContain(sourceAndBasis);
            part.ShouldContain(ignoring);
            part.ShouldContain(retentionAndProcessor);
            part.ShouldContain("Personuppgiftsansvarig är Klas Olsson, privatperson, som driver Jobbliggaren.");
            part.ShouldContain(
                "Du har rätt att invända mot behandlingen och att begära information, rättelse, radering eller "
                + "begränsning. Skriv till oss:");
            part.ShouldContain(
                "Är du inte nöjd med hur vi behandlar dina uppgifter kan du lämna klagomål till "
                + "Integritetsskyddsmyndigheten, imy.se.");

            // The address sits protected while the code and the grant live, so "vi sparar den inte" is false here.
            part.ShouldNotContain("sparar den inte");
            part.ShouldNotContain("ditt konto");
            part.ShouldNotContain("!");
            part.ShouldNotContain("—");
        }

        rendered.PlainTextBody.ShouldContain(EmailTemplates.ContactAddress);
        rendered.HtmlBody.ShouldContain($"mailto:{EmailTemplates.ContactAddress}");

        LoginChallengePolicy.ChallengeTtl.ShouldBe(TimeSpan.FromMinutes(15), "the copy's 'högst 15 minuter'");
        LoginChallengePolicy.GrantTtl.ShouldBe(TimeSpan.FromMinutes(10), "the copy's 'högst 10 minuter till'");
        ChangeEmailPolicy.PerTargetDailyBudget.Window.ShouldBe(TimeSpan.FromHours(24), "the copy's 'högst ett dygn'");
    }

    [Fact]
    public void AddressChangeCode_is_handed_nothing_that_identifies_the_account()
    {
        // The category answer is only worth as much as the absence of an identity beside it.
        typeof(LoginChallengeEmail.AddressChangeCode).GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe([nameof(LoginChallengeEmail.AddressChangeCode.Code)]);
    }

    [Theory]
    [InlineData("code-and-link")]
    [InlineData("link-only")]
    [InlineData("new-account-code")]
    [InlineData("reauthentication-code")]
    [InlineData("address-change-code")]
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
        [nameof(LoginChallengeEmail.ReauthenticationCode)] = new LoginChallengeEmail.ReauthenticationCode(LoginCode.FromRaw("042917")),
        [nameof(LoginChallengeEmail.AddressChangeCode)] = AddressChange(),
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
