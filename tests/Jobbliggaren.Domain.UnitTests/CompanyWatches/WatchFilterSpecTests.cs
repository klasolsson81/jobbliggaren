using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// Bevaknings-reconcile RF-2 + F4a (#803, CTO 2026-07-12 Q3=B) — invariants for
/// <see cref="WatchFilterSpec"/>: the empty-spec invariant across BOTH geo axes (NULL column is the
/// only canonical "no filter"), identical normalization of both axes (sorted+distinct ordinal), the
/// per-element concept-id format, the PER-AXIS <c>SearchCriteria.MaxConceptIds</c> cap, structural
/// equality including <see cref="WatchFilterSpec.Regions"/> (the EF jsonb value comparison relies on
/// it), and the <see cref="WatchFilterSpec.AdmitsLocation"/> UNION semantics.
///
/// <para>
/// <b>The union is the point of F4a.</b> Job ads carry BOTH <c>municipality_concept_id</c> AND
/// <c>region_concept_id</c>, and an ad may be tagged at LÄN granularity with NO municipality at all.
/// The pre-F4a predicate (<c>AdmitsMunicipality</c>) only knew the kommun axis, so a whole-län
/// selection had to be expanded into its kommun-ids — which would silently drop every län-only ad in
/// that län from the user's notifications (a silent miss in a never-miss product). The regression pin
/// is <see cref="AdmitsLocation_RegionOnlySpec_AdmitsLanOnlyAdWithNullMunicipality"/>.
/// </para>
/// </summary>
public class WatchFilterSpecTests
{
    // ---------------------------------------------------------------
    // Create — empty-spec invariant across BOTH axes
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NoAxisSetAndOnlyMatchedFalse_Fails()
    {
        var result = WatchFilterSpec.Create(municipalities: null, regions: null, onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.Empty");
    }

    [Fact]
    public void Create_WhitespaceOnlyOnBothAxesAndOnlyMatchedFalse_Fails()
    {
        // Normalization runs BEFORE the invariant check on both axes — a spec that is only
        // whitespace is an EMPTY spec, not a two-element one (the NULL column is the only
        // canonical "no filter"; an inert stored spec must never exist).
        var result = WatchFilterSpec.Create(["  ", ""], ["", " "], onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.Empty");
    }

    [Fact]
    public void Create_OnlyMatchedAlone_Succeeds()
    {
        var result = WatchFilterSpec.Create(municipalities: null, regions: null, onlyMatched: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipalities.ShouldBeEmpty();
        result.Value.Regions.ShouldBeEmpty();
        result.Value.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public void Create_MunicipalitiesAlone_Succeeds()
    {
        var result = WatchFilterSpec.Create(["1gEC_kvM_TXK"], regions: null, onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipalities.ShouldBe(["1gEC_kvM_TXK"]);
        result.Value.Regions.ShouldBeEmpty();
        result.Value.OnlyMatched.ShouldBeFalse();
    }

    [Fact]
    public void Create_RegionsAlone_Succeeds()
    {
        // A whole-län selection is a COMPLETE filter on its own — it is never expanded into
        // kommun-ids (that expansion is exactly the silent-miss bug F4a exists to prevent).
        var result = WatchFilterSpec.Create(municipalities: null, ["CifL_Rzy_Mku"], onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Regions.ShouldBe(["CifL_Rzy_Mku"]);
        result.Value.Municipalities.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // IsEmptySelection — the SSOT for "the user cleared the filter" (code-reviewer Major, F4a)
    //
    // The bug this exists to prevent: the handler used to decide emptiness by counting the RAW lists
    // while Create decides it on the NORMALIZED ones. A payload like {"municipalities": [""]} — what a
    // form emits when the user empties the last chip — then counted as NON-empty, went to Create, was
    // normalized to nothing, failed the empty-spec invariant, and came back as 400 "Minst ett filter
    // krävs" to a user who was trying to REMOVE the filter. The old filter stayed active with no way to
    // clear it. Two authorities on one question is the bug; these tests pin the ONE authority.
    // ---------------------------------------------------------------

    [Fact]
    public void IsEmptySelection_NullLists_IsTrue()
    {
        WatchFilterSpec.IsEmptySelection(null, null, onlyMatched: false).ShouldBeTrue();
    }

    [Fact]
    public void IsEmptySelection_EmptyLists_IsTrue()
    {
        WatchFilterSpec.IsEmptySelection([], [], onlyMatched: false).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsEmptySelection_WhitespaceOnlyEntries_IsTrue(string blank)
    {
        // The regression payload. Emptiness is decided AFTER normalization on BOTH axes — a list whose
        // only entry is blank carries no selection at all.
        WatchFilterSpec.IsEmptySelection([blank], [blank], onlyMatched: false).ShouldBeTrue();
        WatchFilterSpec.IsEmptySelection([blank], null, onlyMatched: false).ShouldBeTrue();
        WatchFilterSpec.IsEmptySelection(null, [blank], onlyMatched: false).ShouldBeTrue();
    }

    [Fact]
    public void IsEmptySelection_RealMunicipality_IsFalse()
    {
        WatchFilterSpec.IsEmptySelection(["kommun_a"], null, onlyMatched: false).ShouldBeFalse();
    }

    [Fact]
    public void IsEmptySelection_RealRegion_IsFalse()
    {
        // The län axis counts as a selection on its own (a whole-län pick is a complete filter).
        WatchFilterSpec.IsEmptySelection(null, ["lan_skane"], onlyMatched: false).ShouldBeFalse();
    }

    [Fact]
    public void IsEmptySelection_OnlyMatchedAlone_IsFalse()
    {
        WatchFilterSpec.IsEmptySelection(null, null, onlyMatched: true).ShouldBeFalse();
    }

    [Fact]
    public void IsEmptySelection_BlankEntriesMixedWithARealId_IsFalse()
    {
        WatchFilterSpec.IsEmptySelection(["", "kommun_a", "  "], null, onlyMatched: false).ShouldBeFalse();
    }

    [Theory]
    // Every shape the transport can send: blank-only, empty, null, real ids, OnlyMatched.
    [InlineData(new string[0], new string[0], false)]
    [InlineData(new[] { "" }, new string[0], false)]
    [InlineData(new[] { "  " }, new[] { "" }, false)]
    [InlineData(new string[0], new[] { "\t" }, false)]
    [InlineData(new[] { "kommun_a" }, new string[0], false)]
    [InlineData(new string[0], new[] { "lan_skane" }, false)]
    [InlineData(new[] { "" }, new string[0], true)]
    [InlineData(new[] { "kommun_a" }, new[] { "lan_skane" }, true)]
    public void IsEmptySelection_AgreesWithCreateEmptySpecInvariant(
        string[] municipalities, string[] regions, bool onlyMatched)
    {
        // THE pin: the two authorities must never disagree. IsEmptySelection true ⟺ Create rejects the
        // selection as an empty spec. A divergence here IS the bug (the handler clears when Create would
        // have said "empty", or the handler calls Create with something Create then refuses).
        var isEmpty = WatchFilterSpec.IsEmptySelection(municipalities, regions, onlyMatched);
        var create = WatchFilterSpec.Create(municipalities, regions, onlyMatched);

        var createRejectedAsEmpty =
            create.IsFailure && create.Error.Code == "WatchFilterSpec.Empty";

        createRejectedAsEmpty.ShouldBe(isEmpty,
            "IsEmptySelection och Create måste vara ENIGA om vad ett tomt val är — " +
            "två auktoriteter på samma fråga var precis buggen");
    }

    // ---------------------------------------------------------------
    // Normalization — identical on both axes (trim, drop blank, distinct + sort ordinal)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesMunicipalities_TrimDistinctSortedOrdinal()
    {
        var result = WatchFilterSpec.Create(
            [" zzz_id ", "aaa_id", "zzz_id", "", "  ", "AAA_id"], regions: null, onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        // Ordinal sort: uppercase before lowercase.
        result.Value.Municipalities.ShouldBe(["AAA_id", "aaa_id", "zzz_id"]);
    }

    [Fact]
    public void Create_NormalizesRegions_TrimDistinctSortedOrdinal_IdenticallyToMunicipalities()
    {
        // The two axes are separate NAMESPACES but share ONE normalization rule. A canonical form
        // on both is what makes the jsonb value comparison (and therefore EF change detection)
        // honest: [" b ", "a", "a"] and ["a", "b"] must be the same stored spec.
        var result = WatchFilterSpec.Create(
            municipalities: null, [" zzz_id ", "aaa_id", "zzz_id", "", "  ", "AAA_id"], onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Regions.ShouldBe(["AAA_id", "aaa_id", "zzz_id"]);
    }

    // ---------------------------------------------------------------
    // Cap + per-element format (default-deny) — PER AXIS
    // ---------------------------------------------------------------

    [Fact]
    public void Create_MoreThanMaxConceptIdsMunicipalities_Fails()
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"id_{i}");

        var result = WatchFilterSpec.Create(tooMany, regions: null, onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.TooManyMunicipalities");
    }

    [Fact]
    public void Create_MoreThanMaxConceptIdsRegions_Fails()
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"lan_{i}");

        var result = WatchFilterSpec.Create(municipalities: null, tooMany, onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.TooManyRegions");
    }

    [Fact]
    public void Create_MaxConceptIdsOnBothAxesSimultaneously_Succeeds()
    {
        // The cap is PER AXIS, not a shared budget: a full kommun list plus a full län list is a
        // legitimate (if unusual) selection and must not be rejected by an accidental sum-cap.
        var municipalities = Enumerable.Range(0, SearchCriteria.MaxConceptIds).Select(i => $"kn_{i}");
        var regions = Enumerable.Range(0, SearchCriteria.MaxConceptIds).Select(i => $"lan_{i}");

        var result = WatchFilterSpec.Create(municipalities, regions, onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipalities.Count.ShouldBe(SearchCriteria.MaxConceptIds);
        result.Value.Regions.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö_id")]
    [InlineData("way_too_long_for_a_concept_id_over_32_chars")]
    [InlineData("semi;colon")]
    public void Create_InvalidMunicipalityConceptId_Fails(string invalid)
    {
        var result = WatchFilterSpec.Create([invalid], regions: null, onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.InvalidMunicipality");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö_id")]
    [InlineData("way_too_long_for_a_concept_id_over_32_chars")]
    [InlineData("semi;colon")]
    public void Create_InvalidRegionConceptId_Fails(string invalid)
    {
        // The region axis is default-deny on the SAME pattern — and reports its OWN error code, so
        // the FE can point at the axis the user actually touched.
        var result = WatchFilterSpec.Create(municipalities: null, [invalid], onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.InvalidRegion");
    }

    // ---------------------------------------------------------------
    // Structural equality (jsonb value-comparison footgun)
    // ---------------------------------------------------------------

    [Fact]
    public void Equals_LogicallyEqualSpecsOnBothAxes_AreStructurallyEqual()
    {
        var a = WatchFilterSpec.Create(["bbb", " aaa "], ["lan_b", " lan_a "], onlyMatched: true).Value;
        var b = WatchFilterSpec.Create(["aaa", "bbb", "bbb"], ["lan_a", "lan_b"], onlyMatched: true).Value;

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_SpecsDifferingOnlyInRegions_AreNotEqual()
    {
        // Regression pin for the EF jsonb VALUE COMPARISON: if Equals/GetHashCode ignored the
        // Regions axis, EF's change detection would consider a län-only edit a no-op and silently
        // NEVER persist the user's new selection (the write "succeeds" and changes nothing).
        var skane = WatchFilterSpec.Create(["kommun_a"], ["lan_skane"], onlyMatched: false).Value;
        var vastra = WatchFilterSpec.Create(["kommun_a"], ["lan_vastra"], onlyMatched: false).Value;

        skane.Equals(vastra).ShouldBeFalse();
        skane.GetHashCode().ShouldNotBe(vastra.GetHashCode());
    }

    [Fact]
    public void Equals_SpecWithRegions_IsNotEqualToSameSpecWithoutRegions()
    {
        var withRegion = WatchFilterSpec.Create(["kommun_a"], ["lan_skane"], onlyMatched: false).Value;
        var withoutRegion = WatchFilterSpec.Create(["kommun_a"], regions: null, onlyMatched: false).Value;

        withRegion.Equals(withoutRegion).ShouldBeFalse();
    }

    [Fact]
    public void Equals_DifferentSpecs_AreNotEqual()
    {
        var ortOnly = WatchFilterSpec.Create(["aaa"], regions: null, onlyMatched: false).Value;
        var ortAndMatched = WatchFilterSpec.Create(["aaa"], regions: null, onlyMatched: true).Value;
        var otherOrt = WatchFilterSpec.Create(["bbb"], regions: null, onlyMatched: false).Value;

        ortOnly.Equals(ortAndMatched).ShouldBeFalse();
        ortOnly.Equals(otherOrt).ShouldBeFalse();
        ortOnly.Equals(null).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // AdmitsLocation — UNION semantics (F4a / CTO Q3=B) + the 8A stance
    // ---------------------------------------------------------------

    [Fact]
    public void AdmitsLocation_NoGeoAxis_AdmitsEverything()
    {
        var spec = WatchFilterSpec.Create(municipalities: null, regions: null, onlyMatched: true).Value;

        spec.AdmitsLocation("any_kommun", "any_lan", adRemote: false).ShouldBeTrue();
        spec.AdmitsLocation(null, "any_lan", adRemote: false).ShouldBeTrue();
        spec.AdmitsLocation("any_kommun", null, adRemote: false).ShouldBeTrue();
        spec.AdmitsLocation(null, null, adRemote: false).ShouldBeTrue(
            "utan geo-axel är filtret inte geografiskt — även en annons helt utan ort passerar");
    }

    [Fact]
    public void AdmitsLocation_MunicipalityOnlySpec_AdmitsOnlyListedMunicipalities()
    {
        // The pre-F4a behaviour, unchanged: a kommun selection still means kommun.
        var spec = WatchFilterSpec.Create(["kommun_a", "kommun_b"], regions: null, onlyMatched: false).Value;

        spec.AdmitsLocation("kommun_a", "lan_x", adRemote: false).ShouldBeTrue();
        spec.AdmitsLocation("kommun_c", "lan_x", adRemote: false).ShouldBeFalse();
    }

    [Fact]
    public void AdmitsLocation_MunicipalityOnlySpec_RejectsLanOnlyAd()
    {
        // 8A: the user picked KOMMUNER, so a län-only ad (no municipality) is not admitted by the
        // kommun axis — and there is no region axis to admit it either.
        var spec = WatchFilterSpec.Create(["kommun_a"], regions: null, onlyMatched: false).Value;

        spec.AdmitsLocation(null, "lan_a", adRemote: false).ShouldBeFalse();
    }

    [Fact]
    public void AdmitsLocation_RegionOnlySpec_AdmitsLanOnlyAdWithNullMunicipality()
    {
        // THE F4a REGRESSION PIN. An ad tagged at LÄN granularity carries NO municipality. Under the
        // pre-F4a predicate (kommun axis only), a whole-län pick had to be expanded into kommun-ids,
        // and this ad — genuinely inside the picked län — would then match NOTHING and never notify
        // the user: a silent miss. The union admits it via the region axis. If this test goes red,
        // whole-län watchers have stopped being told about län-only ads.
        var spec = WatchFilterSpec.Create(municipalities: null, ["lan_skane"], onlyMatched: false).Value;

        spec.AdmitsLocation(municipalityConceptId: null, regionConceptId: "lan_skane", adRemote: false).ShouldBeTrue();
    }

    [Fact]
    public void AdmitsLocation_RegionOnlySpec_AdmitsAdInAnyMunicipalityOfThatRegion()
    {
        // "Hela Skåne" must also admit the kommun-tagged ads inside Skåne — the municipality is not
        // in the (empty) municipality list, so ONLY the region axis can admit it.
        var spec = WatchFilterSpec.Create(municipalities: null, ["lan_skane"], onlyMatched: false).Value;

        spec.AdmitsLocation("kommun_malmo", "lan_skane", adRemote: false).ShouldBeTrue();
    }

    [Fact]
    public void AdmitsLocation_RegionOnlySpec_RejectsAdInAnotherRegion()
    {
        // The union widens across AXES, never across VALUES: an unpicked län is still rejected.
        var spec = WatchFilterSpec.Create(municipalities: null, ["lan_skane"], onlyMatched: false).Value;

        spec.AdmitsLocation("kommun_goteborg", "lan_vastra", adRemote: false).ShouldBeFalse();
        spec.AdmitsLocation(null, "lan_vastra", adRemote: false).ShouldBeFalse();
    }

    [Fact]
    public void AdmitsLocation_BothAxesSet_EitherHitAdmits()
    {
        // Union, not intersection: a hit on EITHER axis is enough. An intersection here would reject
        // both of these ads (each satisfies exactly one axis) and starve the digest.
        var spec = WatchFilterSpec.Create(["kommun_a"], ["lan_skane"], onlyMatched: false).Value;

        spec.AdmitsLocation("kommun_a", "lan_vastra", adRemote: false).ShouldBeTrue("kommun-träff räcker");
        spec.AdmitsLocation("kommun_z", "lan_skane", adRemote: false).ShouldBeTrue("län-träff räcker");
        spec.AdmitsLocation("kommun_z", "lan_vastra", adRemote: false).ShouldBeFalse("ingen axel träffar");
    }

    [Fact]
    public void AdmitsLocation_ActiveGeoFilter_RejectsAdWithNeitherAxis()
    {
        // 8A data-minimizing stance, unchanged in kind by the union: an ad tagged with NEITHER a
        // municipality NOR a region cannot be shown to be inside the user's selection, so it never
        // passes an active geo filter (the hit is never created).
        var municipalityOnly = WatchFilterSpec.Create(["kommun_a"], regions: null, onlyMatched: false).Value;
        var regionOnly = WatchFilterSpec.Create(municipalities: null, ["lan_skane"], onlyMatched: false).Value;
        var both = WatchFilterSpec.Create(["kommun_a"], ["lan_skane"], onlyMatched: false).Value;

        municipalityOnly.AdmitsLocation(null, null, adRemote: false).ShouldBeFalse();
        regionOnly.AdmitsLocation(null, null, adRemote: false).ShouldBeFalse();
        both.AdmitsLocation(null, null, adRemote: false).ShouldBeFalse();
    }

    [Fact]
    public void AdmitsLocation_OnlyMatchedIsNotAGeoAxis_DoesNotNarrowLocation()
    {
        // OnlyMatched narrows at DISPATCH (read-time grade), never at the geo predicate — a spec with
        // OnlyMatched alone must not accidentally behave like an active geo filter and suppress hits
        // scan-side (that would be a never-created hit for a purely read-time preference).
        var spec = WatchFilterSpec.Create(municipalities: null, regions: null, onlyMatched: true).Value;

        spec.AdmitsLocation(null, null, adRemote: false).ShouldBeTrue();
        spec.AdmitsLocation("kommun_a", "lan_skane", adRemote: false).ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // #551 PR-B D6 — the remote/distans axis (union disjunct, remote-only spec valid)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_RemoteOnlySpec_IsValid()
    {
        // A spec whose ONLY narrowing is remote=true narrows to remote ads → non-empty/valid.
        var result = WatchFilterSpec.Create(
            municipalities: null, regions: null, onlyMatched: false, remote: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Remote.ShouldBeTrue();
    }

    [Fact]
    public void IsEmptySelection_RemoteTrueAlone_IsNotEmpty()
    {
        WatchFilterSpec.IsEmptySelection(null, null, onlyMatched: false, remote: true).ShouldBeFalse();
    }

    [Fact]
    public void IsEmptySelection_AllEmptyInclRemoteFalse_IsEmpty()
    {
        WatchFilterSpec.IsEmptySelection(null, null, onlyMatched: false, remote: false).ShouldBeTrue();
        // And Create fails-closed on the fully-empty selection (unchanged invariant).
        WatchFilterSpec.Create(null, null, onlyMatched: false, remote: false).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AdmitsLocation_RemoteOnlySpec_AdmitsRemoteAd_RejectsNonRemote()
    {
        var spec = WatchFilterSpec.Create(
            municipalities: null, regions: null, onlyMatched: false, remote: true).Value;

        // A remote (location-less) ad passes the remote disjunct...
        spec.AdmitsLocation(null, null, adRemote: true).ShouldBeTrue();
        // ...but a NON-remote ad does NOT — the early-return MUST account for Remote, else a
        // remote-only spec would admit every ad (the D6 load-bearing fix).
        spec.AdmitsLocation("kommun_a", "lan_skane", adRemote: false).ShouldBeFalse();
        spec.AdmitsLocation(null, null, adRemote: false).ShouldBeFalse();
    }

    [Fact]
    public void AdmitsLocation_MunicipalityUnionRemote_EitherHitAdmits()
    {
        // Union across axes incl. remote (same shape as ApplyFilter's Distans+muni case):
        // a muni-hit OR a remote ad passes; a non-remote ad in an unpicked kommun does not.
        var spec = WatchFilterSpec.Create(
            ["kommun_a"], regions: null, onlyMatched: false, remote: true).Value;

        spec.AdmitsLocation("kommun_a", null, adRemote: false).ShouldBeTrue("kommun-träff räcker");
        spec.AdmitsLocation(null, null, adRemote: true).ShouldBeTrue("remote-annons räcker");
        spec.AdmitsLocation("kommun_z", "lan_x", adRemote: false).ShouldBeFalse("varken kommun eller remote");
    }

    [Fact]
    public void Equals_DiffersOnlyByRemote_AreNotEqual()
    {
        // Pins that Remote is a member of BOTH Equals and GetHashCode (jsonb-equality footgun).
        var withRemote = WatchFilterSpec.Create(["kommun_a"], null, onlyMatched: false, remote: true).Value;
        var withoutRemote = WatchFilterSpec.Create(["kommun_a"], null, onlyMatched: false, remote: false).Value;

        withRemote.Equals(withoutRemote).ShouldBeFalse();
        withRemote.GetHashCode().ShouldNotBe(withoutRemote.GetHashCode());
    }
}
