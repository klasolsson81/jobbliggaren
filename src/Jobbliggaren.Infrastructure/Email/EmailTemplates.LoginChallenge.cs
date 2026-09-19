using System.Diagnostics;
using System.Globalization;
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
                Någon har begärt att logga in på ditt konto på Jobbliggaren.

                Din inloggningskod är:
                {code}

                Skriv in koden på sidan där du begärde den. Du kan också logga in genom
                att öppna länken nedan. Koden och länken gäller i {minutes} minuter och kan
                bara användas en gång.
                {link}

                Om det inte var du behöver du inte göra något. Koden och länken slutar
                gälla av sig själva.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Din inloggningskod till Jobbliggaren",
                preheader: $"Koden och länken gäller i {minutes} minuter.",
                body: EmailHtml.P("Någon har begärt att logga in på ditt konto på Jobbliggaren.")
                    + EmailHtml.P("Din inloggningskod är:")
                    + EmailHtml.P(code)
                    + EmailHtml.P(
                        "Skriv in koden på sidan där du begärde den. Du kan också logga in genom att "
                        + $"öppna länken nedan. Koden och länken gäller i {minutes} minuter och kan bara "
                        + "användas en gång.")
                    + EmailHtml.Button(link, "Logga in")
                    + EmailHtml.P(
                        "Om det inte var du behöver du inte göra något. Koden och länken slutar gälla "
                        + "av sig själva.")
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
                Någon har begärt att logga in på ditt konto på Jobbliggaren.

                Öppna länken nedan för att logga in. Länken gäller i {minutes} minuter och
                kan bara användas en gång.
                {link}

                Mejlet innehåller ingen kod, eftersom fler koder har begärts för din adress
                det senaste dygnet än vi skickar. Länken fungerar ändå.

                Om det inte var du behöver du inte göra något. Länken slutar gälla av sig
                själv.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Logga in på Jobbliggaren",
                preheader: $"Länken gäller i {minutes} minuter.",
                body: EmailHtml.P("Någon har begärt att logga in på ditt konto på Jobbliggaren.")
                    + EmailHtml.P(
                        $"Öppna länken nedan för att logga in. Länken gäller i {minutes} minuter och "
                        + "kan bara användas en gång.")
                    + EmailHtml.Button(link, "Logga in")
                    + EmailHtml.P(
                        "Mejlet innehåller ingen kod, eftersom fler koder har begärts för din adress "
                        + "det senaste dygnet än vi skickar. Länken fungerar ändå.")
                    + EmailHtml.P(
                        "Om det inte var du behöver du inte göra något. Länken slutar gälla av sig själv.")
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

        return new EmailContent(
            Subject: "Inloggning på Jobbliggaren",
            PlainTextBody: $"""
                Någon har angett den här adressen för att logga in eller skapa ett konto
                på Jobbliggaren. Det finns inget konto för adressen, och det går inte att
                skapa nya konton ännu. Vi öppnar snart.

                Om det inte var du behöver du inte göra något.

                Adressen har angetts på vår inloggningssida, av dig eller av någon annan.
                Den används bara för att skicka det här meddelandet och för att begränsa
                hur många meddelanden som kan skickas till den. Om du själv angav adressen
                är grunden att vi vidtar en åtgärd som du har begärt (artikel 6.1 b). Om
                någon annan angav den är grunden berättigat intresse (artikel 6.1 f): den
                som äger en adress ska få veta att den har använts hos oss.

                Vi sparar adressen skyddad i högst {minutes} minuter, och ett avtryck av den
                i högst ett dygn för att kunna begränsa antalet meddelanden. Därefter finns
                den inte kvar hos oss. E-posten levereras av Scaleway SAS i Frankrike, som
                behandlar meddelandet för att kunna leverera det. I
                personuppgiftsbiträdesavtalet har leverantören åtagit sig att behandlingen
                sker inom EU.

                Personuppgiftsansvarig är Klas Olsson, privatperson, som driver
                Jobbliggaren.

                Du har rätt att invända mot behandlingen och att begära information,
                rättelse, radering eller begränsning. Skriv till oss:
                {ContactAddress}

                Är du inte nöjd med hur vi behandlar dina uppgifter kan du lämna klagomål
                till Integritetsskyddsmyndigheten, imy.se.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Inloggning på Jobbliggaren",
                preheader: "Det går inte att skapa nya konton ännu.",
                body: EmailHtml.P(
                        "Någon har angett den här adressen för att logga in eller skapa ett konto på "
                        + "Jobbliggaren. Det finns inget konto för adressen, och det går inte att skapa "
                        + "nya konton ännu. Vi öppnar snart.")
                    + EmailHtml.P("Om det inte var du behöver du inte göra något.")
                    + EmailHtml.P(
                        "Adressen har angetts på vår inloggningssida, av dig eller av någon annan. Den "
                        + "används bara för att skicka det här meddelandet och för att begränsa hur många "
                        + "meddelanden som kan skickas till den. Om du själv angav adressen är grunden att "
                        + "vi vidtar en åtgärd som du har begärt (artikel 6.1 b). Om någon annan angav den "
                        + "är grunden berättigat intresse (artikel 6.1 f): den som äger en adress ska få "
                        + "veta att den har använts hos oss.")
                    + EmailHtml.P(
                        $"Vi sparar adressen skyddad i högst {minutes} minuter, och ett avtryck av den i "
                        + "högst ett dygn för att kunna begränsa antalet meddelanden. Därefter finns den "
                        + "inte kvar hos oss. E-posten levereras av Scaleway SAS i Frankrike, som "
                        + "behandlar meddelandet för att kunna leverera det. I "
                        + "personuppgiftsbiträdesavtalet har leverantören åtagit sig att behandlingen "
                        + "sker inom EU.")
                    + EmailHtml.P(
                        "Personuppgiftsansvarig är Klas Olsson, privatperson, som driver Jobbliggaren.")
                    + EmailHtml.LinkParagraph(
                        "Du har rätt att invända mot behandlingen och att begära information, rättelse, "
                        + "radering eller begränsning. Skriv till oss:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.P(
                        "Är du inte nöjd med hur vi behandlar dina uppgifter kan du lämna klagomål till "
                        + "Integritetsskyddsmyndigheten, imy.se.")
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
                Någon har begärt att logga in på ditt konto på Jobbliggaren. Kontot är
                markerat för radering och går inte att logga in på.

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
                body: EmailHtml.P(
                        "Någon har begärt att logga in på ditt konto på Jobbliggaren. Kontot är "
                        + "markerat för radering och går inte att logga in på.")
                    + EmailHtml.LinkParagraph(
                        $"Kontot raderas permanent tidigast {date}. Fram till dess kan du få det "
                        + "återställt genom att skriva till oss:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.P("Om du inte vill ha kvar kontot behöver du inte göra något.")
                    + EmailHtml.SignOff()));
    }

    // The token is Base64Url (only [A-Za-z0-9_-]), so it survives the query round-trip unescaped.
    private static string LoginLink(string baseUrl, LoginLinkToken token) =>
        $"{baseUrl.TrimEnd('/')}{LoginLinkRoute}?token={token.Reveal()}";

    private static int ChallengeMinutes() => (int)LoginChallengePolicy.ChallengeTtl.TotalMinutes;
}
