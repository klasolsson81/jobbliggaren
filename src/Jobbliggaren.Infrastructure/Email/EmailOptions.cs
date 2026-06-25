namespace Jobbliggaren.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Provider-val: "Console" (loggar email till applikationslogg, dev/MVP) eller
    /// "Resend" (transaktionell HTTP-provider, ADR 0080 Vag 4 PR-4 — löser TD-101).
    /// Okänt värde fail-stoppas i DI. Default "Console".
    /// </summary>
    public string Provider { get; init; } = "Console";

    /// <summary>
    /// API-nyckel för HTTP-providern (Resend). ENDAST via gitignored
    /// <c>appsettings.Local.json</c> / managed secret — aldrig i committad config.
    /// Krävs när <see cref="Provider"/> = "Resend"; DI fail-stoppar om den saknas
    /// (ingen tyst no-op som ser ut att skicka).
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    public string FromAddress { get; init; } = "no-reply@jobbliggaren.se";

    public string FromName { get; init; } = "Jobbliggaren";

    /// <summary>
    /// Bas-URL för app:en. Används i invitation-länkens
    /// <c>{BaseUrl}/registrera?token={plaintext}</c>.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:3000";
}
