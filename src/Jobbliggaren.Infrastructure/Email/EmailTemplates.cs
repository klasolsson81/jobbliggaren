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
}
