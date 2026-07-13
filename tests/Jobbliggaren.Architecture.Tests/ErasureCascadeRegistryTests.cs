using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The Art. 17 cascade registry, enforced (#842).
/// </summary>
/// <remarks>
/// <b>Why this test is the most valuable thing in the PR.</b> ADR 0024 already had an Art. 17
/// cascade registry. It listed <c>raw_payload</c> and nothing else — not <c>job_ads.description</c>,
/// where the recruiter's address actually was. It was prose in a document, so it went stale
/// silently, and an auditor reading it would have concluded we were compliant while the only
/// erasure path erased nothing. <b>A registry a human has to remember to update is not a
/// registry.</b>
/// <para>
/// So the registry is now a type, and this test is its enforcement: every persisted aggregate the
/// application can reach must be CLASSIFIED — cascaded, matched-but-not-erased (with a written
/// legal ground), or structurally incapable of holding recruiter text (with a written reason). Add
/// a table and forget to think about erasure, and <b>the build breaks</b> with a message telling you
/// exactly which decision you owe.
/// </para>
/// <para>
/// The row counts at the time of writing were 1 and 0. That is not a reason to skip any of this —
/// a control keyed to a row count measured on one afternoon is precisely the mistake #842 IS. The
/// vacuous purger was also correct for as long as nobody looked.
/// </para>
/// </remarks>
public class ErasureCascadeRegistryTests
{
    /// <summary>
    /// Every entity behind a <c>DbSet</c> on <see cref="IAppDbContext"/> — that is, every
    /// persisted surface an Application handler can reach at all.
    /// </summary>
    private static IReadOnlyList<Type> PersistedEntityTypes() =>
        [.. typeof(IAppDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(t => t.GetGenericArguments()[0])];

    [Fact]
    public void Every_persisted_surface_is_classified_for_Art17_erasure()
    {
        var classified = new HashSet<Type>(ErasureCascadeRegistry.Cascaded);
        classified.UnionWith(ErasureCascadeRegistry.MatchedButNotErased.Keys);
        classified.UnionWith(ErasureCascadeRegistry.NoRecruiterTextSurface.Keys);

        var unclassified = PersistedEntityTypes().Where(t => !classified.Contains(t)).ToList();

        unclassified.ShouldBeEmpty(
            "a new persisted surface must be classified in ErasureCascadeRegistry: does an Art. 17 "
            + "erasure of a recruiter reach it?\n\n"
            + "Put it in exactly one of:\n"
            + "  * Cascaded                — we search AND erase it\n"
            + "  * MatchedButNotErased     — it can hold her identifier and we deliberately keep it "
            + "(write the LEGAL GROUND; the dry run will report it, and the reply to the data "
            + "subject will disclose it)\n"
            + "  * NoRecruiterTextSurface  — it structurally cannot hold recruiter free text (write "
            + "WHY; 'we looked and it was fine' is what the last registry said)\n\n"
            + "Unclassified: " + string.Join(", ", unclassified.Select(t => t.Name)));
    }

    [Fact]
    public void No_surface_is_classified_twice()
    {
        var all = ErasureCascadeRegistry.Cascaded
            .Concat(ErasureCascadeRegistry.MatchedButNotErased.Keys)
            .Concat(ErasureCascadeRegistry.NoRecruiterTextSurface.Keys)
            .ToList();

        all.Distinct().Count().ShouldBe(all.Count,
            "a surface that is both erased and not erased is a contradiction we would ship as a "
            + "sentence to a data subject.");
    }

    /// <summary>
    /// A ground that is blank is a ground that was not thought about. Both "we keep it" sets carry
    /// prose because both are things we will have to say out loud to a person who asked us to
    /// delete her data.
    /// </summary>
    [Fact]
    public void Every_not_erased_surface_carries_a_written_reason()
    {
        foreach (var (type, reason) in ErasureCascadeRegistry.MatchedButNotErased)
            reason.Length.ShouldBeGreaterThan(40, $"{type.Name} needs a real legal ground.");

        foreach (var (type, reason) in ErasureCascadeRegistry.NoRecruiterTextSurface)
            reason.Length.ShouldBeGreaterThan(10, $"{type.Name} needs a stated reason.");
    }

    /// <summary>
    /// The response the data subject is answered with must enumerate exactly the surfaces the
    /// registry knows about. If a surface is added to the registry but not to the reported counts,
    /// we would erase (or knowingly keep) something we never told her about — which is the failure
    /// this whole issue is a case of.
    /// </summary>
    [Fact]
    public void The_reported_surface_counts_match_the_registry()
    {
        var reported = typeof(ErasureSurfaceCounts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) && p.Name != nameof(ErasureSurfaceCounts.Total))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        reported.ShouldBe(ErasureCascadeRegistry.ReportedSurfaces, ignoreOrder: true,
            "ErasureSurfaceCounts is what we TELL the data subject we looked at. A surface the "
            + "registry reasons about but the response does not report is something we erased — or "
            + "knowingly kept — without telling her.\n"
            + $"reported by the response: [{string.Join(", ", reported.Order())}]\n"
            + $"declared by the registry: [{string.Join(", ", ErasureCascadeRegistry.ReportedSurfaces.Order())}]");
    }
}
