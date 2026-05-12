using System.Globalization;

namespace JobbPilot.Infrastructure.Email;

/// <summary>
/// Svenska email-templates per civic-utility-ton (1177/Digg-stil — sakliga,
/// inga utropstecken, ingen "hej och välkommen!"-ton). Plain text-utgåvor
/// (HTML kan tilläggas senare via SES). Templates är immutable strings —
/// flytta till resource-filer först när vi har 5+ flerspråkiga templates.
/// </summary>
internal static class EmailTemplates
{
    public sealed record EmailContent(string Subject, string PlainTextBody);

    public static EmailContent InvitationEmail(
        string baseUrl, string plaintextToken, DateTimeOffset expiresAt)
    {
        var link = $"{baseUrl.TrimEnd('/')}/registrera?token={plaintextToken}";
        var expiresLocal = expiresAt
            .ToOffset(TimeSpan.FromHours(2))
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return new EmailContent(
            Subject: "Inbjudan till JobbPilot",
            PlainTextBody: $"""
                Du har bjudits in till JobbPilot.

                Skapa ditt konto via länken:
                {link}

                Länken är giltig till {expiresLocal} (svensk tid).

                Om du inte väntar dig denna inbjudan kan du bortse från meddelandet.

                Vänliga hälsningar,
                JobbPilot
                """);
    }

    public static EmailContent WaitlistConfirmationEmail() =>
        new(
            Subject: "Tack för din anmälan till JobbPilot",
            PlainTextBody: """
                Vi har tagit emot din anmälan till väntelistan.

                JobbPilot släpper in användare i kontrollerade pulser. Du får
                ett nytt mejl med registreringslänk när din plats är godkänd.

                Vi sparar din e-postadress endast för väntelistan. Om du vill
                tas bort, svara på detta mejl.

                Vänliga hälsningar,
                JobbPilot
                """);
}
