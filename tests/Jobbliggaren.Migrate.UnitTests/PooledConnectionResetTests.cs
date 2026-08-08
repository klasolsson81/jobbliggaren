using Npgsql;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins that no <c>ConnectionStrings__Postgres</c> the repo composes ever disables Npgsql's
/// reset-on-close, on the YAML surface where those values are actually written.
///
/// <para>
/// <b>Why this is a cross-tenant risk and not a tuning knob (#1232, #1229).</b> Postgres searches
/// <c>pg_temp</c> <i>before</i> <c>public</c> when resolving an unqualified relation name, and EF
/// Core emits unqualified table names. Npgsql issues <c>DISCARD ALL</c> when a connection returns
/// to the pool, which drops temp tables; <c>No Reset On Close=true</c> turns that off. A temp
/// table surviving in a <b>physical</b> pooled connection would then be resolved by a
/// <i>different user's</i> later request on that same connection — a cross-tenant read inside the
/// application, with no SQL injection anywhere.
/// </para>
///
/// <para>
/// Two layers have to fall together for that to be exploitable: no injection primitive (measured:
/// none), and <c>DISCARD ALL</c> on pool return. The second is a library default, and a future
/// performance or pgbouncer change could remove it in one line while every suite stays green.
/// #1229 made this worth pinning rather than commenting: it granted <c>TEMPORARY</c> on the
/// database to <see cref="Roles.App"/>, which is what makes a temp table creatable on the
/// application's own connections in the first place.
/// </para>
///
/// <para>
/// <b>Shape-based, not substring-based, and that is load-bearing.</b> Npgsql keyword lookup is
/// case- and whitespace-insensitive, so <c>No Reset On Close=true</c>, <c>NoResetOnClose=true</c>
/// and <c>no reset on close=TRUE</c> are the same setting. A <c>ShouldNotContain("No Reset On
/// Close")</c> guard — the idiom the neighbouring pins use — is defeated by the second spelling
/// while reading as though it covered it. So each value is parsed with
/// <see cref="NpgsqlConnectionStringBuilder"/> and the resulting <b>property</b> is asserted.
/// </para>
///
/// <para>
/// <b>Scope.</b> This covers the YAML that composes the value. The IL surface — a connection
/// string built in C# — is pinned separately in <c>PooledConnectionResetIlTests</c>, because a
/// Mono.Cecil literal scan cannot see a YAML file and a text scan cannot see a compiled literal.
/// <c>infra/terraform/</c> is deliberately NOT covered: ADR 0066 retired the deployed AWS stack
/// and CLAUDE.md §11 preserves that tree as a record of what ran, not as live config.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class PooledConnectionResetTests
{
    private const string Key = "ConnectionStrings__Postgres:";

    /// <summary>
    /// The YAML files that compose a Postgres connection string for a running host. Listed
    /// explicitly rather than globbed: a new file that composes one must be added here
    /// deliberately, and the vacuity guard below fails if a listed file stops carrying one.
    /// </summary>
    private static readonly string[] Sources =
    [
        Path.Combine("deploy", "docker-compose.yml"),
        Path.Combine(".github", "workflows", "e2e.yml"),
    ];

    private static IEnumerable<(string Source, string Value)> Composed()
    {
        foreach (var source in Sources)
        {
            var path = Path.Combine(AppContext.BaseDirectory, source);
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(Key, StringComparison.Ordinal))
                    continue;

                // Everything after the FIRST colon. The value itself contains colons
                // (`${VAR:?}`, `Port=5432`), so a split on all colons would truncate it.
                yield return (source, trimmed.Split(':', 2)[1].Trim().Trim('"'));
            }
        }
    }

    public static TheoryData<string, string> ComposedConnectionStrings()
    {
        var data = new TheoryData<string, string>();
        foreach (var (source, value) in Composed())
        {
            data.Add(source, value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ComposedConnectionStrings))]
    public void ComposedConnectionString_NeverDisablesResetOnClose(string source, string value)
    {
        var builder = new NpgsqlConnectionStringBuilder(value);

        builder.NoResetOnClose.ShouldBeFalse(
            $"{source} composes a ConnectionStrings__Postgres (host={builder.Host}) with " +
            "reset-on-close disabled. " +
            "pg_temp is searched before public and EF emits unqualified table names, so a temp " +
            "table surviving a pooled physical connection is resolvable by another user's later " +
            "request. If this is genuinely wanted, it needs an accepted-risk ADR, not a config edit.");
    }

    [Fact]
    public void Scan_FindsAConnectionString_InEverySourceItClaimsToCover()
    {
        // THE VACUITY GUARD. The theory above passes for free on a file the scan silently
        // stopped matching — a renamed key, a restructured file, a Content include that stopped
        // copying. That is how this kind of pin goes quiet instead of red, so the coverage is
        // asserted rather than assumed.
        var found = Composed().Select(c => c.Source).Distinct().ToList();

        found.ShouldBe(Sources, ignoreOrder: true,
            customMessage:
            $"Expected a line starting with '{Key}' in every listed source. If a file was " +
            "restructured or the key renamed, fix this scan — deleting the entry silently " +
            "removes the guard for that surface.");
    }

    [Theory]
    [MemberData(nameof(ComposedConnectionStrings))]
    public void ComposedConnectionString_ParsesAsARealConnectionString(string source, string value)
    {
        // Second half of the vacuity guard: a value that parsed into an EMPTY builder would
        // satisfy the assertion above without ever having been a connection string. Npgsql
        // silently accepts an empty string, so the property check alone cannot tell "reset is
        // on" from "there was nothing here to read".
        var builder = new NpgsqlConnectionStringBuilder(value);

        // The raw value is deliberately NOT echoed. Nothing leaks today — both sources are
        // tracked in a public repo and carry a placeholder or an ephemeral CI credential — but
        // `Sources` is documented as a list meant to grow, and the day it gains a file that
        // resolves a real credential this message would print it. Host and Database are enough to
        // diagnose a value that failed to parse.
        builder.Host.ShouldNotBeNullOrWhiteSpace($"{source} yielded a value with no Host.");
        builder.Database.ShouldNotBeNullOrWhiteSpace($"{source} yielded a value with no Database (host={builder.Host}).");
    }
}
