using Microsoft.Extensions.Configuration;

namespace Jobbliggaren.Infrastructure.Configuration;

/// <summary>
/// #198 / ADR 0050 gate B-1 — reads secrets from FILES instead of environment values.
///
/// <para>
/// <b>Why this exists.</b> Compose injected the field-encryption master key as a container
/// environment value, and Docker persists container environment in its own on-disk state:
/// <c>docker inspect</c> returns the value even after the container has exited (measured on
/// the box 2026-08-05, #1240). That was one of B-1's two plaintext-on-disk surfaces, the other
/// being <c>deploy/.env</c> itself. A path is not a value, so pointing at a file on a RAM-backed
/// mount removes the secret from container state entirely.
/// </para>
///
/// <para>
/// <b>The convention is the one this repo already ships.</b> <c>MigrateEnv.Resolve</c>
/// (<c>src/Jobbliggaren.Migrate/MigrateEnv.cs</c>) implements <c>&lt;NAME&gt;_FILE</c> for the
/// Migrate host, which has no configuration pipeline. This is the same policy realised as an
/// <see cref="IConfigurationSource"/> for the two hosts that do: set
/// <c>FieldEncryption__LocalMasterKeyBase64_FILE=/run/app-secrets/FieldEncryption__LocalMasterKeyBase64</c>
/// and the file's content becomes configuration key
/// <c>FieldEncryption:LocalMasterKeyBase64</c>. One spelling across all three executables.
/// </para>
///
/// <para>
/// <b>The on-disk layout is the contract, not this reader</b> (senior-cto-advisor bind
/// 2026-08-09, Q3). File names are .NET configuration keys with the <c>__</c> delimiter —
/// exactly what <c>AddKeyPerFile</c> expects — so swapping this source for the first-party
/// package later is a one-line change in two composition roots and touches neither the box,
/// the injection script, nor compose.
/// </para>
///
/// <para>
/// <b>Dev is unchanged and no new mandatory key exists</b> (CLAUDE.md §11). With no
/// <c>*_FILE</c> variables set, this provider contributes zero keys and is inert;
/// <c>appsettings.Local.json</c> keeps working exactly as before.
/// </para>
///
/// <para>
/// <b>Failure modes, one owner each.</b> A <c>_FILE</c> variable pointing at an unreadable path
/// throws here, at configuration build, naming the variable and the path and NEVER the content
/// (CLAUDE.md §5 — no secret material in logs or exception messages). An empty or
/// whitespace-only file contributes nothing, so the existing options validators keep sole
/// ownership of "this secret is missing" — one error, one owner.
/// </para>
/// </summary>
public static class EnvFileSecretsConfigurationExtensions
{
    /// <summary>
    /// Adds the <c>&lt;KEY&gt;_FILE</c> configuration source. Register it AFTER every other
    /// source: on the production box the file is the authority, and no stray environment
    /// variable may outrank it.
    /// </summary>
    public static IConfigurationBuilder AddEnvFileSecrets(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(new EnvFileSecretsConfigurationSource());
    }
}

internal sealed class EnvFileSecretsConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new EnvFileSecretsConfigurationProvider();
}

internal sealed class EnvFileSecretsConfigurationProvider : ConfigurationProvider
{
    /// <summary>Suffix that marks an environment variable as a pointer to a secret file.</summary>
    internal const string FileSuffix = "_FILE";

    public override void Load() =>
        Data = Resolve(ReadProcessEnvironment(), File.ReadAllText);

    private static IEnumerable<KeyValuePair<string, string?>> ReadProcessEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            yield return new KeyValuePair<string, string?>(
                (string)entry.Key, entry.Value as string);
        }
    }

    /// <summary>
    /// Pure resolution: takes the environment as data and the file read as a delegate, so it is
    /// unit-testable without process environment or filesystem — the same shape as
    /// <c>MigrateEnv.Resolve</c>.
    ///
    /// <para>
    /// A variable named <c>&lt;KEY&gt;_FILE</c> contributes configuration key <c>&lt;KEY&gt;</c>
    /// with <c>__</c> translated to the configuration delimiter <c>:</c>. Content is trimmed (a
    /// secret mount or a shell redirect commonly leaves a trailing newline, and a trailing byte
    /// changes an HMAC). Empty after trimming contributes nothing.
    /// </para>
    /// </summary>
    internal static Dictionary<string, string?> Resolve(
        IEnumerable<KeyValuePair<string, string?>> environment,
        Func<string, string> readFile)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, path) in environment)
        {
            if (!name.EndsWith(FileSuffix, StringComparison.Ordinal)
                || name.Length == FileSuffix.Length)
            {
                continue;
            }

            // An unset or blank pointer is "not configured", not an error: it lets a compose
            // file carry the variable while an environment that does not use files leaves it
            // empty.
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string content;
            try
            {
                content = readFile(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or ArgumentException)
            {
                // Names the variable and the path — never the content, and never the inner
                // exception's message, which some providers echo file content into.
                throw new InvalidOperationException(
                    $"Secret file for '{name}' could not be read: '{path}' " +
                    $"({ex.GetType().Name}). The value is a file path, not a secret; " +
                    "check that the file exists and is readable by the process user.");
            }

            var value = content.Trim();
            if (value.Length == 0)
            {
                // The options validator owns "missing secret" — do not duplicate that verdict.
                continue;
            }

            // Same normalisation the framework's own environment-variable provider uses, and
            // the one AddKeyPerFile expects: the double underscore is the section delimiter.
            var configurationKey = name[..^FileSuffix.Length]
                .Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);

            data[configurationKey] = value;
        }

        return data;
    }
}
