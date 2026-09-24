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
    private const string SampleCode = "042917";

    private static readonly LoginLinkToken Link = LoginLinkToken.FromRaw(Token);

    private static EmailTemplates.EmailContent Render(LoginChallengeEmail content) =>
        EmailTemplates.LoginChallenge(BaseUrl, content);

    public static TheoryData<string> Variants() =>
        ["code-and-link", "link-only", "registration-closed", "pending-deletion", "new-account-code",
            "new-account-code-limit-reached", "reauthentication-code", "address-change-code"];

    private static EmailTemplates.EmailContent RenderVariant(string variant) => variant switch
    {
        "code-and-link" => Render(new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw(SampleCode), Link)),
        "link-only" => Render(new LoginChallengeEmail.LinkOnly(Link)),
        "registration-closed" => Render(new LoginChallengeEmail.RegistrationClosed()),
        "pending-deletion" => Render(new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19))),
        "new-account-code" => Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw(SampleCode))),
        "new-account-code-limit-reached" => Render(new LoginChallengeEmail.NewAccountCodeLimitReached()),
        "reauthentication-code" => Render(new LoginChallengeEmail.ReauthenticationCode(LoginCode.FromRaw(SampleCode))),
        "address-change-code" => Render(AddressChange()),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static LoginChallengeEmail.AddressChangeCode AddressChange() => new(LoginCode.FromRaw(SampleCode));

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

    [Fact]
    public void CodeAndLink_carries_the_login_link_with_the_token_as_its_only_parameter()
    {
        var rendered = Render(new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw(SampleCode), Link));

        // The link stands alone on its line, so nothing follows the token. Lines are compared rather than
        // "\n" matched: a raw string literal renders the line endings of its source file.
        rendered.PlainTextBody.Split('\n').Select(line => line.TrimEnd('\r'))
            .ShouldContain($"{BaseUrl}/logga-in/lank?token={Token}");
    }

    [Theory]
    [InlineData("code-and-link")]
    [InlineData("link-only")]
    public void A_login_mail_tells_a_recipient_who_did_not_ask_that_nothing_is_needed(string variant)
    {
        var rendered = RenderVariant(variant);

        MailText.PlainParagraphs(rendered.PlainTextBody).ShouldContain("Om det inte var du behöver du inte göra något.");
        MailText.HtmlParagraphs(rendered.HtmlBody).ShouldContain("Om det inte var du behöver du inte göra något.");
    }

    [Fact]
    public void LinkOnly_carries_the_link_and_no_code()
    {
        var rendered = Render(new LoginChallengeEmail.LinkOnly(Link));

        rendered.PlainTextBody.ShouldContain($"{BaseUrl}/logga-in/lank?token={Token}");
        rendered.PlainTextBody.ShouldContain("ingen kod");
        rendered.PlainTextBody.ShouldNotMatch("[0-9]{6}");
        rendered.HtmlBody.ShouldNotMatch("(?<!#)[0-9]{6}");
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
        rendered.PlainTextBody.ShouldNotMatch("[0-9]{6}");
        rendered.HtmlBody.ShouldNotMatch("(?<!#)[0-9]{6}");
    }

    [Fact]
    public void NewAccountCode_carries_no_link()
    {
        var rendered = Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw(SampleCode)));

        // A magic link is for an existing account only (ADR 0142 D1), and "Koden finns bara i det här
        // meddelandet" is true only while that holds.
        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.HtmlBody.ShouldNotContain("token=");
    }

    private static List<string> CodeBearingVariantNames() =>
        [.. OneOfEach
            .Where(entry => entry.Value.GetType().GetProperties().Any(property => property.PropertyType == typeof(LoginCode)))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)];

    public static TheoryData<string> CodeBearingVariants()
    {
        var data = new TheoryData<string>();
        foreach (var name in CodeBearingVariantNames())
            data.Add(name);

        return data;
    }

    [Theory]
    [MemberData(nameof(CodeBearingVariants))]
    public void Every_code_bearing_variant_shows_the_code_once_in_each_part_and_in_html_at_the_code_rung(string variant)
    {
        var rendered = Render(OneOfEach[variant]);

        // Once in each body and the HTML occurrence is the rung's own paragraph, so the preheader, the <title>
        // and the h1, which an inbox and a lock screen preview, cannot carry it (#1737 condition 22).
        rendered.Subject.ShouldNotContain(SampleCode);
        rendered.PlainTextBody.Split(SampleCode).Length.ShouldBe(2);
        rendered.HtmlBody.Split(SampleCode).Length.ShouldBe(2);
        rendered.HtmlBody.ShouldContain(
            EmailHtml.P(MailText.PlainParagraphs(rendered.PlainTextBody)[0]).ToString()
            + EmailHtml.Code(SampleCode).ToString());
    }

    [Theory]
    [MemberData(nameof(CodeBearingVariants))]
    public void Every_code_bearing_variant_opens_its_plain_part_with_a_paragraph_that_is_not_the_code(string variant)
    {
        var rendered = Render(OneOfEach[variant]);

        // The plain part has no preheader, so its first paragraph is what a client building its snippet from
        // text/plain shows first (ADR 0144 D4 row 16, #1825).
        var firstParagraph = rendered.PlainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n")[0];
        firstParagraph.ShouldNotBeNullOrWhiteSpace();
        firstParagraph.ShouldNotContain(SampleCode);
    }

    [Fact]
    public void The_code_bearing_variants_are_exactly_the_variants_whose_mail_carries_the_code()
    {
        // Found by the property, so a variant added later is not left out of the theory above; checked here
        // against what the mails actually render, so the lookup cannot go blind and leave the theory empty.
        var byProperty = CodeBearingVariantNames();
        var byRendering = OneOfEach
            .Where(entry => Render(entry.Value).PlainTextBody.Contains(SampleCode, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal);

        byProperty.ShouldBe(byRendering);
        byProperty.ShouldContain(nameof(LoginChallengeEmail.CodeAndLink));
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
        var text = Unwrapped(Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw(SampleCode))).PlainTextBody);

        text.ShouldContain(SignedBlock["new-account-retention"]);

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

        // True here and false in NewAccountCode: without a code there is no grant and no account.
        text.ShouldContain(SignedBlock["limit-reached-retention"]);
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
        Unwrapped(Render(new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw(SampleCode))).PlainTextBody)
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
    public void ReauthenticationCode_carries_no_link_no_art_14_notice_and_the_way_back()
    {
        // #1739 — to the account holder's own address, which the account already holds: no Art. 14 block (the
        // recipient is not class (3)), no link (a link yields a session, never a re-authentication), and the
        // contact address for the case where it was not the holder who asked, since no self-service
        // logout-everywhere exists for a passwordless account.
        var rendered = Render(new LoginChallengeEmail.ReauthenticationCode(LoginCode.FromRaw(SampleCode)));

        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.HtmlBody.ShouldNotContain("token=");

        var text = Unwrapped(rendered.PlainTextBody);
        text.ShouldContain("ditt konto");
        text.ShouldContain("radera kontot eller byta e-postadress.");
        text.ShouldNotContain("lösenord");
        rendered.HtmlBody.ShouldNotContain("lösenord");
        text.ShouldContain(EmailTemplates.ContactAddress);
        text.ShouldNotContain("artikel 6.1");
        text.ShouldNotContain("Personuppgiftsansvarig");
        text.ShouldNotContain("alla enheter");
    }

    [Fact]
    public void AddressChangeCode_carries_no_link()
    {
        var rendered = Render(AddressChange());

        rendered.Subject.ShouldBe("Bekräfta din nya e-postadress");
        rendered.PlainTextBody.ShouldNotContain("/logga-in/lank");
        rendered.PlainTextBody.ShouldNotContain("token=");
        rendered.HtmlBody.ShouldNotContain("token=");
    }

    // The bound blocks as security-auditor signed them in #1825's form round (DESIGN.md §8 rule 7, ADR 0144 D4).
    // Each is asserted word for word in both parts, because the two are hand-maintained copies.
    private const string SignedProcessor =
        "E-posten levereras av Scaleway SAS i Frankrike, som i personuppgiftsbiträdesavtalet har åtagit sig att "
        + "behandla den inom EU.";

    private const string SignedControllerAndRights =
        "Personuppgiftsansvarig är Klas Olsson, privatperson, som driver Jobbliggaren. Du har rätt att invända mot "
        + "behandlingen och att begära information, rättelse, radering eller begränsning. Skriv till oss: "
        + EmailTemplates.ContactAddress;

    private const string SignedComplaint = "Du kan också klaga hos Integritetsskyddsmyndigheten, imy.se.";

    private static readonly Dictionary<string, string> SignedBlock = new()
    {
        ["no-credential-basis"] =
            "Adressen har angetts på vår inloggningssida, av dig eller av någon annan. Den används bara för att "
            + "skicka det här meddelandet och för att begränsa hur många meddelanden som kan skickas till den. Angav "
            + "du den själv är grunden att vi vidtar en åtgärd du har begärt (artikel 6.1 b), annars berättigat "
            + "intresse (artikel 6.1 f): den som äger en adress ska få veta att den har använts hos oss.",
        ["registration-closed-retention"] =
            "Vi sparar adressen skyddad i högst 15 minuter och ett avtryck av den i högst ett dygn. Därefter finns "
            + "den inte kvar hos oss. " + SignedProcessor,
        ["limit-reached-retention"] =
            "Vi sparar adressen skyddad i högst 15 minuter och ett avtryck av den i högst ett dygn. Därefter finns "
            + "den inte kvar hos oss, eftersom inget konto kan skapas med det här meddelandet. " + SignedProcessor,
        ["new-account-ground"] =
            "Adressen har angetts på vår inloggningssida, av dig eller av någon annan. Den används för att skicka "
            + "det här meddelandet, för att begränsa hur många meddelanden som kan skickas till den, och för att "
            + "skapa kontot om du väljer att göra det. Angav du den själv är grunden att vi vidtar åtgärder på din "
            + "begäran innan ett avtal ingås (artikel 6.1 b), annars berättigat intresse (artikel 6.1 f): den som "
            + "äger en adress ska få veta att den har använts hos oss. Koden finns bara i det här meddelandet, så "
            + "ingen annan kan skapa ett konto med adressen. Angav du inte adressen själv behöver du inte göra något.",
        ["new-account-retention"] =
            "Vi sparar adressen skyddad i högst 15 minuter medan koden gäller, och i högst 10 minuter till om du "
            + "använder koden. Ett avtryck av adressen sparas i högst ett dygn. Skapar du kontot sparas adressen så "
            + "länge kontot finns, annars finns den inte kvar hos oss efter tiderna ovan. " + SignedProcessor,
        ["controller-and-rights"] = SignedControllerAndRights,
        ["complaint"] = SignedComplaint,
        ["reauth-opening"] =
            "Någon som är inloggad på ditt konto vill göra en ändring: radera kontot eller byta e-postadress.",
        ["reauth-detection"] =
            "Om det inte var du är någon annan inloggad på ditt konto. Ändringen kan inte göras utan koden. Skriv "
            + "till oss så hjälper vi dig: " + EmailTemplates.ContactAddress,
        ["address-change-ground"] =
            "Adressen har vi fått från en användare som angav den för bytet. Vi berättar inte vem det är, "
            + "eftersom det skulle vara en uppgift om en annan person. Adressen används för att skicka det här "
            + "meddelandet, för att begränsa hur många meddelanden som kan skickas till den, för att kontrollera "
            + "att den som äger adressen godkänner bytet, och som kontots nya adress om bytet slutförs. Grunden är "
            + "berättigat intresse (artikel 6.1 f): en adress ska inte kunna kopplas till ett konto utan att den "
            + "som äger den bekräftar det.",
        ["address-change-ignoring"] =
            "Bortser du från meddelandet ändras ingenting: adressen kopplas aldrig till kontot.",
        ["address-change-retention"] =
            "Vi sparar adressen skyddad i högst 15 minuter medan koden gäller, och i högst 10 minuter till om du "
            + "använder koden. Avtryck av adressen sparas i högst ett dygn. Slutförs bytet sparas adressen så länge "
            + "kontot finns, annars finns den inte kvar hos oss efter tiderna ovan. " + SignedProcessor,
        ["pending-deletion-restore"] =
            "Kontot raderas permanent tidigast 2026-10-19. Fram till dess kan du få det återställt genom att skriva "
            + "till oss: " + EmailTemplates.ContactAddress,
    };

    private static readonly Dictionary<string, string[]> SignedOrder = new()
    {
        ["registration-closed"] =
            ["no-credential-basis", "registration-closed-retention", "controller-and-rights", "complaint"],
        ["new-account-code-limit-reached"] =
            ["no-credential-basis", "limit-reached-retention", "controller-and-rights", "complaint"],
        ["new-account-code"] = ["new-account-ground", "new-account-retention", "controller-and-rights", "complaint"],
        ["address-change-code"] =
        [
            "address-change-ground", "address-change-ignoring", "address-change-retention", "controller-and-rights",
            "complaint",
        ],
        ["reauthentication-code"] = ["reauth-opening", "reauth-detection"],
        ["pending-deletion"] = ["pending-deletion-restore"],
    };

    public static TheoryData<string> SignedVariants()
    {
        var data = new TheoryData<string>();
        foreach (var variant in SignedOrder.Keys)
            data.Add(variant);

        return data;
    }

    [Theory]
    [MemberData(nameof(SignedVariants))]
    public void Every_signed_block_is_one_whole_paragraph_of_both_parts_in_its_signed_order(string variant)
    {
        var rendered = RenderVariant(variant);

        foreach (var paragraphs in new[]
                 {
                     MailText.PlainParagraphs(rendered.PlainTextBody), MailText.HtmlParagraphs(rendered.HtmlBody),
                 })
        {
            var previous = -1;
            foreach (var block in SignedOrder[variant])
            {
                paragraphs.Count(paragraph => paragraph == SignedBlock[block]).ShouldBe(1, $"{variant}: {block}");

                var position = paragraphs.IndexOf(SignedBlock[block]);
                position.ShouldBeGreaterThan(previous, $"{variant}: {block} is out of its signed order");
                previous = position;
            }
        }
    }

    [Fact]
    public void AddressChangeCode_names_no_account_and_reads_its_durations_from_the_policy()
    {
        var rendered = Render(AddressChange());

        foreach (var part in new[] { Unwrapped(rendered.PlainTextBody), Unwrapped(Tag.Replace(rendered.HtmlBody, " ")) })
        {
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
        [nameof(LoginChallengeEmail.CodeAndLink)] = new LoginChallengeEmail.CodeAndLink(LoginCode.FromRaw(SampleCode), Link),
        [nameof(LoginChallengeEmail.LinkOnly)] = new LoginChallengeEmail.LinkOnly(Link),
        [nameof(LoginChallengeEmail.RegistrationClosed)] = new LoginChallengeEmail.RegistrationClosed(),
        [nameof(LoginChallengeEmail.PendingDeletion)] = new LoginChallengeEmail.PendingDeletion(new DateOnly(2026, 10, 19)),
        [nameof(LoginChallengeEmail.NewAccountCode)] = new LoginChallengeEmail.NewAccountCode(LoginCode.FromRaw(SampleCode)),
        [nameof(LoginChallengeEmail.NewAccountCodeLimitReached)] = new LoginChallengeEmail.NewAccountCodeLimitReached(),
        [nameof(LoginChallengeEmail.ReauthenticationCode)] = new LoginChallengeEmail.ReauthenticationCode(LoginCode.FromRaw(SampleCode)),
        [nameof(LoginChallengeEmail.AddressChangeCode)] = AddressChange(),
    };

    private static string Unwrapped(string body) => MailText.Unwrapped(body);

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
