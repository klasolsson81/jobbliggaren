using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #887 — every backend read (or write) of <c>JobAds</c> in Application + Infrastructure must carry
/// a written lifecycle decision in <see cref="JobAdLifecycleReadRegistry"/>, or the build breaks.
/// </summary>
/// <remarks>
/// The enumerator is a Mono.Cecil IL scan — the same harness <c>ConnectionStringLeakageTests</c>
/// already ships — with the <c>Ldstr</c> predicate replaced by a <c>call</c>/<c>callvirt</c> to a
/// <c>get_JobAds</c> getter. It is fail-CLOSED (every property access emits a getter call; a new
/// read cannot exist without a new instruction) and shape-based (a <c>MethodReference</c> to
/// <c>get_JobAds</c> is invariant under formatting and local renames — NOT a source-string scan,
/// which #864 D5/G4 forbids). See <see cref="JobAdLifecycleReadRegistry"/> for the five things this
/// control deliberately does NOT reach.
/// </remarks>
public class JobAdLifecycleReadRegistryTests
{
    /// <summary>The two declaring types that expose the <c>JobAds</c> DbSet getter.</summary>
    /// <remarks>
    /// BOTH must be matched (CTO R2 fail-open protection): Application handlers read through the
    /// <see cref="IAppDbContext"/> interface getter; Infrastructure services may read through the
    /// concrete <see cref="AppDbContext"/> getter. Matching only the interface would leave a
    /// fail-open hole for every concrete-context read. <see cref="The_scan_SEES_a_site_of_every_shape"/>
    /// pins one site per declaring type so a future narrowing here cannot go silent.
    /// </remarks>
    private static readonly string[] JobAdsGetterDeclaringTypes =
    [
        typeof(IAppDbContext).FullName!,
        typeof(AppDbContext).FullName!,
    ];

    private const string GetterName = "get_JobAds";

    /// <summary>
    /// The scanned assemblies: Application (interface reads) and Infrastructure (concrete reads).
    /// Api/Worker compose DI only and hold no <c>JobAds</c> queries; Domain cannot see EF.
    /// </summary>
    private static IEnumerable<string> ScannedAssemblyPaths =>
    [
        typeof(IAppDbContext).Assembly.Location,   // Jobbliggaren.Application
        typeof(AppDbContext).Assembly.Location,     // Jobbliggaren.Infrastructure
    ];

    // ────────────────────────────────────────────────────────────────────────────────────────
    // THE SCAN
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every <c>get_JobAds</c> call in the two assemblies, attributed to its LOGICAL method
    /// (<c>Namespace.Type.Method</c>), with a count. Compiler-generated wrappers — async/iterator
    /// state machines (<c>&lt;Handle&gt;d__N.MoveNext</c>), lambdas (<c>&lt;Handle&gt;b__N</c>) and
    /// local functions (<c>&lt;Handle&gt;g__L|N</c>) — are unwrapped back to the method that declared
    /// them, so the registry key is the method a human wrote, not a mangled ordinal-bearing name
    /// (which would be reorder-fragile).
    /// </summary>
    private static Dictionary<string, int> ScanSites()
    {
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in ScannedAssemblyPaths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);

            foreach (var module in assembly.Modules)
                foreach (var type in module.GetTypes())
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        var hits = 0;
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                                continue;

                            if (instruction.Operand is not MethodReference callee)
                                continue;

                            if (!string.Equals(callee.Name, GetterName, StringComparison.Ordinal))
                                continue;

                            if (!JobAdsGetterDeclaringTypes.Contains(
                                    callee.DeclaringType.FullName, StringComparer.Ordinal))
                                continue;

