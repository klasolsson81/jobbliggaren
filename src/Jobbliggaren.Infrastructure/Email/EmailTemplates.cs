using System.Globalization;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Svenska email-templates per civic-utility-ton (1177/Digg-stil — sakliga,
/// inga utropstecken, ingen "hej och välkommen!"-ton). Templates är immutable strings —
/// flytta till resource-filer först när vi har 5+ flerspråkiga templates.
///
/// <para>
/// <b>Two parts of one message (#183, 2026-08-12).</b> Every template renders both halves of a
/// <c>multipart/alternative</c> mail: <c>PlainTextBody</c> remains the fallback, and <c>HtmlBody</c>
/// renders the SAME copy through
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
/// the wordmark set as text in the footer, and the tagline under it. The first
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
internal static partial class EmailTemplates
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
        var settingsLink = $"{trimmed}/mina-sidor";

        // A direct mail's one match is its first line; a digest leads with the count and lists the matches.
        var direct = content.Kind == MatchNotificationKind.Direct;

        var items = new StringBuilder();
        var htmlItems = new List<string>();
        foreach (var item in content.Items)
        {
            // Komma-separator (INTE em-dash) — em-dash är förbjudet i svensk UI-copy
            // (feedback_no_em_dash_in_ui_copy; e-postkroppen är användarvänd copy).
            var line = $"{item.JobTitle}, {item.CompanyName} ({item.GradeLabel})";
            items.AppendLine(direct ? line : "- " + line);
            htmlItems.Add(line);
        }
        var remaining = content.TotalCount - content.Items.Count;
        var andMore = remaining > 0
            ? $"\noch {remaining} till.\n"
            : string.Empty;

        var subject = direct ? "Ny toppmatchning på Jobbliggaren" : "Din sammanfattning av nya matchningar";
        var intro = content.TotalCount == 1
            ? "En ny matchning sedan sist:"
            : $"{content.TotalCount} nya matchningar sedan sist:";
        var lead = direct ? string.Empty : $"{intro}\n\n";

        return new EmailContent(
            Subject: subject,
            PlainTextBody: $"""
                {lead}{items.ToString().TrimEnd()}
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
                // The body's first line is also the inbox preview: the match itself in a direct mail,
                // the count in a digest (Klas-krav "informationen först").
                preheader: direct ? string.Join(" ", htmlItems) : intro,
                body: (direct
                        ? htmlItems.Aggregate(Markup.Empty, (markup, line) => markup + EmailHtml.P(line))
                        : EmailHtml.P(intro) + EmailHtml.List(htmlItems))
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
        var settingsLink = $"{trimmed}/mina-sidor";
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

        var filterDisclosure = BuildFilterDisclosure(content.FilterSummary, companiesLink);
        var intro = content.TotalCount == 1
            ? "En ny annons sedan sist:"
            : $"{content.TotalCount} nya annonser sedan sist:";

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
        const string subject = "Din e-postadress har ändrats";

        return new EmailContent(
            Subject: subject,
            PlainTextBody: $"""
                Om det var du som gjorde ändringen behöver du inte göra något.

                Om du inte känner igen ändringen kan någon annan ha fått tillgång till ditt
                konto. Hör av dig till oss så hjälper vi dig:
                {ContactAddress}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: subject,
                preheader: "Om det var du som gjorde ändringen behöver du inte göra något.",
                body: EmailHtml.P("Om det var du som gjorde ändringen behöver du inte göra något.")
                    + EmailHtml.LinkParagraph(
                        "Om du inte känner igen ändringen kan någon annan ha fått tillgång till "
                        + "ditt konto. Hör av dig till oss så hjälper vi dig:",
                        $"mailto:{ContactAddress}",
                        ContactAddress)
                    + EmailHtml.SignOff()));
    }
}
