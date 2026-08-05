using System.Text.RegularExpressions;

namespace Jobbliggaren.Migrate;

/// <summary>
/// The guard for identifiers that reach SQL by interpolation.
///
/// <para>
/// Postgres cannot parameterise an identifier — a database, schema or role name has to be
/// interpolated into the statement text — so every such name passes through here first. Lives
/// in its own file rather than as a local function in top-level statements because
/// <see cref="PhaseADatabaseGrants"/> interpolates too, and a guard the caller has to remember
/// to invoke is one the next caller will not.
/// </para>
/// </summary>
internal static partial class PostgresIdentifier
{
    /// <summary>
    /// Unquoted Postgres identifiers fold to lower case and cap at 63 bytes, so anything a
    /// legitimate caller passes matches this. Deliberately narrower than what Postgres would
    /// accept quoted: nothing here needs mixed case or punctuation.
    /// </summary>
    [GeneratedRegex(@"^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Valid();

    /// <summary>Throws unless <paramref name="ident"/> is safe to interpolate into SQL.</summary>
    internal static string Validate(string ident) =>
        Valid().IsMatch(ident)
            ? ident
            : throw new InvalidOperationException($"Ogiltigt Postgres-identifier: {ident}");
}
