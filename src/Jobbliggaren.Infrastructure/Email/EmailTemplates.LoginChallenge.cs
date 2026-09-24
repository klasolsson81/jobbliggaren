using System.Diagnostics;
using System.Globalization;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Email;

internal static partial class EmailTemplates
{
    /// <summary>The login link's route. Its one query parameter is spelled exactly <c>token</c>, the name the
    /// Caddy edge filter deletes from its access log (ADR 0142 "Page form").</summary>
    internal const string LoginLinkRoute = "/logga-in/lank";

    /// <summary>
    /// #1735 — the login-challenge mail, one variant per mail. The plan chooses the variant at issue time
    /// (ADR 0142 D2); this only renders it.
    /// </summary>
    public static EmailContent LoginChallenge(string baseUrl, LoginChallengeEmail content) => content switch
    {
        LoginChallengeEmail.CodeAndLink codeAndLink => LoginCodeAndLink(baseUrl, codeAndLink),
        LoginChallengeEmail.LinkOnly linkOnly => LoginLinkOnly(baseUrl, linkOnly),
        LoginChallengeEmail.RegistrationClosed => LoginRegistrationClosed(),
        LoginChallengeEmail.PendingDeletion pendingDeletion => LoginPendingDeletion(pendingDeletion),
        LoginChallengeEmail.NewAccountCode newAccountCode => LoginNewAccountCode(newAccountCode),
        LoginChallengeEmail.NewAccountCodeLimitReached => LoginNewAccountCodeLimitReached(),
        LoginChallengeEmail.ReauthenticationCode reauthenticationCode => LoginReauthenticationCode(reauthenticationCode),
        LoginChallengeEmail.AddressChangeCode addressChangeCode => LoginAddressChangeCode(addressChangeCode),
        _ => throw new UnreachableException("A LoginChallengeEmail variant has no template."),
    };

