namespace Jobbliggaren.Infrastructure.Persistence;

/// <summary>
/// The collations the model declares. Which columns carry which is a RULE, not a list —
/// see ADR 0109: Swedish natural-language text gets <see cref="Swedish"/> (there the collation is
/// CORRECTNESS); machine identifiers do not (there, under any deterministic collation, equality is
/// byte equality, so there is no defect to fix).
/// </summary>
internal static class Collations
{
    /// <summary>
    /// ICU <c>sv-SE</c>, deterministic. Declared in <c>AppDbContext.OnModelCreating</c> and applied
    /// per-column via <c>UseCollation</c>. Rationale, measurements and the scope rule: ADR 0109.
    /// </summary>
    internal const string Swedish = "swedish";
}
