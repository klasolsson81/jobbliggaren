namespace JobbPilot.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Provider-val: "Console" (loggar email till applikationslogg, dev/MVP)
    /// eller "Ses" (AWS SES v2, eu-north-1). Sandbox-mode i Ses kräver att
    /// mottagar-emails är verifierade i AWS-konsolen innan första utskick.
    /// </summary>
    public string Provider { get; init; } = "Console";

    public string FromAddress { get; init; } = "no-reply@jobbpilot.se";

    public string FromName { get; init; } = "JobbPilot";

    /// <summary>
    /// Bas-URL för app:en. Används i invitation-länkens
    /// <c>{BaseUrl}/registrera?token={plaintext}</c>.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:3000";

    /// <summary>
    /// AWS-region för SES-klienten. Default eu-north-1 (Stockholm).
    /// </summary>
    public string AwsRegion { get; init; } = "eu-north-1";
}
