using System.Reflection;
using Jobbliggaren.Application.JobAds.Queries;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #745 (epic #737, perf finding <c>d1-list-dto-ships-full-description</c>) — the LIST-wire DTO
/// (<see cref="JobAdDto"/>) is structurally incapable of carrying the ad <c>Description</c>, while the
/// DETAIL-wire DTO (<see cref="JobAdDetailDto"/>) keeps it. The list surfaces (ListJobAds /
/// RunSavedSearch / the per-user match sort, all funnelling through
/// <c>JobAdSearchComposition.ToDto()</c>) never render the ad body — the cards read title/company/
/// dates only, and the detail modal/page fetch the body separately via <c>GetJobAd</c>. Projecting the
/// untruncated <c>Description</c> per row (PageSize up to 100) de-TOASTed a wide column Postgres → API →
/// BFF for a payload nothing reads. This lock keeps the divergence structural rather than convention-
/// dependent: re-adding <c>Description</c> to the list DTO breaks the build here.
/// <para>
/// This is a SIBLING of <c>RecruiterContactFtsLockTests</c> L4 (which pins the <c>Contacts</c> axis of
/// the same list/detail split), not an extension of it — L4's concern is contact-incapability; folding
/// "no Description" into it would dilute that lock. The DTO is a positional record, so its shape IS the
/// serialized wire shape — a reflection guard on the type is fully sufficient (no SQL/host needed).
/// </para>
/// </summary>
public class JobAdListDtoShapeTests
{
    [Fact]
    public void List_dto_omits_description_detail_dto_keeps_it()
    {
        // The perf intent: the list-row form carries no Description.
        HasPublicProperty(typeof(JobAdDto), "Description").ShouldBeFalse(
            "JobAdDto is the LIST wire — it must NOT carry the ad Description (perf, #745/#737 " +
            "d1-list-dto-ships-full-description). No list surface renders the body; the detail path " +
            "fetches it via GetJobAd → JobAdDetailDto.");

        // Counterfactual (memory reference_absence_proves_gate_only_with_counterfactual): a bare
        // absence assertion is vacuous — it passes even if this test cannot see a property named
        // "Description". Pinning that the DETAIL DTO still HAS it proves the probe works AND locks the
        // divergence in both directions (a "helpful" re-unification that drops it from detail also reds).
        HasPublicProperty(typeof(JobAdDetailDto), "Description").ShouldBeTrue(
            "JobAdDetailDto is the DETAIL wire — it must keep the ad Description (the detail modal/page " +
            "render it). If this fails, the probe is broken or the split has been mis-unified.");
    }

    private static bool HasPublicProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null;
}
