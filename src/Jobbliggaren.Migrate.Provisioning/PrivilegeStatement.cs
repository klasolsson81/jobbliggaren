namespace Jobbliggaren.Migrate;

/// <summary>
/// One provisioning statement plus the operator-facing description the caller logs for it.
///
/// <para>
/// Hoisted out of <see cref="PhaseADatabaseGrants"/> when the schema-level statements were
/// extracted alongside the database-level ones (#1232): both lists are the same kind of thing —
/// SQL the caller executes in order, with a line for the operator log — and one type for both
/// is what lets a caller iterate them uniformly.
/// </para>
/// </summary>
public readonly record struct PrivilegeStatement(string Sql, string Description);
