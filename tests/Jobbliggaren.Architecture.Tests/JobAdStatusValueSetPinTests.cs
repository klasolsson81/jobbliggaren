using System.Reflection;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The <see cref="JobAdStatus"/> value set is closed at exactly <c>{Active, Archived, Erased}</c>,
/// and declaring a fourth value <b>breaks the build here</b> — the build-time guard #885's CTO
/// ruling (G3) recorded as owed but "held only by prose".
/// </summary>
/// <remarks>
/// <b>Why this pin exists.</b> #885 (<c>d617adfc</c>) gated the match-detail scorer/handler on a
/// <b>deny-list</b> <c>Status != Erased</c>, over an allow-list, and G3 recorded a "binding
/// constraint on the written reason": the deny-list's drift risk is real and is <b>NOT</b> otherwise
/// build-guarded. A <c>Status != Erased</c> site closes over a closed-world assumption — that "not
/// Erased" means <i>exactly</i> <c>{Active, Archived}</c>. Declare a fourth <c>JobAdStatus</c> value
/// and every such site silently starts admitting it (B9: a fourth value costs zero migrations —
/// <c>varchar(20)</c> via a value converter), which is #885's own defect in mirror image. Three
/// agents (dotnet-architect Note, security-auditor Minor 2, test-writer Minor 1) converged
/// independently on this fix.
/// <para>
/// <b>What was NOT already guarding it.</b> <c>JobAdLifecycleReadRegistryTests</c> (#887) pins the
/// COUNT of <c>get_JobAds</c> call SITES per method — it never reads the status VALUE set, so a
/// fourth value passes it untouched. The only prior controls were prose: #886's declaration-site
/// rule on <see cref="JobAdStatus"/> ("no value without a writer, a distinct invariant, and a
/// decision on historical rows") and the written mirror-dependency in each deny-list site's registry
/// reason. Prose does not break a build; this does.
/// <para>
/// <b>Form (CTO 2026-07-17, binding).</b> Reflection over the DECLARED field set — the surface the
/// risk lives on (a fourth <c>public static readonly JobAdStatus</c> field). Not the
/// <see cref="JobAdStatus.FromValue"/> parser (that is fail-open on a bare field-add — the exact
/// dangerous mutation), and not a declaration bolted onto <c>JobAdLifecycleReadRegistry</c> (a
/// different reason to change, and a §6.5 hotspot another open PR owns). It is the direct analogue of
/// the named house precedent <c>ErasureCascadeRegistryTests.The_reported_surface_counts_match_the_registry</c>
/// (reflect a type's members → compare to a declared set → break on divergence) and uses the same
/// <c>GetFields(BindingFlags…)</c> idiom as <see cref="OrganizationNumberSurfacingGuardTests"/>.
/// </para>
/// </para>
/// <para>
/// <b>What this pin does NOT do.</b> It asserts the EXISTENCE and IDENTITY of the value set; it does
/// NOT verify the TRUTH of any <c>!= Erased</c> site's reasoning — that stays prose in
/// <c>JobAdLifecycleReadRegistry</c>, read by a human. The pin's job is to FORCE the re-examination
/// when a value is added or removed, not to perform it. Nor does it enumerate the deny-list sites:
/// an enumeration in a test drifts (the #887 D9 remark), so the failure message POINTS TO the
/// registry as the single source of truth for the deny-list class.
/// </para>
/// </remarks>
public class JobAdStatusValueSetPinTests
{
    /// <summary>
    /// The closed world. Every <c>Status != Erased</c> deny-list site in the codebase is correct
    /// only while "not Erased" means exactly the complement of this set — <c>{Active, Archived}</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> ExpectedValues =
        new HashSet<string>(StringComparer.Ordinal) { "Active", "Archived", "Erased" };

