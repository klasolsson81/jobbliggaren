using System.Globalization;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Svenska email-templates per civic-utility-ton (1177/Digg-stil — sakliga,
/// inga utropstecken, ingen "hej och välkommen!"-ton). Templates är immutable strings —
/// flytta till resource-filer först när vi har 5+ flerspråkiga templates.
///
/// <para>
/// <b>Two parts of one message (#183, 2026-08-12).</b> Every template renders both halves of a
/// <c>multipart/alternative</c> mail: <c>PlainTextBody</c> is unchanged from before this change and
/// remains the fallback, and <c>HtmlBody</c> renders the SAME copy through
/// <see cref="EmailHtml"/>. They live in the same method on purpose — a template whose two parts
/// are edited in separate files drifts, and a divergence here is not cosmetic: the Art. 30 entry's
/// Datakategori is written against the message content, so an HTML part carrying a data field the
/// text part does not is a register change (security-auditor 2026-08-12).
/// </para>
///
/// <para>
/// <b>What the HTML part carries beyond the text part, exhaustively</b> — the list is kept complete
/// because the Art. 30 Datakategori argument rests on it, and an earlier version of it was measured
/// short by two reviewers: the <c>&lt;title&gt;</c>, the preheader, the visible <c>&lt;h1&gt;</c>,
/// the wordmark set as text in the footer, and one footer line saying the service is free. The first
/// three repeat the subject or a sentence already in the body; NONE of the five is a personal data
/// field, which is the test that matters. Raw URLs become labelled links. The sign-off is rendered by
/// <c>EmailHtml.SignOff</c> and keeps BOTH of the text part's lines ("Vänliga hälsningar," /
/// "Jobbliggaren") in one paragraph — an earlier version dropped the second line on the theory that
/// the footer wordmark replaced it, which left a comma-terminated line ending nothing on the far side
/// of a visual divider (design-reviewer Major 2, 2026-08-12).
/// </para>
///
/// <para>
/// <b>Do not put a remote resource in either part.</b> The register's retention entry rests on this
/// code emitting none; see <see cref="EmailHtml"/> for the full ground and the pin.
/// </para>
/// </summary>
internal static class EmailTemplates
{
    public sealed record EmailContent(string Subject, string PlainTextBody, string HtmlBody);

    /// <summary>
    /// The address the security notices tell people to write to. <b>Not <c>/hjalpcenter</c>, which is
    /// where they pointed until 2026-08-12:</b> that page exists and stays, but it is a HUB that links
    /// onward to <c>/kontakt</c>, and these three mails reach someone who may have just lost access to
    /// the account. A hub is one step too many there, and a <c>mailto:</c> works from any client even
    /// when the person cannot sign in (Klas-beslut 2026-08-12).
    /// <para>
    /// "kontakt" rather than "support": a support address promises a desk with response times that a
    /// free one-person service does not have, and this same address answers the privacy policy's
    /// controller-contact duty (Art. 13(1)(b)), where "support@" reads wrong. It is the word the site
    /// already uses — the page is <c>/kontakt</c> and the footer link says Kontakt.
    /// </para>
    /// <para>
    /// <b>The published copy carries the same address, and that is pinned.</b> The web app states it
    /// in <c>messages/{sv,en}/content-legal.json</c>, so the address has two homes across the stack
    /// and no way to keep them equal by construction;
    /// <c>ContactAddressMatchesPublishedContactTests</c> asserts the mirror, the same move the palette
    /// mirror makes for the colour literals.
    /// </para>
    /// </summary>
    internal const string ContactAddress = "kontakt@jobbliggaren.se";

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
        var htmlItems = new List<string>();
        foreach (var item in content.Items)
        {
            // Komma-separator (INTE em-dash) — em-dash är förbjudet i svensk UI-copy
            // (feedback_no_em_dash_in_ui_copy; e-postkroppen är användarvänd copy).
            items.AppendLine(CultureInfo.InvariantCulture,
                $"- {item.JobTitle}, {item.CompanyName} ({item.GradeLabel})");
            htmlItems.Add($"{item.JobTitle}, {item.CompanyName} ({item.GradeLabel})");
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
                """,
            HtmlBody: EmailHtml.Document(
                title: subject,
                // The intro carries the count, so the inbox preview answers "how many" before the
                // mail is opened — Klas-krav "informationen först", applied one level earlier than
                // the body.
                preheader: intro,
                body: EmailHtml.P(intro)
                    + EmailHtml.List(htmlItems)
                    + (remaining > 0 ? EmailHtml.P($"och {remaining} till.") : Markup.Empty)
                    + EmailHtml.Button(matchesLink, "Öppna dina matchningar")
                    + EmailHtml.LinkParagraph(
                        "Du får detta för att du har slagit på matchningsnotiser. Du kan ändra hur "
                        + "ofta du får dem, eller stänga av dem helt:",
                        settingsLink,
                        "Ändra dina inställningar")
                    + EmailHtml.SignOff()));
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
    /// renderas ur <see cref="FollowedCompanyFilterSummary"/>:s två booleans som EN mening som inte
    /// namnger någon axel (Klas-beslut 2026-08-12), efter listan och före CTA:n (den besvarar "varför kan något saknas", medan stycket längre ned
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
    /// skulle dessutom skicka preferens-PII till en tredjepartsavsändare (#183) utan nytta för
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
        var htmlItems = new List<string>();
        foreach (var item in content.Items)
        {
            // Komma-separator (INTE em-dash) — em-dash är förbjudet i svensk UI-copy
            // (feedback_no_em_dash_in_ui_copy; e-postkroppen är användarvänd copy).
            items.AppendLine(CultureInfo.InvariantCulture,
                $"- {item.JobTitle}, {item.CompanyName}");
            htmlItems.Add($"{item.JobTitle}, {item.CompanyName}");
        }
        var remaining = content.TotalCount - content.Items.Count;
        var andMore = remaining > 0
            ? $"\noch {remaining} till.\n"
            : string.Empty;

        var countPhrase = content.TotalCount == 1
            ? "en ny annons"
            : $"{content.TotalCount} nya annonser";

        var filterDisclosure = BuildFilterDisclosure(content.FilterSummary, companiesLink);
        var intro = $"Företag du följer har publicerat {countPhrase} sedan sist:";

        return new EmailContent(
            Subject: "Nya annonser från företag du följer",
            PlainTextBody: $"""
                {intro}

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
                """,
            HtmlBody: EmailHtml.Document(
                title: "Nya annonser från företag du följer",
                preheader: intro,
                body: EmailHtml.P(intro)
                    + EmailHtml.List(htmlItems)
                    + (remaining > 0 ? EmailHtml.P($"och {remaining} till.") : Markup.Empty)
                    // The filter disclosure sits between the list and the CTA in BOTH parts. It
                    // answers "why might something be missing"; the paragraph below answers "why am
                    // I getting this at all". Two questions, two places, never merged (RF-13=13B).
                    + BuildFilterDisclosureHtml(content.FilterSummary, companiesLink)
                    + EmailHtml.Button(jobsLink, "Öppna annonserna")
                    + EmailHtml.LinkParagraph(
                        "Du får detta för att du har slagit på notiser för företag du följer. Du "
                        + "kan ändra hur ofta du får dem, eller stänga av dem helt:",
                        settingsLink,
                        "Ändra dina inställningar")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// The disclosure, in ONE sentence covering BOTH axes (Klas-beslut 2026-08-12).
    /// <para>
    /// It said one line per active axis until then, per the RF-13=13B sub-bind. In the case that
    /// matters — a watch narrowed on both axes — that rendered three paragraphs plus a link around a
    /// single ad, which is the "ingen luft" rule inverted. Collapsing loses which KIND of filter
    /// applied and keeps the thing the disclosure exists for: that ads are missing and where to change
    /// it. <b>The collapse is safe under the summary's ANY-semantics</b> for the same reason the copy
    /// is name-free — "filter" is true whether the narrowing came from the matched-only axis, the
    /// location axis, or both, whereas naming an axis would be false the moment a second watch
    /// narrows on the other one.
    /// </para>
    /// <b>What did NOT change:</b> the disclosure still falls silent exactly when no filter
    /// contributed, still sits between the list and the CTA, and is still rendered in BOTH parts.
    /// Silently narrowing was rejected on §5 grounds and still is.
    /// </summary>
    private const string FilterDisclosureSentence =
        "Några annonser kan saknas: du har filter på ett eller flera av företagen du följer.";

    /// <summary>
    /// Where to change it. The HTML part folds this into the sentence as the link text; the plain-text
    /// part needs it on its own line, because a bare URL under a sentence tells the reader nothing
    /// about where it goes. That is the one place the two parts differ in shape rather than wording,
    /// and it is why the compression stopped at three lines of text instead of one.
    /// </summary>
    private const string FilterDisclosureAction = "Ändra filtren under Företag";

    /// <summary>
    /// RF-13=13B — EN mening när minst en filter-axel är aktiv, eller ingenting alls när inget
    /// filter bidrog (formen ändrad från en rad per axel, Klas-beslut 2026-08-12).
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
        lines.AppendLine(FilterDisclosureSentence);
        lines.AppendLine($"{FilterDisclosureAction}:");
        lines.AppendLine(companiesLink);

        return lines.ToString();
    }

    /// <summary>
    /// The HTML twin of <see cref="BuildFilterDisclosure"/>. Same predicate, same ANY-semantic
    /// wording, and the same empty result when no filter contributed. <b>The SHAPE deliberately
    /// differs in one respect</b> — see <see cref="FilterDisclosureAction"/>: the plain-text part puts
    /// the action on its own line above a bare URL, because it cannot fold a label into one, while
    /// this part folds it in as the link text. Wording identical, form not — the two must fall silent together, because a disclosure that appears in only
    /// one part of a <c>multipart/alternative</c> message is a disclosure the recipient may never
    /// see. The copy is NAME-FREE for the reason spelled out on
    /// <see cref="FollowedCompanyNotification"/>: the summary is an ANY over all the user's active
    /// watches, so any name-bearing sentence would be false the moment a second watch filters on
    /// another location, and it would also send preference PII to a third-party sender.
    /// </summary>
    private static Markup BuildFilterDisclosureHtml(
        FollowedCompanyFilterSummary? summary, string companiesLink)
    {
        if (summary is null || (!summary.OnlyMatchedActive && !summary.LocationFilterActive))
            return Markup.Empty;

        return EmailHtml.LinkParagraph(
            FilterDisclosureSentence, companiesLink, FilterDisclosureAction);
    }

    /// <summary>
    /// #679 — change-email ownership confirmation, sent to the NEW address. Builds the confirmation
    /// link from <paramref name="baseUrl"/> + the URL-safe token; the new address is percent-encoded
    /// (plus-addressing) and the token is already Base64Url (no escaping needed). Civic tone
    /// (1177/Digg): no exclamation marks, no em-dash. The address is not changed until the link is
    /// opened; the link is valid for 24h (CTO-bind #1 TokenLifespan).
    ///
    /// <para>
    /// <b>Carries an Art. 14 notice, and it is UNCONDITIONAL.</b> This goes to recipient class (3) —
    /// an address that by construction sits on no account, since <c>ChangeEmailCommandHandler</c>
    /// verifies <c>IsEmailTakenAsync</c> is false before sending — so a mistyped address delivers a
    /// message to someone who is neither a user nor the source of the data. The notice cannot be
    /// conditioned on the recipient being a stranger, because at send time we cannot know whether it
    /// is the holder or a third party; a branch would only ever be right by accident. Art. 14(2)(f)
    /// is answered with a CATEGORY, since naming the account holder would be a disclosure in the
    /// other direction. Both parts carry it — see <see cref="BuildFilterDisclosureHtml"/> for why.
    /// </para>
    /// <para>
    /// <b>The retention sentence says nothing about where the address IS</b>, and that is the
    /// constraint rather than a phrasing choice (<c>security-auditor</c> Blocker, 2026-08-16): ADR
    /// 0133 accepts Scaleway's own retention as UNMEASURED, so any exhaustive location claim would
    /// assert what the house has recorded it cannot measure. What the copy may say is what our own
    /// side does, which is measured: the address reaches no table and no audit projection
    /// (<c>ChangeEmailCommand</c>'s remarks), and the only derived artefact is the cooldown gate's
    /// SHA-256 fingerprint at a 60 s TTL (<c>RedisCooldownGate</c>), which is not a copy of it.
    /// Residency is stated at the rank the contract gives it — DPA Art. 11 undertakes EU level, not
    /// region level. <c>release-checklist.md</c> §2.5 point 1 precondition 6 owns the reasoning.
    /// </para>
    /// </summary>
    public static EmailContent EmailChangeConfirmation(
        string baseUrl, EmailChangeConfirmationEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');

        // uid: dashed 'D' Guid — LOAD-BEARING. /confirm-email-change binds ConfirmEmailChangeRequest.Uid
        // as a Guid via System.Text.Json, whose Guid converter accepts ONLY the dashed 'D' form; a compact
        // 'N' uid fails to bind and 400s every confirm (#981, same root cause as the registration link).
        // Do NOT shorten this to ':N'. email: percent-encoded (plus-addressing / '@'). token: already
        // Base64Url (only [A-Za-z0-9_-]) so it survives the query round-trip unescaped.
        var confirmLink =
            $"{trimmed}/bekrafta-epost" +
            $"?uid={content.UserId:D}" +
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

                Adressen har vi fått från en användare som angav den för bytet. Vi berättar
                inte vem det är, eftersom det skulle vara en uppgift om en annan person.
                Adressen används bara för att skicka det här meddelandet och för att
                kontrollera att den som äger adressen godkänner bytet. Grunden är berättigat
                intresse (artikel 6.1 f): en adress ska inte kunna kopplas till ett konto
                utan att den som äger den bekräftar det.

                Bortser du från meddelandet ändras ingenting: adressen kopplas aldrig till
                kontot och vi sparar den inte hos oss. Länken slutar gälla efter 24 timmar.
                E-posten levereras av Scaleway SAS i Frankrike, som behandlar meddelandet
                för att kunna leverera det. I personuppgiftsbiträdesavtalet har
                leverantören åtagit sig att behandlingen sker inom EU.

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
                title: "Bekräfta din nya e-postadress",
                preheader: "Adressen ändras inte förrän du har öppnat länken. Länken gäller i 24 timmar.",
                body: EmailHtml.P(
                        "Någon har begärt att byta e-postadress på ett Jobbliggaren-konto till "
                        + "den här adressen.")
                    + EmailHtml.P(
                        "Om det var du, bekräfta att adressen är din genom att öppna länken nedan. "
                        + "Länken gäller i 24 timmar.")
                    + EmailHtml.Button(confirmLink, "Bekräfta din nya adress")
                    + EmailHtml.P(
                        "Adressen ändras inte förrän du har öppnat länken. Om du inte har begärt "
                        + "ändringen kan du bortse från det här meddelandet.")
                    + EmailHtml.P(
                        "Adressen har vi fått från en användare som angav den för bytet. Vi "
                        + "berättar inte vem det är, eftersom det skulle vara en uppgift om en "
                        + "annan person. Adressen används bara för att skicka det här meddelandet "
                        + "och för att kontrollera att den som äger adressen godkänner bytet. "
                        + "Grunden är berättigat intresse (artikel 6.1 f): en adress ska inte "
                        + "kunna kopplas till ett konto utan att den som äger den bekräftar det.")
                    + EmailHtml.P(
                        "Bortser du från meddelandet ändras ingenting: adressen kopplas aldrig "
                        + "till kontot och vi sparar den inte hos oss. Länken slutar gälla efter "
                        + "24 timmar. E-posten levereras av Scaleway SAS i Frankrike, som "
                        + "behandlar meddelandet för att kunna leverera det. I "
                        + "personuppgiftsbiträdesavtalet har leverantören åtagit sig att "
                        + "behandlingen sker inom EU.")
                    + EmailHtml.P(
                        "Personuppgiftsansvarig är Klas Olsson, privatperson, som driver "
                        + "Jobbliggaren.")
                    + EmailHtml.LinkParagraph(
                        "Du har rätt att invända mot behandlingen och att begära information, "
                        + "rättelse, radering eller begränsning. Skriv till oss:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.P(
                        "Är du inte nöjd med hur vi behandlar dina uppgifter kan du lämna "
                        + "klagomål till Integritetsskyddsmyndigheten, imy.se.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// #679 (CTO-bind #4) — "your email address was changed" security notice to the OLD address after
    /// a completed change. No token, no link to the new address, does not reveal the new address -
    /// only a factual notice plus the contact address, and NO site link at all (see the port doc on
    /// <c>IEmailSender.SendEmailChangedNotificationAsync</c> for why that is a property and not a
    /// gap). Civic tone: no exclamation marks, no em-dash.
    /// </summary>
    public static EmailContent EmailChangedNotification()
    {
        // No baseUrl parameter, and that is the signature telling the truth rather than a
        // simplification: this template stopped carrying a site link on 2026-08-12 when the help-centre
        // route became the contact address, so a parameter kept "in case" would be dead weight that
        // reads as a link the mail does not have.
        return new EmailContent(
            Subject: "Din e-postadress har ändrats",
            PlainTextBody: $"""
                E-postadressen som är kopplad till ditt konto på Jobbliggaren har ändrats
                till en annan adress.

                Om det var du som gjorde ändringen behöver du inte göra något.

                Om du inte känner igen ändringen kan någon annan ha fått tillgång till ditt
                konto. Hör av dig till oss så hjälper vi dig:
                {ContactAddress}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Din e-postadress har ändrats",
                preheader: "Om det var du som gjorde ändringen behöver du inte göra något.",
                body: EmailHtml.P(
                        "E-postadressen som är kopplad till ditt konto på Jobbliggaren har ändrats "
                        + "till en annan adress.")
                    + EmailHtml.P("Om det var du som gjorde ändringen behöver du inte göra något.")
                    + EmailHtml.LinkParagraph(
                        "Om du inte känner igen ändringen kan någon annan ha fått tillgång till "
                        + "ditt konto. Hör av dig till oss så hjälper vi dig:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// #714 — registration email-confirmation, sent to the account's OWN address after signup. Builds
    /// the activation link from <paramref name="baseUrl"/> + the URL-safe token; the token is already
    /// Base64Url (only [A-Za-z0-9_-]) so it survives the query round-trip unescaped, and the uid is the
    /// dashed 'D' Guid the confirm endpoint binds (STJ's Guid converter accepts only 'D'; #981). No email
    /// in the link (the address is unchanged). Civic tone (1177/Digg): no exclamation marks, no
    /// em-dash. The link is valid for 24h (EmailConfirmationTokenProvider TokenLifespan).
    /// </summary>
    public static EmailContent EmailConfirmation(
        string baseUrl, EmailConfirmationEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');

        // uid: dashed 'D' Guid — LOAD-BEARING. /verify-email binds VerifyEmailRequest.Uid as a Guid via
        // System.Text.Json, whose Guid converter accepts ONLY the dashed 'D' form; a compact 'N' uid fails
        // to bind and 400s every activation (#981). Do NOT shorten this to ':N'. token: already Base64Url
        // (only [A-Za-z0-9_-]) so it survives the query round-trip unescaped. No email param (the address
        // is not changing).
        var confirmLink =
            $"{trimmed}/bekrafta-konto" +
            $"?uid={content.UserId:D}" +
            $"&token={content.UrlSafeToken}";

        return new EmailContent(
            Subject: "Bekräfta din e-postadress",
            PlainTextBody: $"""
                Tack för att du har registrerat dig på Jobbliggaren.

                Bekräfta att adressen är din genom att öppna länken nedan.
                Länken gäller i 24 timmar.
                {confirmLink}

                Om du inte har skapat något konto kan du bortse från det här
                meddelandet.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Bekräfta din e-postadress",
                preheader: "Länken gäller i 24 timmar.",
                body: EmailHtml.P("Tack för att du har registrerat dig på Jobbliggaren.")
                    + EmailHtml.P(
                        "Bekräfta att adressen är din genom att öppna länken nedan. Länken gäller "
                        + "i 24 timmar.")
                    + EmailHtml.Button(confirmLink, "Bekräfta din e-postadress")
                    // Klas-krav: the account mails carry a plain "if this was not you, do nothing"
                    // further down. It is the text template's own closing sentence, in the same
                    // position, so the two parts stay word-for-word.
                    + EmailHtml.P(
                        "Om du inte har skapat något konto kan du bortse från det här meddelandet.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// #714 — registration account-exists notice, sent out-of-band to a TAKEN address when someone
    /// attempts to register it (login-nudge, Klas decision). No token, no link that grants access -
    /// only a factual notice + a login link built from <paramref name="baseUrl"/>. Because the HTTP
    /// response is an identical 202 for a taken or a fresh address, this mail is the ONLY differentiator
    /// and it reaches only the real owner's inbox, so it leaks no account existence to a requester who
    /// does not own the address. Civic tone: no exclamation marks, no em-dash.
    /// <para>
    /// #1171 added the password-reset link, which #714 wanted and could not have because the flow did
    /// not exist. Someone trying to register an address they already own has most often forgotten their
    /// password, so the login nudge alone sends them back to the wall they hit. The link carries no
    /// token and grants nothing — it is the same public URL the login page links to — so it adds no
    /// exposure to a mail that already reaches only the real owner.
    /// </para>
    /// </summary>
    public static EmailContent AccountExistsNotice(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var loginLink = $"{trimmed}/logga-in";
        var forgotLink = $"{trimmed}/glomt-losenord";

        return new EmailContent(
            Subject: "Din e-postadress är redan registrerad hos Jobbliggaren",
            PlainTextBody: $"""
                Någon har försökt skapa ett konto med den här e-postadressen, men
                adressen är redan registrerad hos Jobbliggaren.

                Om det var du, logga in här:
                {loginLink}

                Har du glömt ditt lösenord kan du välja ett nytt här:
                {forgotLink}

                Om det inte var du behöver du inte göra något. Ingenting har
                ändrats.

                Kommer du inte in, eller stämmer något inte, hör av dig:
                {ContactAddress}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Din e-postadress är redan registrerad hos Jobbliggaren",
                preheader: "Om det inte var du behöver du inte göra något. Ingenting har ändrats.",
                body: EmailHtml.P(
                        "Någon har försökt skapa ett konto med den här e-postadressen, men adressen "
                        + "är redan registrerad hos Jobbliggaren.")
                    + EmailHtml.P("Om det var du, logga in här:")
                    + EmailHtml.Button(loginLink, "Logga in")
                    + EmailHtml.LinkParagraph(
                        "Har du glömt ditt lösenord kan du välja ett nytt här:",
                        forgotLink,
                        "Välj ett nytt lösenord")
                    + EmailHtml.P("Om det inte var du behöver du inte göra något. Ingenting har ändrats.")
                    + EmailHtml.LinkParagraph(
                        "Kommer du inte in, eller stämmer något inte, hör av dig:",
                        $"mailto:{ContactAddress}", ContactAddress)
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// #1171 — the password-reset link, sent to the address that requested it. Carries the userId and
    /// an opaque token; the link is the only thing that can change the password. Civic tone
    /// (1177/Digg): no exclamation marks, no em-dash.
    /// <para>
    /// The body states the lifespan by reading <see cref="PasswordResetTokenProviderOptions.LifespanMinutes"/>
    /// rather than spelling a number, so the promise and the provider that enforces it cannot drift
    /// apart. The two 24h templates hardcode theirs; this one deliberately does not.
    /// </para>
    /// <para>
    /// The "if it was not you" paragraph is load-bearing rather than boilerplate: the request endpoint
    /// answers a uniform 202 for every well-formed address, so anyone can cause this mail to be sent to
    /// an address they do not own. It must say plainly that nothing has changed and that ignoring it is
    /// safe.
    /// </para>
    /// </summary>
    public static EmailContent PasswordReset(string baseUrl, PasswordResetEmail content)
    {
        var trimmed = baseUrl.TrimEnd('/');

        // uid: dashed 'D' Guid — LOAD-BEARING, same as the two link templates above. /reset-password
        // binds ResetPasswordRequest.Uid via System.Text.Json, whose Guid converter accepts ONLY the
        // dashed form; a compact 'N' uid 400s every click (#981). token: already Base64Url, so it
        // survives the query round-trip unescaped.
        var resetLink =
            $"{trimmed}/aterstall-losenord" +
            $"?uid={content.UserId:D}" +
            $"&token={content.UrlSafeToken}";

        return new EmailContent(
            Subject: "Återställ ditt lösenord",
            PlainTextBody: $"""
                Någon har begärt ett nytt lösenord för ditt konto på Jobbliggaren.

                Öppna länken nedan för att välja ett nytt lösenord. Länken gäller i
                {PasswordResetTokenProviderOptions.LifespanMinutes} minuter och kan
                bara användas en gång.
                {resetLink}

                Om det inte var du behöver du inte göra något. Ditt lösenord är
                oförändrat så länge du inte öppnar länken, och den slutar gälla av
                sig själv.

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Återställ ditt lösenord",
                preheader:
                    $"Länken gäller i {PasswordResetTokenProviderOptions.LifespanMinutes} minuter "
                    + "och kan bara användas en gång.",
                body: EmailHtml.P(
                        "Någon har begärt ett nytt lösenord för ditt konto på Jobbliggaren.")
                    // Reads the provider's own constant for the same reason the text part does:
                    // the promise and the lifespan that enforces it cannot drift apart.
                    + EmailHtml.P(
                        "Öppna länken nedan för att välja ett nytt lösenord. Länken gäller i "
                        + $"{PasswordResetTokenProviderOptions.LifespanMinutes} minuter och kan "
                        + "bara användas en gång.")
                    + EmailHtml.Button(resetLink, "Välj ett nytt lösenord")
                    + EmailHtml.P(
                        "Om det inte var du behöver du inte göra något. Ditt lösenord är oförändrat "
                        + "så länge du inte öppnar länken, och den slutar gälla av sig själv.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// #1171 — the password-changed security notice, sent after a completed reset. No token and no
    /// link that grants access on its own: a factual notice, the reset route, and the contact address.
    /// <b>Not the twin of <see cref="EmailChangedNotification"/>, and the difference is the security
    /// point:</b> there the address itself was repointed, so a reset link would deliver the reset to
    /// the attacker and the mail therefore carries no site link at all. Here the address is unchanged,
    /// so the reset route genuinely works for the rightful owner and is kept. Civic tone: no
    /// exclamation marks, no em-dash.
    /// <para>
    /// This is the breach-detection control (OWASP ASVS V2.5, NIST SP 800-63B). A reset hands the
    /// account to whoever holds the inbox, so this mail is the one moment a real owner can notice a
    /// reset they did not perform while they could still act on it. That is why it says what to do
    /// rather than merely what happened.
    /// </para>
    /// </summary>
    public static EmailContent PasswordChangedNotice(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var forgotLink = $"{trimmed}/glomt-losenord";

        return new EmailContent(
            Subject: "Ditt lösenord har ändrats",
            PlainTextBody: $"""
                Lösenordet för ditt konto på Jobbliggaren har ändrats via en
                återställningslänk. Du har loggats ut på alla enheter.

                Om det var du behöver du inte göra något. Logga in med ditt nya
                lösenord.

                Om det inte var du kommer den som ändrade lösenordet åt kontot tills
                du väljer ett nytt. Gör det direkt:
                {forgotLink}

                Kontakta oss sedan på:
                {ContactAddress}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: "Ditt lösenord har ändrats",
                preheader: "Du har loggats ut på alla enheter.",
                body: EmailHtml.P(
                        "Lösenordet för ditt konto på Jobbliggaren har ändrats via en "
                        + "återställningslänk. Du har loggats ut på alla enheter.")
                    + EmailHtml.P(
                        "Om det var du behöver du inte göra något. Logga in med ditt nya lösenord.")
                    // The breach-detection control (OWASP ASVS V2.5): this mail is the one moment a
                    // real owner can notice a reset they did not perform while they can still act,
                    // so the action comes before the contact line in both parts.
                    //
                    // INLINE, not a button (Klas-beslut 2026-08-12). The route must stay — remove it
                    // and the mail says "your password changed" with no way to act — but as the
                    // primary call to action it shouts at the ~99% who performed the reset
                    // themselves and are done reading. An inline link keeps the one click for the
                    // case that needs it and lets the common case end on "du behöver inte göra
                    // något". This is the only template whose CTA is deliberately not a button.
                    + EmailHtml.LinkParagraph(
                        "Om det inte var du kommer den som ändrade lösenordet åt kontot tills du "
                        + "väljer ett nytt. Gör det direkt:",
                        forgotLink,
                        "Välj ett nytt lösenord")
                    + EmailHtml.LinkParagraph(
                        "Kontakta oss sedan på:", $"mailto:{ContactAddress}", ContactAddress)
                    + EmailHtml.SignOff()));
    }
}
