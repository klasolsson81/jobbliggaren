using System.Globalization;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Svenska email-templates per civic-utility-ton (1177/Digg-stil — sakliga,
/// inga utropstecken, ingen "hej och välkommen!"-ton). Plain text-utgåvor
/// (HTML kan tilläggas senare via SES). Templates är immutable strings —
/// flytta till resource-filer först när vi har 5+ flerspråkiga templates.
/// </summary>
internal static class EmailTemplates
{
    public sealed record EmailContent(string Subject, string PlainTextBody);

    /// <summary>
    /// ADR 0080 Vag 4 PR-4 — bakgrundsmatchnings-notis. Icke-PII (jobbtitlar +
    /// företag + grad-LABELS, aldrig en siffra/procent). En OBLIGATORISK inställnings-/
    /// avregistreringslänk (GDPR Art. 7(3)) byggs ur <paramref name="baseUrl"/>. Ingen
    /// mottagar-adress, inget CV-innehåll i body:n.
    /// </summary>
    public static EmailContent MatchNotification(
        string baseUrl, MatchNotificationEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var matchesLink = $"{trimmed}/matchningar";
        var settingsLink = $"{trimmed}/installningar";

        var items = new StringBuilder();
        foreach (var item in content.Items)
        {
            // Komma-separator (INTE em-dash) — em-dash är förbjudet i svensk UI-copy
            // (feedback_no_em_dash_in_ui_copy; e-postkroppen är användarvänd copy).
            items.AppendLine(CultureInfo.InvariantCulture,
                $"- {item.JobTitle}, {item.CompanyName} ({item.GradeLabel})");
        }
        var remaining = content.TotalCount - content.Items.Count;
        var andMore = remaining > 0
            ? $"\noch {remaining} till.\n"
            : string.Empty;

        var countPhrase = content.TotalCount == 1
            ? "en ny matchning"
            : $"{content.TotalCount} nya matchningar";
        var (subject, intro) = content.Kind == MatchNotificationKind.Direct
            ? ("Ny toppmatchning på Jobbliggaren",
               "Bakgrundsmatchningen har hittat en ny toppmatchning åt dig:")
            : ("Din sammanfattning av nya matchningar",
               $"Bakgrundsmatchningen har hittat {countPhrase} sedan sist:");

        return new EmailContent(
            Subject: subject,
            PlainTextBody: $"""
                {intro}

                {items.ToString().TrimEnd()}
                {andMore}
                Öppna dina matchningar:
                {matchesLink}

                Du får detta för att du har slagit på matchningsnotiser. Du kan
                ändra hur ofta du får dem, eller stänga av dem helt, i dina
                inställningar:
                {settingsLink}

                Vänliga hälsningar,
                Jobbliggaren
                """);
    }

    /// <summary>
    /// ADR 0087 D5 (#311 PR-4) — company-follow notification. Icke-PII (jobbtitlar + PUBLIKA
    /// företagsnamn, INGEN grad-label/siffra och ALDRIG org.nr — ADR 0087 D8). En OBLIGATORISK
    /// inställnings-/avregistreringslänk (GDPR Art. 7(3)) byggs ur <paramref name="baseUrl"/>. Ingen
    /// mottagar-adress, inget CV-innehåll. Civic-ton (1177/Digg): inga utropstecken, ingen em-dash.
    ///
    /// <para>
    /// <b>Filter-disclosure (bevakning F4a, RF-13=13B).</b> Är någon bevakning filtrerad saknas
    /// annonser i mejlet, och det MÅSTE sägas — tyst smalning avvisades på §5-grund. Disclosuren
    /// renderas ur <see cref="FollowedCompanyFilterSummary"/>:s två booleans, en rad per aktiv axel,
    /// efter listan och före CTA:n (den besvarar "varför kan något saknas", medan stycket längre ned
    /// besvarar "varför får jag detta alls" — två frågor, två platser, aldrig sammanslagna).
    /// </para>
    ///
    /// <para>
    /// <b>Copy:n är NAMN-FRI, och det är ett krav — inte en förenkling.</b> Summaryn har
    /// ANY-semantik över ANVÄNDARENS ALLA AKTIVA bevakningsfilter ("minst en aktiv bevakning är
    /// filtrerad", CTO sub-bind A′ — se <c>DigestDispatchJob.BuildFilterSummary</c>), så varje
    /// namnbärande påstående vore FALSKT så snart en andra bevakning filtrerar på en annan ort:
    /// "detta mejl visar bara annonser i Göteborg" ljuger för den som också följer ett bolag
    /// filtrerat på Malmö. Att bära ortsnamn
    /// skulle dessutom skicka preferens-PII till en tredjepartsavsändare (Resend) utan nytta för
    /// användaren, för en detalj som ligger ett klick bort i appen (Art. 5(1)(c)). Utöka därför
    /// INTE kontraktet med ortsnamn.
    /// </para>
    /// </summary>
    public static EmailContent FollowedCompanyNotification(
        string baseUrl, FollowedCompanyNotificationEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var jobsLink = $"{trimmed}/jobb";
        var settingsLink = $"{trimmed}/installningar";
        var companiesLink = $"{trimmed}/foretag";

        var items = new StringBuilder();
        foreach (var item in content.Items)
        {
            // Komma-separator (INTE em-dash) — em-dash är förbjudet i svensk UI-copy
            // (feedback_no_em_dash_in_ui_copy; e-postkroppen är användarvänd copy).
            items.AppendLine(CultureInfo.InvariantCulture,
                $"- {item.JobTitle}, {item.CompanyName}");
        }
        var remaining = content.TotalCount - content.Items.Count;
        var andMore = remaining > 0
            ? $"\noch {remaining} till.\n"
            : string.Empty;

        var countPhrase = content.TotalCount == 1
            ? "en ny annons"
            : $"{content.TotalCount} nya annonser";

        var filterDisclosure = BuildFilterDisclosure(content.FilterSummary, companiesLink);

        return new EmailContent(
            Subject: "Nya annonser från företag du följer",
            PlainTextBody: $"""
                Företag du följer har publicerat {countPhrase} sedan sist:

                {items.ToString().TrimEnd()}
                {andMore}{filterDisclosure}
                Öppna annonserna:
                {jobsLink}

                Du får detta för att du har slagit på notiser för företag du följer.
                Du kan ändra hur ofta du får dem, eller stänga av dem helt, i dina
                inställningar:
                {settingsLink}

                Vänliga hälsningar,
                Jobbliggaren
                """);
    }

    /// <summary>
    /// RF-13=13B — en rad per aktiv filter-axel, eller ingenting alls när inget filter bidrog.
    /// Formuleringen "ett eller flera av företagen du följer" är den enda som är sann under
    /// summaryns ANY-semantik; den avslutas med var filtren ändras, så disclosuren blir handlingsbar
    /// (raden på /foretag visar VILKA bevakningar som är filtrerade).
    /// </summary>
    private static string BuildFilterDisclosure(
        FollowedCompanyFilterSummary? summary, string companiesLink)
    {
        if (summary is null || (!summary.OnlyMatchedActive && !summary.LocationFilterActive))
            return string.Empty;

        var lines = new StringBuilder();
        lines.AppendLine();

        if (summary.OnlyMatchedActive)
        {
            lines.AppendLine(
                "Du får bara matchande annonser för ett eller flera av företagen du följer, "
                + "så annonser du inte matchar visas inte här.");
        }

        if (summary.LocationFilterActive)
        {
            lines.AppendLine(
                "Du har ortsfilter på ett eller flera av företagen du följer, "
                + "så annonser i andra orter visas inte här.");
        }

        lines.AppendLine();
        lines.AppendLine("Du ser och ändrar filtren under Företag:");
        lines.AppendLine(companiesLink);

        return lines.ToString();
    }

    /// <summary>
    /// #679 — change-email ownership confirmation, sent to the NEW address. Builds the confirmation
    /// link from <paramref name="baseUrl"/> + the URL-safe token; the new address is percent-encoded
    /// (plus-addressing) and the token is already Base64Url (no escaping needed). Civic tone
    /// (1177/Digg): no exclamation marks, no em-dash. The address is not changed until the link is
    /// opened; the link is valid for 24h (CTO-bind #1 TokenLifespan).
    /// </summary>
    public static EmailContent EmailChangeConfirmation(
        string baseUrl, EmailChangeConfirmationEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');

        // uid: compact 'N' Guid (url-safe). email: percent-encoded (plus-addressing / '@').
        // token: already Base64Url (only [A-Za-z0-9_-]) so it survives the query round-trip unescaped.
        var confirmLink =
            $"{trimmed}/bekrafta-epost" +
            $"?uid={content.UserId:N}" +
            $"&email={Uri.EscapeDataString(content.NewEmail)}" +
            $"&token={content.UrlSafeToken}";

        return new EmailContent(
            Subject: "Bekräfta din nya e-postadress",
            PlainTextBody: $"""
                Någon har begärt att byta e-postadress på ett Jobbliggaren-konto till
                den här adressen.

                Om det var du, bekräfta att adressen är din genom att öppna länken nedan.
                Länken gäller i 24 timmar.
                {confirmLink}

                Adressen ändras inte förrän du har öppnat länken. Om du inte har begärt
                ändringen kan du bortse från det här meddelandet.

                Vänliga hälsningar,
                Jobbliggaren
                """);
    }

    /// <summary>
    /// #679 (CTO-bind #4) — "your email address was changed" security notice to the OLD address after
    /// a completed change. No token, no link to the new address, does not reveal the new address -
    /// only a factual notice + the help-centre link built from <paramref name="baseUrl"/>. Civic tone:
    /// no exclamation marks, no em-dash.
    /// </summary>
    public static EmailContent EmailChangedNotification(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var helpLink = $"{trimmed}/hjalpcenter";

        return new EmailContent(
            Subject: "Din e-postadress har ändrats",
            PlainTextBody: $"""
                E-postadressen som är kopplad till ditt konto på Jobbliggaren har ändrats
                till en annan adress.

                Om det var du som gjorde ändringen behöver du inte göra något.

                Om du inte känner igen ändringen kan någon annan ha fått tillgång till ditt
                konto. Hör av dig till oss via hjälpcentret så hjälper vi dig:
                {helpLink}

                Vänliga hälsningar,
                Jobbliggaren
                """);
    }

    /// <summary>
    /// #714 — registration email-confirmation, sent to the account's OWN address after signup. Builds
    /// the activation link from <paramref name="baseUrl"/> + the URL-safe token; the token is already
    /// Base64Url (only [A-Za-z0-9_-]) so it survives the query round-trip unescaped, and the compact
    /// 'N' Guid uid is url-safe. No email in the link (the address is unchanged). Civic tone (1177/
    /// Digg): no exclamation marks, no em-dash. The account cannot log in until the link is opened; the
    /// link is valid for 24h (EmailConfirmationTokenProvider TokenLifespan).
    /// </summary>
    public static EmailContent EmailConfirmation(
        string baseUrl, EmailConfirmationEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');

        // uid: compact 'N' Guid (url-safe). token: already Base64Url (only [A-Za-z0-9_-]) so it
        // survives the query round-trip unescaped. No email param (the address is not changing).
        var confirmLink =
            $"{trimmed}/bekrafta-konto" +
            $"?uid={content.UserId:N}" +
            $"&token={content.UrlSafeToken}";

        return new EmailContent(
            Subject: "Bekräfta din e-postadress",
            PlainTextBody: $"""
                Tack för att du har skapat ett konto på Jobbliggaren.

                Bekräfta att adressen är din genom att öppna länken nedan. Du kan
                logga in när adressen är bekräftad. Länken gäller i 24 timmar.
                {confirmLink}

                Om du inte har skapat något konto kan du bortse från det här
                meddelandet.

                Vänliga hälsningar,
                Jobbliggaren
                """);
    }

    /// <summary>
    /// #714 — registration account-exists notice, sent out-of-band to a TAKEN address when someone
    /// attempts to register it (login-nudge, Klas decision). No token, no link that grants access -
    /// only a factual notice + a login link built from <paramref name="baseUrl"/>. Because the HTTP
    /// response is an identical 202 for a taken or a fresh address, this mail is the ONLY differentiator
    /// and it reaches only the real owner's inbox, so it leaks no account existence to a requester who
    /// does not own the address. Civic tone: no exclamation marks, no em-dash. No password-reset link -
    /// that flow does not exist yet (#714 follow-up).
    /// </summary>
    public static EmailContent AccountExistsNotice(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var loginLink = $"{trimmed}/logga-in";
        var helpLink = $"{trimmed}/hjalpcenter";

        return new EmailContent(
            Subject: "Du har redan ett konto hos Jobbliggaren",
            PlainTextBody: $"""
                Någon har försökt skapa ett konto med den här e-postadressen, men
                du har redan ett konto hos Jobbliggaren.

                Om det var du kan du logga in i stället:
                {loginLink}

                Om det inte var du behöver du inte göra något. Ditt konto är
                oförändrat. Har du frågor når du oss via hjälpcentret:
                {helpLink}

                Vänliga hälsningar,
                Jobbliggaren
                """);
    }
}