    // ────────────────────────────────────────────────────────────────────────────────────────
    // THE PIN — set identity over the declared field set
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE test. The declared <see cref="JobAdStatus"/> value set is EXACTLY
    /// <c>{Active, Archived, Erased}</c>. A new value (ADD), a removed/renamed value (MISSING), or a
    /// drifted <c>.Value</c> string all break the build, naming what drifted and pointing the reader
    /// at <c>JobAdLifecycleReadRegistry</c>.
    /// </summary>
    [Fact]
    public void The_JobAdStatus_value_set_is_exactly_Active_Archived_Erased()
    {
        var declared = DeclaredStatusValues();

        // Vacuity guard: if the reflection surfaces nothing, the FieldType filter or the BindingFlags
        // drifted and this pin would be judging an empty set. Fail as "reflection broke", loudly,
        // rather than letting an empty set read as "everything is missing".
        declared.ShouldNotBeEmpty(
            "reflection over JobAdStatus surfaced no value fields — the FieldType filter or the "
            + "BindingFlags in DeclaredStatusValues() has drifted, and this pin just became vacuous. "
            + "Fix the reflection; do not delete the test.");

        var drift = UndeclaredOrMissingValues(declared, ExpectedValues);

        drift.ShouldBeEmpty(
            "the JobAdStatus value set has drifted from {Active, Archived, Erased}.\n\n"
            + "A NEW value (UNDECLARED) is the dangerous case: every 'Status != Erased' deny-list "
            + "read/write site closes over the closed-world assumption that 'not Erased' means "
            + "EXACTLY {Active, Archived}, so a new value silently WIDENS what those sites admit — "
            + "#885's own defect in mirror image, and it is NOT otherwise build-guarded "
            + "(JobAdLifecycleReadRegistryTests pins the COUNT of get_JobAds sites, never the status "
            + "VALUES; B9: a fourth value costs zero migrations).\n\n"
            + "Before you change the expected set here:\n"
            + "  1. Re-examine every 'Status != Erased' deny-list site — the AnyStatus and WritePath "
            + "decisions in JobAdLifecycleReadRegistry are the MAP (this test does not enumerate the "
            + "sites; the registry is their source of truth).\n"
            + "  2. Decide, per #886, whether the new value earns a writer, a distinct invariant, and "
            + "a decision on historical rows — or whether a removed value left a dangling reference.\n"
            + "  3. Only then update ExpectedValues to match the deliberate new closed world.\n\n"
            + "Drift:\n  " + string.Join("\n  ", drift));
    }

