namespace Jobbliggaren.Migrate;

/// <summary>
/// Verdict of the schema-ahead gate (#1236). Exactly one of these decides what `schema`
/// mode does after reading <c>__EFMigrationsHistory</c> and its own assembly's migration list.
/// </summary>
public enum SchemaAheadVerdict
{
    /// <summary>The database is not ahead of this assembly — migrate forward as always.</summary>
    Proceed,

    /// <summary>
    /// Pure backwards pin: the history holds migrations this assembly does not contain and
    /// the assembly holds nothing unapplied. Refuse with <see cref="SchemaAheadGate.ExitRefusedSchemaAhead"/>;
    /// the operator may bless exactly this state via the override.
    /// </summary>
    RefuseSchemaAhead,

    /// <summary>
    /// True divergence: unknown history rows AND unapplied assembly migrations at once.
    /// Never overridable — no automatic apply is safe when neither side is a prefix of the other.
    /// </summary>
    RefuseDivergence,

    /// <summary>
    /// The operator supplied the exact unknown-ID set as the override value. Skip
    /// <c>MigrateAsync</c> entirely and exit 0 so <c>service_completed_successfully</c> releases —
    /// this run certifies nothing about schema compatibility; that judgment was the operator's.
    /// </summary>
    OverriddenNoOp,
}

/// <summary>
/// The gate's full decision: verdict plus the two derived sets the caller logs.
/// <c>Pending</c> preserves assembly order (what EF would apply, in order); <c>Unknown</c>
/// preserves applied order (what the history holds that this assembly cannot name).
/// </summary>
public sealed record SchemaAheadDecision(
    SchemaAheadVerdict Verdict,
    IReadOnlyList<string> Pending,
    IReadOnlyList<string> Unknown,
    bool OverridePresentButIdle);

/// <summary>
/// The schema-ahead gate for `schema` mode (#1236): may this assembly run EF migrations
/// against the database whose applied-migration list it has just read?
///
/// <para>
/// <b>Why it exists.</b> An image-tag rollback rolls back CODE only. EF applies pending
/// migrations (assembly ∖ applied) and silently ignores history rows it cannot name, so a
/// backwards-pinned <c>IMAGE_TAG</c> runs an older assembly against a newer schema without a
/// single log line — and this repo holds measured cases where that direction destroys data
/// irreversibly. The gate turns that silence into a refusal before <c>MigrateAsync</c> runs.
/// </para>
///
/// <para>
/// <b>Why the override value is the unknown-ID set and never a boolean.</b> A leftover
/// <c>MIGRATE_ALLOW_SCHEMA_AHEAD=1</c> in the box's <c>.env</c> would bless the NEXT accidental
/// backwards pin months later — recreating exactly the incident class this gate exists for.
/// Requiring the exact refused ID set makes the override self-expiring: a later, different pin
/// has different unknown IDs, so a stale override no longer matches and the gate refuses again.
/// </para>
///
/// <para>
/// <b>One normalizer.</b> The caller hands over the RAW environment value — including the empty
/// string compose renders for an unset <c>${MIGRATE_ALLOW_SCHEMA_AHEAD:-}</c> — and every rule
/// about what counts as an override lives in this type alone.
/// </para>
///
/// <para>
/// Pure by design (#1232 precedent): strings in, decision out, no EF and no I/O, so the unit
/// suite and the Testcontainers substrate test assert the same object production executes.
/// </para>
/// </summary>
public static class SchemaAheadGate
{
    /// <summary>The environment variable `schema` mode reads the override from.</summary>
    public const string OverrideVariableName = "MIGRATE_ALLOW_SCHEMA_AHEAD";

    /// <summary>
    /// Exit code for <see cref="SchemaAheadVerdict.RefuseSchemaAhead"/>. 3 and 4 rather than 2:
    /// the reconcile unit's journal already carries the wrapper's own vocabulary, where
    /// 2 means "could not answer" — and migrate's crash path already exits 1. Compose's
    /// dependency line surfaces this number (`exited (3)`), telling the operator whether the
    /// override is even on the table before reading the container log.
    /// </summary>
    public const int ExitRefusedSchemaAhead = 3;

    /// <summary>Exit code for <see cref="SchemaAheadVerdict.RefuseDivergence"/> — see <see cref="ExitRefusedSchemaAhead"/>.</summary>
    public const int ExitRefusedDivergence = 4;

    /// <summary>
    /// Decides. <paramref name="appliedMigrations"/> is the history read
    /// (<c>GetAppliedMigrationsAsync</c>), <paramref name="assemblyMigrations"/> the assembly list
    /// (<c>GetMigrations</c>), <paramref name="overrideValue"/> the raw environment value.
    /// Comparison is ordinal — migration IDs are exact identifiers, and EF compares them ordinally.
    /// </summary>
    public static SchemaAheadDecision Decide(
        IReadOnlyCollection<string> appliedMigrations,
        IReadOnlyCollection<string> assemblyMigrations,
        string? overrideValue)
    {
        var assemblySet = new HashSet<string>(assemblyMigrations, StringComparer.Ordinal);
        var appliedSet = new HashSet<string>(appliedMigrations, StringComparer.Ordinal);

        var unknown = appliedMigrations.Where(m => !assemblySet.Contains(m)).ToList();
        var pending = assemblyMigrations.Where(m => !appliedSet.Contains(m)).ToList();

        var overrideProvided = !string.IsNullOrWhiteSpace(overrideValue);
        var overrideIds = ParseOverride(overrideValue);

        if (unknown.Count == 0)
        {
            return new SchemaAheadDecision(
                SchemaAheadVerdict.Proceed, pending, unknown,
                OverridePresentButIdle: overrideProvided);
        }

        if (pending.Count > 0)
        {
            return new SchemaAheadDecision(
                SchemaAheadVerdict.RefuseDivergence, pending, unknown,
                OverridePresentButIdle: false);
        }

        var overridden = overrideIds is not null
            && overrideIds.SetEquals(unknown);

        return new SchemaAheadDecision(
            overridden ? SchemaAheadVerdict.OverriddenNoOp : SchemaAheadVerdict.RefuseSchemaAhead,
            pending, unknown,
            OverridePresentButIdle: false);
    }

    private static HashSet<string>? ParseOverride(string? overrideValue)
    {
        if (string.IsNullOrWhiteSpace(overrideValue))
        {
            return null;
        }

        var ids = overrideValue.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ids.Length == 0 ? null : new HashSet<string>(ids, StringComparer.Ordinal);
    }
}
