using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

// #359 / ADR 0084 — same-field constraint on the committed
// occupation-substitutability.json artifact. Every substitutability edge MUST be
// WITHIN one OccupationField: occupationField(source) == occupationField(related),
// where occupationField(ssyk-4 group) is derived from the committed
// taxonomy-snapshot.json (each ssyk-4 group belongs to exactly one field, 1:1).
//
// TEST-FIRST (Red): the committed artifact still carries cross-field edges
// (the reported handläggare bridge Data/IT → Administration). The structural test
// below is RED today and goes GREEN after the artifact is regenerated with the
// same-field filter. The test only reads the embedded artifacts via the existing
// internal loaders (TaxonomySnapshotSeeder.LoadSnapshot / .LoadSubstitutability,
// InternalsVisibleTo: Jobbliggaren.Application.UnitTests) — it never touches the
// generator or the artifact.
public class OccupationSubstitutabilitySameFieldTests
{
    // Reported defect (#359): a Data/IT source must not bridge to a handläggare
    // (Administration, ekonomi, juridik) group.
    private const string MjukvaruSystemutvecklareSsyk4 = "DJh5_yyF_hEM"; // Data/IT
    private const string PlanerareUtredareSsyk4 = "vPP6_rsw_dck";        // Administration…
    private const string SystemanalytikerSsyk4 = "UXKZ_3zZ_ipB";         // Data/IT

    // ssyk-4 conceptId → OccupationField conceptId, derived from the committed
    // snapshot. Built once: occupationFields[*].occupationGroups[*] is the ssyk-4
    // layer, and each group belongs to exactly one field (verified 1:1 below).
    private static Dictionary<string, string> BuildSsyk4ToField()
    {
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in snapshot.OccupationFields)
        {
            foreach (var group in field.OccupationGroups ?? [])
            {
                // 1:1 guard — a ssyk-4 group mapping to two fields would corrupt
                // the same-field verdict, so fail loud rather than silently last-wins.
                map.ContainsKey(group.ConceptId).ShouldBeFalse(
                    $"ssyk-4 {group.ConceptId} belongs to more than one OccupationField "
                    + $"({map.GetValueOrDefault(group.ConceptId)} and {field.ConceptId})");
                map[group.ConceptId] = field.ConceptId;
            }
        }

        return map;
    }

    private static Dictionary<string, string> BuildSsyk4ToFieldLabel()
    {
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in snapshot.OccupationFields)
        {
            foreach (var group in field.OccupationGroups ?? [])
            {
                map[group.ConceptId] = field.Label;
            }
        }

        return map;
    }

    private static Dictionary<string, string> BuildSsyk4ToGroupLabel()
    {
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in snapshot.OccupationFields)
        {
            foreach (var group in field.OccupationGroups ?? [])
            {
                map[group.ConceptId] = group.Label;
            }
        }

        return map;
    }

    // ── 1. Structural invariant (the load-bearing one) ──────────────────────
    [Fact]
    public void CommittedArtifact_ShouldContainOnlyWithinFieldEdges_WhenEveryEdgeIsChecked()
    {
        var ssyk4ToField = BuildSsyk4ToField();
        var fieldLabel = BuildSsyk4ToFieldLabel();
        var groupLabel = BuildSsyk4ToGroupLabel();
        var file = TaxonomySnapshotSeeder.LoadSubstitutability();

        // Every edge endpoint must be a known ssyk-4 group (else the field verdict
        // is undefined). A regeneration that emits an unknown id is a separate
        // regression we want surfaced rather than silently skipped.
        string Describe(string conceptId) =>
            $"{conceptId} ({groupLabel.GetValueOrDefault(conceptId, "<unknown group>")}"
            + $" / field={fieldLabel.GetValueOrDefault(conceptId, "<unknown field>")})";

        var crossFieldEdges = file.Relations
            .SelectMany(r => r.RelatedConceptIds.Select(related =>
                (Source: r.SourceConceptId, Related: related)))
            .Where(e =>
            {
                var hasSource = ssyk4ToField.TryGetValue(e.Source, out var sourceField);
                var hasRelated = ssyk4ToField.TryGetValue(e.Related, out var relatedField);
                // Unknown endpoints count as offending (diagnosable below) — a same-
                // field artifact has every endpoint resolvable and field-equal.
                return !hasSource || !hasRelated || sourceField != relatedField;
            })
            .Select(e => $"{Describe(e.Source)}  ->  {Describe(e.Related)}")
            .ToList();

        crossFieldEdges.ShouldBeEmpty(
            $"Found {crossFieldEdges.Count} cross-field substitutability edge(s) "
            + "(#359 / ADR 0084 violation). Every edge must stay within one "
            + "OccupationField. Offending pairs:\n"
            + string.Join("\n", crossFieldEdges));
    }

    // ── 2. Named regression — the reported defect MUST be absent ─────────────
    [Fact]
    public void CommittedArtifact_ShouldNotBridgeMjukvaruToHandlaggare_WhenEdgesAreInspected()
    {
        // The exact handläggare bridge Klas reported: Data/IT (Mjukvaru- och
        // systemutvecklare) must NOT relate to Administration (Planerare och
        // utredare = handläggare).
        var file = TaxonomySnapshotSeeder.LoadSubstitutability();

        var relatedOfMjukvaru = file.Relations
            .Where(r => r.SourceConceptId == MjukvaruSystemutvecklareSsyk4)
            .SelectMany(r => r.RelatedConceptIds)
            .ToList();

        relatedOfMjukvaru.ShouldNotContain(PlanerareUtredareSsyk4,
            $"{MjukvaruSystemutvecklareSsyk4} (Data/IT) must not bridge to "
            + $"{PlanerareUtredareSsyk4} (handläggare, Administration) — #359.");
    }

    // ── 3. Named positive — a within-field edge MUST be present (non-vacuity) ─
    [Fact]
    public void CommittedArtifact_ShouldKeepMjukvaruToSystemanalytiker_WhenEdgesAreInspected()
    {
        // Non-vacuity guard: the same-field filter must KEEP legitimate within-field
        // edges, so the artifact is not simply emptied. Both ends are Data/IT.
        var file = TaxonomySnapshotSeeder.LoadSubstitutability();

        var relatedOfMjukvaru = file.Relations
            .Where(r => r.SourceConceptId == MjukvaruSystemutvecklareSsyk4)
            .SelectMany(r => r.RelatedConceptIds)
            .ToList();

        relatedOfMjukvaru.ShouldContain(SystemanalytikerSsyk4,
            $"{MjukvaruSystemutvecklareSsyk4} (Data/IT) must keep its within-field "
            + $"edge to {SystemanalytikerSsyk4} (Systemanalytiker, Data/IT) — the "
            + "filter must not strip legitimate same-field relations.");
    }
}