    /// <summary>
    /// The declared <see cref="JobAdStatus"/> value set: reflect every <c>public static</c> field of
    /// type <see cref="JobAdStatus"/> and project its <see cref="JobAdStatus.Value"/> string. The
    /// <c>.Value</c> is what <see cref="JobAdStatus.FromValue"/> and the EF value converter round-trip,
    /// so pinning the projected strings transitively pins that no field's persisted value drifted.
    /// </summary>
    private static HashSet<string> DeclaredStatusValues() =>
        typeof(JobAdStatus)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(JobAdStatus))
            .Select(f => ((JobAdStatus)f.GetValue(null)!).Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The pure drift function, factored out of <see cref="The_JobAdStatus_value_set_is_exactly_Active_Archived_Erased"/>
    /// so the crafted-input witnesses below exercise the SAME code the live assertion runs. A pin
    /// proven only against the live (already-conforming) declaration is a pin no FAILING input has
    /// ever shown to have teeth (<c>feedback_fix_for_untested_guarantee_must_itself_be_tested</c>) —
    /// mirrors <c>OrganizationNumberSurfacingGuardTests</c>' and <c>JobAdLifecycleReadRegistryTests</c>'
    /// self-proving-negative idiom.
    /// </summary>
    /// <returns>
    /// One human-readable line per drifted value: an <c>UNDECLARED</c> value present in
    /// <paramref name="declared"/> but not in <paramref name="expected"/> (the ADD case), and a
    /// <c>MISSING</c> value present in <paramref name="expected"/> but not in <paramref name="declared"/>
    /// (the remove/rename case). Empty ⇒ the two sets are identical (the symmetric difference is ∅).
    /// </returns>
    internal static IReadOnlyList<string> UndeclaredOrMissingValues(
        IReadOnlySet<string> declared, IReadOnlySet<string> expected)
    {
        var drift = new List<string>();

        foreach (var extra in declared.Except(expected).OrderBy(v => v, StringComparer.Ordinal))
            drift.Add(
                $"UNDECLARED: '{extra}' — a JobAdStatus value not in the expected closed world. "
                + "Every '!= Erased' deny-list site now silently admits it.");

        foreach (var missing in expected.Except(declared).OrderBy(v => v, StringComparer.Ordinal))
            drift.Add(
                $"MISSING: '{missing}' — an expected JobAdStatus value is no longer declared "
                + "(removed, renamed, or its .Value string drifted). Check for dangling references.");

        return drift;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // SELF-PROVING NEGATIVES — the drift function actually flags drift (and accepts the true set)
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An EXTRA (undeclared) value must be flagged — the ADD case the pin exists for. Without this
    /// crafted witness, the drift function is asserted-but-unproven on the very branch that guards
    /// the deny-list class (the live registry never exercises it, being conforming).
    /// </summary>
    [Fact]
    public void The_drift_function_flags_an_undeclared_value()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal)
            { "Active", "Archived", "Erased", "Suspended" };

        var drift = UndeclaredOrMissingValues(declared, ExpectedValues);

        drift.ShouldContain(v => v.Contains("UNDECLARED") && v.Contains("Suspended"),
            "an undeclared JobAdStatus value must be flagged — this is the ADD case that silently "
            + "widens every '!= Erased' deny-list site.");
    }

    /// <summary>
    /// A MISSING value must be flagged — the remove/rename/drift case. Set identity (not cardinality)
    /// is what makes this catchable: a rename keeps the count at three
    /// (<c>reference_count_only_oracle_needs_asymmetric_seed</c>).
    /// </summary>
    [Fact]
    public void The_drift_function_flags_a_missing_value()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal) { "Active", "Archived" };

        var drift = UndeclaredOrMissingValues(declared, ExpectedValues);

        drift.ShouldContain(v => v.Contains("MISSING") && v.Contains("Erased"),
            "a missing expected value must be flagged — a removed/renamed status must not pass "
            + "silently.");
    }

    /// <summary>
    /// The oracle must be able to say YES: the EXACT declared set drifts nothing. A gate that only
    /// ever rejects proves nothing about the gate that only ever passes — it would also reject the
    /// real registry.
    /// </summary>
    [Fact]
    public void The_drift_function_accepts_the_exact_declared_set()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal)
            { "Active", "Archived", "Erased" };

        UndeclaredOrMissingValues(declared, ExpectedValues).ShouldBeEmpty(
            "the exact closed world must drift nothing — otherwise the pin is a rubber stamp that "
            + "would also reject the live declaration.");
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // FIELD ↔ PARSER CROSS-CHECK — the declaration and FromValue mirror one closed world
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The declared fields and <see cref="JobAdStatus.FromValue"/> must mirror ONE closed world
    /// (DRY — one knowledge piece, one truth). Every declared field round-trips through the parser to
    /// the same instance; an undeclared value — and the retired <c>"Expired"</c> (#886) — is rejected.
    /// Closes the divergence the field-set pin does not catch by itself: a <c>FromValue</c> arm added
    /// without a field, or a field whose <c>.Value</c> the parser does not accept.
    /// </summary>
    [Fact]
    public void FromValue_round_trips_every_declared_field_and_rejects_an_undeclared_value()
    {
        var fields = typeof(JobAdStatus)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(JobAdStatus))
            .Select(f => (JobAdStatus)f.GetValue(null)!)
            .ToList();

        fields.ShouldNotBeEmpty(
            "reflection over JobAdStatus surfaced no value fields — the cross-check would be vacuous.");

        foreach (var status in fields)
        {
            var parsed = JobAdStatus.FromValue(status.Value);

            parsed.IsSuccess.ShouldBeTrue(
                $"FromValue rejected the declared value '{status.Value}'. A declared field the parser "
                + "does not accept cannot round-trip through the DB — the field set and the parser "
                + "have diverged from one closed world.");
            parsed.Value.ShouldBe(status,
                $"FromValue('{status.Value}') did not round-trip to its declared field instance.");
        }

        JobAdStatus.FromValue("Suspended").IsFailure.ShouldBeTrue(
            "FromValue accepted a value no field declares — the parser's accept-set has drifted ahead "
            + "of the declared fields; they must mirror one closed world.");

        JobAdStatus.FromValue("Expired").IsFailure.ShouldBeTrue(
            "the retired 'Expired' must never parse again — #886 retired it (no writer ever produced "
            + "it). A value converter that accepted it would resurrect a state nothing backs.");
    }
}
