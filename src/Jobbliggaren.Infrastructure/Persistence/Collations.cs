namespace Jobbliggaren.Infrastructure.Persistence;

/// <summary>
/// The collations the model declares. Which columns carry which is a RULE, not a list —
/// see ADR 0110: Swedish natural-language text gets <see cref="Swedish"/> (there the collation is
/// CORRECTNESS); machine identifiers do not (there, under any deterministic collation, equality is
/// byte equality, so there is no defect to fix).
/// </summary>
internal static class Collations
{
    /// <summary>
    /// ICU <c>sv-SE</c>, deterministic. Declared in <c>AppDbContext.OnModelCreating</c> and applied
    /// per-column via <c>UseCollation</c>. Rationale, measurements and the scope rule: ADR 0110.
    ///
    /// <para>
    /// <b>Not to be confused with the built-in text-search configuration also called <c>swedish</c></b>,
    /// which this repo already uses (<c>to_tsvector('swedish', ...)</c> for <c>job_ads.search_vector</c>).
    /// They are unrelated objects in different catalogs — <c>pg_collation</c> vs <c>pg_ts_config</c> — and
    /// there is no functional conflict; Postgres resolves each by context. The name is shared only
    /// because both mean "Swedish". Do not "fix" one by renaming the other.
    /// </para>
    /// </summary>
    internal const string Swedish = "swedish";
}