                            hits++;
                        }

                        if (hits == 0)
                            continue;

                        var key = LogicalOwnerKey(method);
                        sites[key] = sites.GetValueOrDefault(key) + hits;
                    }
        }

        return sites;
    }

    /// <summary>
    /// Resolve a (possibly compiler-generated) method to the <c>Namespace.Type.Method</c> a human
    /// wrote. Async/iterator MoveNext bodies live in a nested <c>&lt;Owner&gt;d__N</c> state-machine
    /// type; lambdas/local functions carry the owner in their own <c>&lt;Owner&gt;b__..</c> /
    /// <c>&lt;Owner&gt;g__..</c> name. Walk out of every compiler-generated layer, taking the owner
    /// name from the innermost angle-bracketed segment, and the logical type from the first
    /// non-compiler-generated declaring type.
    /// </summary>
    private static string LogicalOwnerKey(MethodDefinition method)
    {
        var ownerFromMethod = ExtractAngleOwner(method.Name);

        var type = method.DeclaringType;
        string? ownerFromType = null;
        while (type is not null && IsCompilerGenerated(type))
        {
            ownerFromType ??= ExtractAngleOwner(type.Name);
            type = type.DeclaringType;
        }

        var logicalType = (type ?? method.DeclaringType).FullName;
        var logicalMethod = ownerFromMethod ?? ownerFromType ?? method.Name;
        return $"{logicalType}.{logicalMethod}";
    }

    /// <summary>The owner name inside the first <c>&lt;Owner&gt;</c> segment, or null (none / empty
    /// — e.g. the shared <c>&lt;&gt;c</c> display class has nothing between the brackets).</summary>
    private static string? ExtractAngleOwner(string name)
    {
        var open = name.IndexOf('<');
        if (open < 0)
            return null;
        var close = name.IndexOf('>', open + 1);
        if (close <= open + 1)
            return null;
        return name[(open + 1)..close];
    }

    private static bool IsCompilerGenerated(TypeDefinition type) =>
        type.Name.StartsWith('<')
        || type.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    // ────────────────────────────────────────────────────────────────────────────────────────
    // THE GATE — fail-closed, site-granular via count-equality
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE test. Every <c>get_JobAds</c> site the scan finds is classified, and each method's
    /// declared decision count equals the number of calls the scan sees in it. A new read — anywhere,
    /// including a method that already reads <c>JobAds</c> — moves the count and breaks the build,
    /// naming the method and the number of decisions owed.
    /// </summary>
    [Fact]
    public void Every_JobAds_read_site_carries_a_lifecycle_decision()
    {
        var observed = ScanSites();
        var registry = JobAdLifecycleReadRegistry.Sites;

        var problems = new List<string>();

        foreach (var (method, count) in observed.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!registry.TryGetValue(method, out var decisions))
            {
                problems.Add($"{method} — {count} get_JobAds call(s), NO decision in the registry");
                continue;
            }

            if (decisions.Count != count)
            {
                problems.Add(
                    $"{method} — scan sees {count} get_JobAds call(s), registry declares "
                    + $"{decisions.Count}. Add/remove a JobAdSiteDecision so the counts match "
                    + "(SITE granularity: one decision per call).");
            }
        }

        // The inverse: a registry entry for a method the scan no longer sees is stale prose claiming
        // a control over a read that is gone — the same rot the precedent guards against.
        foreach (var method in registry.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!observed.ContainsKey(method))
                problems.Add(
                    $"{method} — in the registry but the scan finds NO get_JobAds call. Remove the "
                    + "stale entry (the read moved or was deleted).");
        }

        problems.ShouldBeEmpty(
            "every backend read of JobAds must carry a WRITTEN lifecycle decision "
            + "(JobAdLifecycleReadRegistry.Sites), or the build breaks naming the decision owed:\n\n"
            + "  ActiveOnly — restricts to Status == Active (the safe default; no reason needed)\n"
            + "  AnyStatus  — deliberately admits non-Active rows; a WRITTEN reason is required\n"
            + "  WritePath  — mutates/inserts JobAds; a WRITTEN reason naming its gate is required\n\n"
            + "The scan reads compiled IL, not source text — it is NOT a source-string scan, and it "
            + "cannot tell a read from a write, so a writer is classified (WritePath), never scoped "
            + "out. Sites:\n  " + string.Join("\n  ", problems));
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // ANTI-VACUITY — the scan must SEE every shape a site can take
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The gate can only judge sites the scan SURFACES. Narrow the matcher — drop the concrete
    /// <see cref="AppDbContext"/> getter, match only <c>call</c> not <c>callvirt</c>, forget to
    /// unwrap async state machines — and it reports FEWER sites, which reads as MORE classified.
    /// That is exactly how a blind spot hides. Each sentinel pins a shape that must stay visible.
    /// </summary>
    [Fact]
    public void The_scan_SEES_a_site_of_every_shape()
    {
        var observed = ScanSites();

        var sentinels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // async handler through the IAppDbContext interface getter (the dominant shape)
            ["Jobbliggaren.Application.JobAds.Queries.GetJobAd.GetJobAdQueryHandler.Handle"] =
                "async Handle reading db.JobAds through the IAppDbContext interface getter — the "
                + "async state machine (<Handle>d__N.MoveNext) must be unwrapped back to Handle.",

            // an Infrastructure read — pins that the scan reaches Infrastructure, not only Application
            ["Jobbliggaren.Infrastructure.JobAds.PerUserJobAdSearchQuery.SearchPerUserAsync"] =
                "an Infrastructure search reading db.JobAds — pins that the scan sees Infrastructure "
                + "reads, not only Application ones.",

            // a bulk WritePath via ExecuteUpdateAsync (a .Where before a write is still a get_JobAds)
            ["Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker.ArchiveJobAdsWithMissCountAtLeastAsync"] =
                "a bulk ExecuteUpdateAsync archival writer — the scan cannot tell it from a read, "
                + "so it MUST be seen and classified WritePath, never scoped out.",

            // a GroupJoin read (JobAds as the inner sequence, not the query root)
            ["Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds.ListSavedJobAdsQueryHandler.Handle"] =
                "db.JobAds as a GroupJoin inner sequence — a get_JobAds call that is not the query "
                + "root; the scan must still see it.",

            // a .Add insert (a WritePath that is NOT a .Where — pins that the getter, not a predicate,
            // is what the scan keys on)
            ["Jobbliggaren.Application.JobAds.Commands.CreateJobAd.CreateJobAdCommandHandler.Handle"] =
                "db.JobAds.Add(...) — an insert with no query predicate; the scan keys on the getter "
                + "call, not on a Where, so it must still see a bare .Add.",

            // a .FromSql-chained read (the getter precedes a raw fragment — the getter is still emitted)
            ["Jobbliggaren.Infrastructure.Matching.MatchScorer.ScoreBatchAsync"] =
                "db.JobAds.FromSql(...) — the getter call precedes a raw SQL fragment; the site is "
                + "seen (the predicate inside the fragment is the R4 non-reach, not the site).",
        };

        var missing = sentinels
            .Where(s => !observed.ContainsKey(s.Key))
            .Select(s => $"{s.Key} — {s.Value}")
            .ToList();

        missing.ShouldBeEmpty(
            "the scan no longer sees a site shape it must see. A narrowed matcher makes the registry "
            + "look MORE complete while covering LESS — the vacuity this whole control exists to end. "
            + "Unseen shapes:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The matcher must key on the getter of BOTH declaring types that expose <c>JobAds</c> — the
    /// <see cref="IAppDbContext"/> interface AND the concrete <see cref="AppDbContext"/> — so a
    /// future read through the concrete context cannot slip past a matcher narrowed to the interface
    /// (CTO R2 fail-open protection). Every current site reads through the interface getter (verified
    /// by the scan), so there is no concrete-getter site to sentinel today; this structural pin holds
    /// the breadth regardless, and if a concrete read is ever added, add its site to the sentinels.
    /// </summary>
    [Fact]
    public void The_matcher_covers_both_declaring_types_of_the_JobAds_getter()
    {
        JobAdsGetterDeclaringTypes.ShouldContain(typeof(IAppDbContext).FullName!,
            "the interface getter must be matched — the dominant read shape.");
        JobAdsGetterDeclaringTypes.ShouldContain(typeof(AppDbContext).FullName!,
            "the concrete AppDbContext getter must be matched too — narrowing to the interface would "
            + "leave a fail-open hole for any Infrastructure read through the concrete context.");
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // THE REASON FLOORS — a decision that owes a reason has a real one
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every decision carries a locating <see cref="JobAdSiteDecision.Note"/>; every
    /// <see cref="JobAdSiteKind.AnyStatus"/> and <see cref="JobAdSiteKind.WritePath"/> decision
    /// carries a written <see cref="JobAdSiteDecision.Reason"/> past a length floor. AnyStatus and
    /// WritePath make the load-bearing claims (this read admits archived rows / this call mutates the
    /// lifecycle axis), so they must cost the most to assert — the precedent's "strongest claim,
    /// highest price" inverted-cost lesson.
    /// </summary>
    [Fact]
    public void Every_decision_that_owes_a_reason_has_one()
    {
        JobAdLifecycleReadRegistry.Sites.ShouldNotBeEmpty(
            "the registry classifies no sites — this gate would be vacuous. It must classify the "
            + "population the scan finds.");

        var thin = new List<string>();

        foreach (var (method, decisions) in JobAdLifecycleReadRegistry.Sites)
            foreach (var (decision, index) in decisions.Select((d, i) => (d, i)))
            {
                var where = $"{method}[{index}] ({decision.Kind})";

                if (string.IsNullOrWhiteSpace(decision.Note) || decision.Note.Length <= 20)
                    thin.Add($"{where} — Note is missing or too thin to locate the site.");

                var owesReason = decision.Kind is JobAdSiteKind.AnyStatus or JobAdSiteKind.WritePath;
                if (owesReason && (decision.Reason is null || decision.Reason.Length <= 60))
                    thin.Add(
                        $"{where} — {decision.Kind} owes a WRITTEN reason (>60 chars). "
                        + "Name why this site admits non-Active rows / what its write gate is. "
                        + "'we judged it fine' is not a reason.");

                if (decision.Kind == JobAdSiteKind.ActiveOnly && decision.Reason is not null)
                    thin.Add(
                        $"{where} — ActiveOnly needs no reason (it is the conforming default). If this "
                        + "site actually admits non-Active rows, it is AnyStatus, not ActiveOnly.");
            }

        thin.ShouldBeEmpty(
            "these decisions do not meet the reason floor:\n  " + string.Join("\n  ", thin));
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // REACH HONESTY — the stated non-reach is a pinned artifact, not a hopeful comment
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The registry names the raw-SQL reads this IL scan is structurally blind to
    /// (<see cref="JobAdLifecycleReadRegistry.KnownNonReaches"/>). The list must be non-empty, each
    /// entry must explain itself, and — the load-bearing part — each named method must still EXIST
    /// (resolved by reflection). So a rename of a raw-SQL method breaks the build and forces a
    /// re-examination of whether its lifecycle predicate still needs disclosing; a future raw-SQL
    /// lifecycle read cannot be silently assumed covered by a control that never saw it. A guard that
    /// overstates its reach is the defect it exists to prevent.
    /// </summary>
    [Fact]
    public void The_stated_non_reach_names_real_raw_SQL_reads()
    {
        var nonReaches = JobAdLifecycleReadRegistry.KnownNonReaches;

        nonReaches.ShouldNotBeEmpty(
            "the registry promises to disclose what the IL scan cannot see. An empty list turns "
            + "'we cover every read' back on, wearing a new type — raw-SQL reads of job_ads exist "
            + "and emit no get_JobAds call.");

        var infrastructure = typeof(AppDbContext).Assembly;
        var broken = new List<string>();

        foreach (var entry in nonReaches)
        {
            if (entry.Why.Length <= 60)
                broken.Add($"{entry.TypeFullName}.{entry.Method} — the 'why' is too thin to be a disclosure.");

            // Reflect the type (these Infrastructure types are internal — resolve by full name, and
            // include non-public methods) and require the named method to still exist.
            var type = infrastructure.GetType(entry.TypeFullName, throwOnError: false);
            if (type is null)
            {
                broken.Add($"{entry.TypeFullName} — type not found by reflection; it moved or was renamed.");
                continue;
            }

            var method = type.GetMethod(
                entry.Method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method is null)
                broken.Add(
                    $"{entry.TypeFullName}.{entry.Method} — method not found. If the raw-SQL method was "
                    + "renamed or removed, re-derive whether its job_ads read still needs disclosing here.");
        }

        broken.ShouldBeEmpty(
            "the stated non-reach must name REAL raw-SQL reads the scan cannot see:\n  "
            + string.Join("\n  ", broken));
    }
}
