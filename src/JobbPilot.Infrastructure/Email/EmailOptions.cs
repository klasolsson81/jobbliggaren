namespace JobbPilot.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Provider-val: "Console" (loggar email till applikationslogg, dev/MVP)
    /// eller "Ses" (AWS SES, prod — kräver SesEmailSender-impl och AWSSDK.
    /// Lyft som TD efter F2-P0d eftersom SES domain-verification + DKIM-setup
    /// är operations-side, ej fas 2-prereq).
    /// </summary>
    public string Provider { get; init; } = "Console";

    public string FromAddress { get; init; } = "no-reply@jobbpilot.se";

    public string FromName { get; init; } = "JobbPilot";

    /// <summary>
    /// Bas-URL för app:en. Används i invitation-länkens
    /// <c>{BaseUrl}/registrera?token={plaintext}</c>.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:3000";
}
