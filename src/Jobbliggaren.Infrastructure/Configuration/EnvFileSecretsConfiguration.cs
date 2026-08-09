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
///
/// <para>
/// <b>Only section-qualified names are read</b>, i.e. the base name must contain <c>__</c>.
/// Without that, this source would read <em>every</em> <c>*_FILE</c> variable in the process
/// environment — <c>SSL_CERT_FILE</c> is the common one in Linux containers — and one pointing
/// at an unreadable path would refuse the host's boot for a file that has nothing to do with
/// this application. All five production keys are section-qualified; the incidental ones are
/// not. This also bounds the blast radius in test hosts, which inherit the ambient environment
/// of whatever machine runs them.
/// </para>
/// </summary>
public static class EnvFileSecretsConfigurationExtensions
{
    /// <summary>
    /// Adds the <c>&lt;KEY&gt;_FILE</c> configuration source. Register it AFTER every other
    /// source: on the production box the file is the authority, and no stray environment
    /// variable may outrank it. That ordering is a security property rather than a preference
    /// — inverted, a <c>FieldEncryption__LocalMasterKeyBase64</c> set as ordinary container
    /// environment would outrank the tmpfs file and recreate the very <c>docker inspect</c>
    /// surface #198 removed. It is pinned by <c>EnvFileSecretsConfigurationTests</c>.
    /// </summary>
    public static IConfigurationBuilder AddEnvFileSecrets(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(new EnvFileSecretsConfigurationSource());
    }
}

/// <summary>
/// Takes its environment reader and file reader as delegates, defaulting to the process
/// environment and <see cref="File.ReadAllText(string)"/>. That is what lets a test drive the
/// whole composition — <c>AddEnvFileSecrets</c>, <c>Build</c>, <c>Load</c> and the last-source-
/// wins precedence — through a real <see cref="ConfigurationBuilder"/> without touching the
/// machine's environment and without standing up a host (the Api suite sits one
/// <c>WebApplicationFactory</c> below EF's process-global ceiling, #1190).
/// </summary>
internal sealed class EnvFileSecretsConfigurationSource(
    Func<IEnumerable<KeyValuePair<string, string?>>>? readEnvironment = null,
    Func<string, string>? readFile = null) : IConfigurationSource
{
    private readonly Func<IEnumerable<KeyValuePair<string, string?>>> _readEnvironment =
        readEnvironment ?? EnvFileSecretsConfigurationProvider.ReadProcessEnvironment;

    private readonly Func<string, string> _readFile = readFile ?? File.ReadAllText;

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new EnvFileSecretsConfigurationProvider(_readEnvironment, _readFile);
}

internal sealed class EnvFileSecretsConfigurationProvider(
    Func<IEnumerable<KeyValuePair<string, string?>>> readEnvironment,
    Func<string, string> readFile) : ConfigurationProvider
{
    /// <summary>Suffix that marks an environment variable as a pointer to a secret file.</summary>
    internal const string FileSuffix = "_FILE";

    /// <summary>
    /// Section delimiter in environment-variable spelling. A base name without it is not one of
    /// ours — see the type's remarks on why that matters.
    /// </summary>
    internal const string SectionSeparator = "__";

    public override void Load() => Data = Resolve(readEnvironment(), readFile);

    internal static IEnumerable<KeyValuePair<string, string?>> ReadProcessEnvironment()
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
    /// A variable named <c>&lt;KEY&gt;_FILE</c> whose base name is section-qualified (contains
    /// <c>__</c>) contributes configuration key <c>&lt;KEY&gt;</c>, with <c>__</c> translated to
    /// the configuration delimiter <c>:</c>. Content is trimmed.
    /// </para>
    ///
    /// <para>
    /// <b>What the trim is and is not for.</b> It is write hygiene plus the empty-file
    /// discriminator: a secret mount or a shell redirect commonly leaves a trailing newline, and
    /// trimming lets a whitespace-only file mean "absent" so the options validators keep sole
    /// ownership of "this secret is missing". It is <em>not</em> load-bearing for correctness of
    /// the values themselves — measured 2026-08-09, every one of the four crypto values is
    /// consumed through <c>Convert.FromBase64String</c>, which ignores whitespace, so a stray
    /// trailing byte would not in fact change a derived HMAC. (An earlier version of this comment
    /// claimed it would; that claim was wrong.) The control is writing exactly the intended bytes
    /// (<c>printf '%s'</c> in the injection script); this is the backstop.
    /// </para>
    ///
    /// <para>
    /// Note also that the first-party <c>AddKeyPerFile</c> does <b>not</b> trim and does not treat
    /// a whitespace-only file as absent (both measured against 10.0.0). This reader is therefore
    /// strictly more permissive, which is the safe direction: swapping to the package later
    /// cannot start accepting something rejected today.
    /// </para>
    /// </summary>
    internal static Dictionary<string, string?> Resolve(
        IEnumerable<KeyValuePair<string, string?>> environment,
        Func<string, string> readFile)
    {
        // OrdinalIgnoreCase matches the ConfigurationProvider base and .NET configuration as a
        // whole; a case-sensitive dictionary would be the deviation. One consequence worth
        // naming: two variables differing only in case would collide, and since
        // Environment.GetEnvironmentVariables() returns a Hashtable with unspecified enumeration
        // order, the winner would be non-deterministic. That is framework parity for ordinary
        // configuration — but these are secrets, so a silent coin toss is refused below.
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, path) in environment)
        {
            if (!name.EndsWith(FileSuffix, StringComparison.Ordinal)
                || name.Length == FileSuffix.Length)
            {
                continue;
            }

            // Section-qualified names only. Without this, SSL_CERT_FILE and every other
            // incidental *_FILE variable in the ambient environment would be read, and one
            // pointing at an unreadable path would refuse the host's boot.
            if (!name.AsSpan(0, name.Length - FileSuffix.Length)
                     .Contains(SectionSeparator, StringComparison.Ordinal))
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
                // exception's message. Measured 2026-08-09: File.ReadAllText's own exceptions
                // carry the path and not the content, so this is defence against a future
                // adapter rather than against the current one.
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
                .Replace(SectionSeparator, ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);

            // Two variables differing only in case resolve to one key. For ordinary
            // configuration last-wins is framework parity; for a secret it would be a silent
            // coin toss decided by Hashtable enumeration order. Refuse instead.
            if (data.ContainsKey(configurationKey))
            {
                throw new InvalidOperationException(
                    $"Two secret-file variables resolve to the same configuration key " +
                    $"'{configurationKey}' (one of them is '{name}'). Configuration keys are " +
                    "case-insensitive, so which file would win is not deterministic. Remove one.");
            }

            data[configurationKey] = value;
        }

        return data;
    }
}