    /// <summary>
    /// An existing account within its code budget: the code and a link that logs in without it. The
    /// lifespan is read from <see cref="LoginChallengePolicy.ChallengeTtl"/>, so the promise and the TTL
    /// that enforces it cannot drift apart. The "if it was not you" paragraph is load-bearing: the request
    /// endpoint answers every well-formed address, so anyone can cause this mail.
    /// </summary>
    internal static EmailContent LoginCodeAndLink(string baseUrl, LoginChallengeEmail.CodeAndLink content)
    {
        var code = content.Code.Reveal();
        var link = LoginLink(baseUrl, content.Link);
        var minutes = ChallengeMinutes();

        return new EmailContent(
            Subject: "Din inloggningskod till Jobbliggaren",
            PlainTextBody: $"""
                Någon har begärt att logga in på ditt konto.

                {code}

                Koden och länken gäller i {minutes} minuter.

                Logga in:
                {link}

                Om det inte var du behöver du inte göra något.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Din inloggningskod till Jobbliggaren",
                preheader: $"Koden och länken gäller i {minutes} minuter.",
                body: EmailHtml.P("Någon har begärt att logga in på ditt konto.")
                    + EmailHtml.Code(code)
                    + EmailHtml.P($"Koden och länken gäller i {minutes} minuter.")
                    + EmailHtml.Button(link, "Logga in")
                    + EmailHtml.P("Om det inte var du behöver du inte göra något.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// An existing account whose code budget is spent: a link only (Klas, 2026-09-19, option (A)). It
    /// reaches the owner's own inbox, so saying why there is no code tells no one else anything.
    /// </summary>
    internal static EmailContent LoginLinkOnly(string baseUrl, LoginChallengeEmail.LinkOnly content)
    {
        var link = LoginLink(baseUrl, content.Link);
        var minutes = ChallengeMinutes();

        return new EmailContent(
            Subject: "Logga in på Jobbliggaren",
            PlainTextBody: $"""
                Någon har begärt att logga in på ditt konto.

                Mejlet innehåller ingen kod, eftersom fler koder har begärts för din adress
                det senaste dygnet än vi skickar.

                Länken gäller i {minutes} minuter.

                Logga in:
                {link}

                Om det inte var du behöver du inte göra något.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Logga in på Jobbliggaren",
                preheader: $"Länken gäller i {minutes} minuter.",
                body: EmailHtml.P("Någon har begärt att logga in på ditt konto.")
                    + EmailHtml.P(
                        "Mejlet innehåller ingen kod, eftersom fler koder har begärts för din adress "
                        + "det senaste dygnet än vi skickar.")
                    + EmailHtml.P($"Länken gäller i {minutes} minuter.")
                    + EmailHtml.Button(link, "Logga in")
                    + EmailHtml.P("Om det inte var du behöver du inte göra något.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// An address with no account while registration is not open: no credential, and no promise of later
    /// contact — keeping the address for that would be the waitlist ADR 0083 retired.
    /// <para>
    /// <b>Carries an Art. 14 notice, UNCONDITIONALLY.</b> This is recipient class (3): anyone can type any
    /// address on the login page, so at send time we cannot know whether the recipient is the one who typed
    /// it. The legal basis is therefore stated for both cases. The retention sentence states what our side
    /// holds and for how long — the protected record for the challenge TTL, and a fingerprint for at most
    /// the code budget's window — and says nothing about where the provider keeps the message, which ADR
    /// 0133 records as unmeasured (security-auditor Q20.2, 2026-09-18).
    /// </para>
    /// </summary>
    internal static EmailContent LoginRegistrationClosed()
    {
        var minutes = ChallengeMinutes();
        var window = CodeBudgetWindow().Duration;

        return new EmailContent(
            Subject: "Inloggning på Jobbliggaren",
            PlainTextBody: $"""
                Det finns inget konto för den här adressen, och det går inte att skapa nya
                konton ännu.

                Angav du inte adressen själv behöver du inte göra något.

                {NoCredentialBasisPlain}

                Vi sparar adressen skyddad i högst {minutes} minuter och ett avtryck av den i
                högst {window}. Därefter finns den inte kvar hos oss.
                {ProcessorPlain}

                {ControllerRightsAndComplaintPlain}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Inloggning på Jobbliggaren",
                preheader: "Det går inte att skapa nya konton ännu.",
                body: EmailHtml.P(
                        "Det finns inget konto för den här adressen, och det går inte att skapa nya konton "
                        + "ännu.")
                    + EmailHtml.P("Angav du inte adressen själv behöver du inte göra något.")
                    + EmailHtml.P(NoCredentialBasisHtml)
                    + EmailHtml.P(
                        $"Vi sparar adressen skyddad i högst {minutes} minuter och ett avtryck av den i högst "
                        + $"{window}. Därefter finns den inte kvar hos oss. {ProcessorHtml}")
                    + ControllerRightsAndComplaintHtml()
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// An account in its restore window: login does not restore it (the 30-day clock is untouched, ADR 0142
    /// D3), so the mail names the earliest date the account goes and the one way back, the contact address.
    /// "Tidigast" because the hard-delete job runs daily, so the deletion lands on or after that date.
    /// </summary>
    internal static EmailContent LoginPendingDeletion(LoginChallengeEmail.PendingDeletion content)
    {
        var date = content.PermanentDeletionEarliest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new EmailContent(
            Subject: "Ditt konto är markerat för radering",
            PlainTextBody: $"""
                Någon har begärt att logga in på ditt konto, men det går inte.

                Kontot raderas permanent tidigast {date}. Fram till dess kan du få det
                återställt genom att skriva till oss:
                {ContactAddress}

                Om du inte vill ha kvar kontot behöver du inte göra något.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Ditt konto är markerat för radering",
                preheader: $"Kontot raderas permanent tidigast {date}.",
                body: EmailHtml.P("Någon har begärt att logga in på ditt konto, men det går inte.")
                    + EmailHtml.LinkParagraph(
                        $"Kontot raderas permanent tidigast {date}. Fram till dess kan du få det "
                        + "återställt genom att skriva till oss:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.P("Om du inte vill ha kvar kontot behöver du inte göra något.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// An address with no account while registration is open, within its code budget: the code that leads to
    /// an account, and no link (ADR 0142 D1).
    /// <para>
    /// <b>The retention paragraph is conditional, and its two halves stay together.</b> After the code is
    /// used, a grant holds the address for <see cref="LoginChallengePolicy.GrantTtl"/> more, and a recipient
    /// who goes on to create the account keeps the address on it — so the closed mail's "Därefter finns den
    /// inte kvar hos oss" would be false here. The legal-basis paragraph's "Koden finns bara i det här
    /// meddelandet" holds because this record carries a code and no link. Wording by security-auditor,
    /// pre-code form rounds 2026-09-20 and 2026-09-24 (#1825).
    /// </para>
    /// </summary>
    internal static EmailContent LoginNewAccountCode(LoginChallengeEmail.NewAccountCode content)
    {
        var code = content.Code.Reveal();
        var minutes = ChallengeMinutes();
        var grantMinutes = (int)LoginChallengePolicy.GrantTtl.TotalMinutes;
        var window = CodeBudgetWindow().Duration;

        return new EmailContent(
            Subject: "Din kod för att skapa konto på Jobbliggaren",
            PlainTextBody: $"""
                Det finns inget konto för den här adressen ännu.

                {code}

                Koden gäller i {minutes} minuter.

                Adressen har angetts på vår inloggningssida, av dig eller av någon annan.
                Den används för att skicka det här meddelandet, för att begränsa hur många
                meddelanden som kan skickas till den, och för att skapa kontot om du väljer
                att göra det. Angav du den själv är grunden att vi vidtar åtgärder på din
                begäran innan ett avtal ingås (artikel 6.1 b), annars berättigat intresse
                (artikel 6.1 f): den som äger en adress ska få veta att den har använts hos
                oss. Koden finns bara i det här meddelandet, så ingen annan kan skapa ett
                konto med adressen. Angav du inte adressen själv behöver du inte göra något.

                Vi sparar adressen skyddad i högst {minutes} minuter medan koden gäller, och i
                högst {grantMinutes} minuter till om du använder koden. Ett avtryck av adressen
                sparas i högst {window}. Skapar du kontot sparas adressen så länge kontot finns,
                annars finns den inte kvar hos oss efter tiderna ovan.
                {ProcessorPlain}

                {ControllerRightsAndComplaintPlain}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Din kod för att skapa konto på Jobbliggaren",
                preheader: $"Koden gäller i {minutes} minuter.",
                body: EmailHtml.P("Det finns inget konto för den här adressen ännu.")
                    + EmailHtml.Code(code)
                    + EmailHtml.P($"Koden gäller i {minutes} minuter.")
                    + EmailHtml.P(
                        "Adressen har angetts på vår inloggningssida, av dig eller av någon annan. Den "
                        + "används för att skicka det här meddelandet, för att begränsa hur många "
                        + "meddelanden som kan skickas till den, och för att skapa kontot om du väljer att "
                        + "göra det. Angav du den själv är grunden att vi vidtar åtgärder på din begäran "
                        + "innan ett avtal ingås (artikel 6.1 b), annars berättigat intresse (artikel 6.1 f): "
                        + "den som äger en adress ska få veta att den har använts hos oss. Koden finns bara i "
                        + "det här meddelandet, så ingen annan kan skapa ett konto med adressen. Angav du inte "
                        + "adressen själv behöver du inte göra något.")
                    + EmailHtml.P(
                        $"Vi sparar adressen skyddad i högst {minutes} minuter medan koden gäller, och i högst "
                        + $"{grantMinutes} minuter till om du använder koden. Ett avtryck av adressen sparas i "
                        + $"högst {window}. Skapar du kontot sparas adressen så länge kontot finns, annars "
                        + $"finns den inte kvar hos oss efter tiderna ovan. {ProcessorHtml}")
                    + ControllerRightsAndComplaintHtml()
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// An address with no account while registration is open, past its code budget: no credential. Its processing is the closed
    /// mail's — one message and the budget keys — so it shares that mail's legal-basis paragraph, and
    /// "Därefter finns den inte kvar hos oss" is true here: without a code there is no verify, so no grant,
    /// no claim and no account. It names no account, because the recipient has none.
    /// </summary>
    internal static EmailContent LoginNewAccountCodeLimitReached()
    {
        var minutes = ChallengeMinutes();
        var (window, recentWindow) = CodeBudgetWindow();

        return new EmailContent(
            Subject: "Inloggning på Jobbliggaren",
            PlainTextBody: $"""
                Det finns inget konto för den här adressen.

                Mejlet innehåller ingen kod, eftersom fler koder har begärts för den här
                adressen {recentWindow} än vi skickar. Försök igen om {window}.

                Angav du inte adressen själv behöver du inte göra något.

                {NoCredentialBasisPlain}

                Vi sparar adressen skyddad i högst {minutes} minuter och ett avtryck av den i
                högst {window}. Därefter finns den inte kvar hos oss, eftersom inget konto kan
                skapas med det här meddelandet.
                {ProcessorPlain}

                {ControllerRightsAndComplaintPlain}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Inloggning på Jobbliggaren",
                preheader: "Mejlet innehåller ingen kod.",
                body: EmailHtml.P("Det finns inget konto för den här adressen.")
                    + EmailHtml.P(
                        "Mejlet innehåller ingen kod, eftersom fler koder har begärts för den här "
                        + $"adressen {recentWindow} än vi skickar. Försök igen om {window}.")
                    + EmailHtml.P("Angav du inte adressen själv behöver du inte göra något.")
                    + EmailHtml.P(NoCredentialBasisHtml)
                    + EmailHtml.P(
                        $"Vi sparar adressen skyddad i högst {minutes} minuter och ett avtryck av den i högst "
                        + $"{window}. Därefter finns den inte kvar hos oss, eftersom inget konto kan skapas med "
                        + $"det här meddelandet. {ProcessorHtml}")
                    + ControllerRightsAndComplaintHtml()
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// A re-authentication code for a signed-in user (#1739, ADR 0142 D5): a code and never a link, to the
    /// account's own address. It names what the code is for, because the operation it unlocks is the
    /// account's deletion or the address it is reached at, and a holder of a hijacked session is who
    /// requested it when it was not the owner. No Art. 14 block: the recipient is the account holder, and
    /// the address is the one the account already holds.
    /// </summary>
    internal static EmailContent LoginReauthenticationCode(LoginChallengeEmail.ReauthenticationCode content)
    {
        var code = content.Code.Reveal();
        var minutes = ChallengeMinutes();

        return new EmailContent(
            Subject: "Din bekräftelsekod till Jobbliggaren",
            PlainTextBody: $"""
                Någon som är inloggad på ditt konto vill göra en ändring: radera kontot eller
                byta e-postadress.

                {code}

                Koden gäller i {minutes} minuter.

                Om det inte var du är någon annan inloggad på ditt konto. Ändringen kan inte
                göras utan koden. Skriv till oss så hjälper vi dig:
                {ContactAddress}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Din bekräftelsekod till Jobbliggaren",
                preheader: $"Koden gäller i {minutes} minuter.",
                body: EmailHtml.P(
                        "Någon som är inloggad på ditt konto vill göra en ändring: radera kontot eller byta "
                        + "e-postadress.")
                    + EmailHtml.Code(code)
                    + EmailHtml.P($"Koden gäller i {minutes} minuter.")
                    + EmailHtml.LinkParagraph(
                        "Om det inte var du är någon annan inloggad på ditt konto. Ändringen kan inte göras "
                        + "utan koden. Skriv till oss så hjälper vi dig:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// The code that proves a NEW address before a change-email completes (#1739, ADR 0142 D5), to that address.
    /// A code and never a link. Recipient class (3): the request step refuses an address any account holds, so
    /// the recipient has no account, and whoever typed the address may not own it; the whole Art. 14 notice is
    /// therefore unconditional, and Art. 14(2)(f) is answered with a category, since naming the account holder
    /// would be a disclosure in the other direction. The retention paragraph is <c>security-auditor</c>'s text
    /// (PR 4's pre-code round and panel, 2026-09-22; #1825's form round, 2026-09-24), each duration read from what
    /// enforces it: the challenge's TTL, the grant's TTL, and the longest fingerprint of the address, the
    /// per-address daily cap's.
    /// </summary>
    internal static EmailContent LoginAddressChangeCode(LoginChallengeEmail.AddressChangeCode content)
    {
        var code = content.Code.Reveal();
        var minutes = ChallengeMinutes();
        var grantMinutes = (int)LoginChallengePolicy.GrantTtl.TotalMinutes;
        var targetDaily = Window(ChangeEmailPolicy.PerTargetDailyBudget.Window).Duration;

        return new EmailContent(
            Subject: "Bekräfta din nya e-postadress",
            PlainTextBody: $"""
                Någon har begärt att byta e-postadress på ett Jobbliggaren-konto till
                den här adressen.

                {code}

                Koden gäller i {minutes} minuter.

                Adressen har vi fått från en användare som angav den för bytet. Vi berättar
                inte vem det är, eftersom det skulle vara en uppgift om en annan person.
                Adressen används för att skicka det här meddelandet, för att begränsa hur
                många meddelanden som kan skickas till den, för att kontrollera att den som
                äger adressen godkänner bytet, och som kontots nya adress om bytet slutförs.
                Grunden är berättigat intresse (artikel 6.1 f): en adress ska inte kunna
                kopplas till ett konto utan att den som äger den bekräftar det.

                Bortser du från meddelandet ändras ingenting: adressen kopplas aldrig till
                kontot.

                Vi sparar adressen skyddad i högst {minutes} minuter medan koden gäller, och i
                högst {grantMinutes} minuter till om du använder koden. Avtryck av adressen
                sparas i högst {targetDaily}. Slutförs bytet sparas adressen så länge kontot
                finns, annars finns den inte kvar hos oss efter tiderna ovan.
                {ProcessorPlain}

                {ControllerRightsAndComplaintPlain}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Bekräfta din nya e-postadress",
                preheader: $"Koden gäller i {minutes} minuter.",
                body: EmailHtml.P(
                        "Någon har begärt att byta e-postadress på ett Jobbliggaren-konto till den här adressen.")
                    + EmailHtml.Code(code)
                    + EmailHtml.P($"Koden gäller i {minutes} minuter.")
                    + EmailHtml.P(
                        "Adressen har vi fått från en användare som angav den för bytet. Vi berättar inte vem det "
                        + "är, eftersom det skulle vara en uppgift om en annan person. Adressen används för att "
                        + "skicka det här meddelandet, för att begränsa hur många meddelanden som kan skickas till "
                        + "den, för att kontrollera att den som äger adressen godkänner bytet, och som kontots nya "
                        + "adress om bytet slutförs. Grunden är berättigat intresse (artikel 6.1 f): en adress ska "
                        + "inte kunna kopplas till ett konto utan att den som äger den bekräftar det.")
                    + EmailHtml.P(
                        "Bortser du från meddelandet ändras ingenting: adressen kopplas aldrig till kontot.")
                    + EmailHtml.P(
                        $"Vi sparar adressen skyddad i högst {minutes} minuter medan koden gäller, och i högst "
                        + $"{grantMinutes} minuter till om du använder koden. Avtryck av adressen sparas i högst "
                        + $"{targetDaily}. Slutförs bytet sparas adressen så länge kontot finns, annars finns den "
                        + $"inte kvar hos oss efter tiderna ovan. {ProcessorHtml}")
                    + ControllerRightsAndComplaintHtml()
                    + EmailHtml.SignOff()));
    }

    // The Art. 14 blocks every mail to an address without an account carries (recipient class (3)). One home
    // each, so the mails cannot drift apart. The plain forms keep the hard wraps of the bodies they
    // are interpolated into.

    // The legal basis of a mail that carries no credential: one message and the keys that cap messages.
    private const string NoCredentialBasisPlain = """
        Adressen har angetts på vår inloggningssida, av dig eller av någon annan.
        Den används bara för att skicka det här meddelandet och för att begränsa
        hur många meddelanden som kan skickas till den. Angav du den själv är
        grunden att vi vidtar en åtgärd du har begärt (artikel 6.1 b), annars
        berättigat intresse (artikel 6.1 f): den som äger en adress ska få veta
        att den har använts hos oss.
        """;

    private const string NoCredentialBasisHtml =
        "Adressen har angetts på vår inloggningssida, av dig eller av någon annan. Den "
        + "används bara för att skicka det här meddelandet och för att begränsa hur många "
        + "meddelanden som kan skickas till den. Angav du den själv är grunden att vi vidtar "
        + "en åtgärd du har begärt (artikel 6.1 b), annars berättigat intresse (artikel 6.1 f): "
        + "den som äger en adress ska få veta att den har använts hos oss.";

    // Says what the provider does and where, and nothing about how long it keeps the message, which ADR 0133
    // records as unmeasured.
    private const string ProcessorPlain = """
        E-posten levereras av Scaleway SAS i Frankrike, som i
        personuppgiftsbiträdesavtalet har åtagit sig att behandla den inom EU.
        """;

    private const string ProcessorHtml =
        "E-posten levereras av Scaleway SAS i Frankrike, som i "
        + "personuppgiftsbiträdesavtalet har åtagit sig att behandla den inom EU.";

    private static readonly string ControllerRightsAndComplaintPlain = $"""
        Personuppgiftsansvarig är Klas Olsson, privatperson, som driver
        Jobbliggaren. Du har rätt att invända mot behandlingen och att begära
        information, rättelse, radering eller begränsning. Skriv till oss:
        {ContactAddress}

        Du kan också klaga hos Integritetsskyddsmyndigheten, imy.se.
        """;

    private static Markup ControllerRightsAndComplaintHtml() =>
        EmailHtml.LinkParagraph(
            "Personuppgiftsansvarig är Klas Olsson, privatperson, som driver Jobbliggaren. Du har rätt att "
            + "invända mot behandlingen och att begära information, rättelse, radering eller begränsning. "
            + "Skriv till oss:",
            $"mailto:{ContactAddress}",
            ContactAddress)
        + EmailHtml.P("Du kan också klaga hos Integritetsskyddsmyndigheten, imy.se.");

    // The token is Base64Url (only [A-Za-z0-9_-]), so it survives the query round-trip unescaped.
    private static string LoginLink(string baseUrl, LoginLinkToken token) =>
        $"{baseUrl.TrimEnd('/')}{LoginLinkRoute}?token={token.Reveal()}";

    private static int ChallengeMinutes() => (int)LoginChallengePolicy.ChallengeTtl.TotalMinutes;

    // The longest-lived fingerprint is the code budget's, so its window is the retention the mails state:
    // as a length ("högst ett dygn") and as the period just passed ("det senaste dygnet").
    private static (string Duration, string JustPassed) CodeBudgetWindow() =>
        Window(LoginChallengePolicy.CodeBudget.Window);

    private static (string Duration, string JustPassed) Window(TimeSpan window)
    {
        var hours = (int)window.TotalHours;
        return hours == 24
            ? ("ett dygn", "det senaste dygnet")
            : ($"{hours} timmar", $"de senaste {hours} timmarna");
    }
}
