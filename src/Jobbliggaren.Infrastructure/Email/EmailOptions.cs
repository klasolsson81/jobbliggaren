namespace Jobbliggaren.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Provider-val: "Console" (loggar email till applikationslogg, dev/MVP) eller
    /// "Ses" (Amazon SES v2 över HTTPS-API:t, ADR 0124). Okänt värde fail-stoppas i DI.
    /// Default "Console" — och den defaulten är oförändrad sedan ADR 0080.
    /// <para>
    /// SES-armens egen konfiguration (region + IAM-nycklar) bor i <see cref="SesEmailOptions"/>
    /// under <c>Email:Ses</c>, inte här: den här klassen konstrueras av VARJE avsändare och av
    /// varje Console/Null-test, så en providers credentials på den hade gjort en SES-detalj
    /// nåbar från armar som inte har någon (ISP). #220 tog bort ett dött
    /// <c>EmailOptions.AwsRegion</c> av exakt det skälet.
    /// </para>
    /// </summary>
    public string Provider { get; init; } = "Console";

    public string FromAddress { get; init; } = "no-reply@jobbliggaren.se";

    public string FromName { get; init; } = "Jobbliggaren";

    /// <summary>
    /// Bas-URL för app:en. Används i bakgrundsmatchnings-notisens länkar
    /// (<c>{BaseUrl}/matchningar</c> + <c>{BaseUrl}/installningar</c>, ADR 0080 Vag 4).
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:3000";
}
