namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Composite idempotency key over the separately-versioned knowledge-bank assets
/// (F4-7, senior-cto-advisor DQ2=C) — mirrors <c>TaxonomySnapshotSeeder.CompositeVersion</c>.
/// <c>params string[]</c> (not a fixed arity) is the OCP move the CTO asked for: the
/// Klas-deferred §7 <c>industry-variants</c> asset version can be appended later with
/// zero edits to existing call sites.
/// </summary>
internal static class KnowledgeBankVersion
{
    /// <summary>e.g. <c>Composite("1.0.0", "1", "1") => "1.0.0+1+1"</c> (rubric +
    /// cliché + verb), and later <c>"+variants-1"</c>.</summary>
    public static string Composite(params string[] componentVersions) =>
        string.Join("+", componentVersions);
}
