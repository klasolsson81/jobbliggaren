namespace Jobbliggaren.Migrate;

/// <summary>
/// Twelve-Factor (faktor III) konfigurations-resolution för Migrate efter
/// AWS-exit (TD-105 / ADR 0050 / ADR 0066). Migrate hämtar inte längre hemligheter
/// via AWS Secrets Manager — alla creds/connection-strings kommer från miljön.
///
/// Stödjer Docker-secret <c>_FILE</c>-konventionen: om <c>&lt;NAME&gt;_FILE</c> är satt
/// läses värdet ur den filen (t.ex. <c>/run/secrets/...</c>), annars ur
/// <c>&lt;NAME&gt;</c>-env-varen. Detta låter Hetzner-Compose-stacken (#196/TD-106)
/// välja env-vars <em>eller</em> Docker-secrets utan att Migrate låser valet.
///
/// <see cref="Resolve"/> är en ren funktion (tar lookup + fil-läsare som delegater)
/// → enhetstestbar utan process-env eller filsystem (CLAUDE.md §2.4). Fil-innehåll
/// trimmas (en secret-mount/<c>echo</c> ger ofta en avslutande radbrytning).
/// </summary>
internal static class MigrateEnv
{
    /// <summary>
    /// Ren resolutions-logik: <c>&lt;name&gt;_FILE</c> har företräde (Docker-secret),
    /// annars <c>&lt;name&gt;</c>. Tomt/whitespace-värde behandlas som osatt (returnerar
    /// <c>null</c>). Filinnehåll trimmas. Tar delegater så den kan testas isolerat.
    /// </summary>
    public static string? Resolve(
        string name,
        Func<string, string?> getEnv,
        Func<string, string> readFile)
    {
        var filePath = getEnv(name + "_FILE");
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var content = readFile(filePath).Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }

        var value = getEnv(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Process-bunden resolution. Saknat värde → fail-loud (CLAUDE.md §3.4).</summary>
    public static string Required(string name) =>
        Resolve(name, Environment.GetEnvironmentVariable, File.ReadAllText)
        ?? throw new InvalidOperationException(
            $"Saknad konfiguration: sätt env-var '{name}' eller '{name}_FILE'.");

    /// <summary>Process-bunden resolution med fallback när värdet saknas.</summary>
    public static string Optional(string name, string fallback) =>
        Resolve(name, Environment.GetEnvironmentVariable, File.ReadAllText) ?? fallback;
}
