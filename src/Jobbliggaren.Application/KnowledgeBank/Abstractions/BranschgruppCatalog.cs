namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// The committed, versioned occupation-field → branschgrupp map plus each branschgrupp's
/// section rule-table (Fas 4b 8b.4a, Asset A — <c>ssyk-branschgrupp.v1.json</c>). Immutable
/// contract; the Swedish-token <c>*File</c> deserialisation form stays in Infrastructure
/// (parity <see cref="Rubric"/>/<see cref="FrameCatalog"/>).
/// <para>
/// The chain this sits in the middle of: the user's CONFIRMED occupation choice
/// (<c>MatchPreferences.PreferredOccupationGroups</c>, ssyk-4 groups) → the taxonomy's
/// group→field parent edge → <b>this map</b> → the section rules the Slutför guide renders.
/// </para>
/// </summary>
public sealed record BranschgruppCatalog(
    string Version,
    IReadOnlyDictionary<string, string> BranschgruppByOccupationField,
    IReadOnlyDictionary<string, BranschgruppRules> RulesById)
{
    /// <summary>
    /// The branschgrupp every unmapped/ambiguous occupation is honestly reported as. NOT a
    /// hole: it is the 62.1 % majority experience (measured against 93 469 Platsbanken ads)
    /// and carries its own first-class suggestions.
    /// </summary>
    public const string Fallback = "ovriga";

    /// <summary>
    /// Resolves a user's occupation-FIELD concept-ids to a single branschgrupp.
    /// <para>
    /// Tie-break (senior-cto-advisor bind, 2026-07-13): the confirmed fields resolve to exactly
    /// ONE non-<see cref="Fallback"/> branschgrupp → that one; anything else → <see cref="Fallback"/>.
    /// So a user who states both an IT and a vård occupation gets the generic row, never a coin
    /// flip: two rule-tables that contradict each other cannot be silently merged, and guessing
    /// which one she "really" meant is exactly the mis-suggestion the Övriga row exists to avoid.
    /// An empty input is a legitimate caller state (no stated occupation) → <see cref="Fallback"/>.
    /// </para>
    /// </summary>
    public string ResolveBranschgrupp(IReadOnlyCollection<string> occupationFieldConceptIds)
    {
        ArgumentNullException.ThrowIfNull(occupationFieldConceptIds);

        var named = occupationFieldConceptIds
            .Select(id => BranschgruppByOccupationField.TryGetValue(id, out var g) ? g : Fallback)
            .Where(g => !string.Equals(g, Fallback, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(2)   // one is enough to decide; two is enough to refuse
            .ToList();

        return named.Count == 1 ? named[0] : Fallback;
    }

    /// <summary>The rule-table for a branschgrupp id. The loader guarantees every id the field
    /// map can yield (including <see cref="Fallback"/>) has one — an unknown id is a bug, not a
    /// user state, so this throws rather than degrading to an empty table.</summary>
    public BranschgruppRules RulesFor(string branschgruppId) =>
        RulesById.TryGetValue(branschgruppId, out var rules)
            ? rules
            : throw new InvalidOperationException(
                $"Branschgrupp '{branschgruppId}' saknar regeltabell i ssyk-branschgrupp-assetet.");
}

/// <summary>
/// One branschgrupp's section rule-table (design handoff §7).
/// </summary>
/// <param name="Id">Stable slug (<c>it</c>/<c>vard</c>/<c>skola</c>/<c>ovriga</c>). The FE keys
/// its i18n on this — never on a label in the payload.</param>
/// <param name="Rationale">The badge copy ("Vanligt inom vård och omsorg"). KB-sourced Swedish,
/// parity <c>ProposedChange.Rationale</c> — never prose the engine synthesised.</param>
/// <param name="StandardSections">Sections this occupation is EXPECTED to have — the handoff's
/// "Extra standardsektioner". Surfaced ahead of the merely-suggested ones.</param>
/// <param name="SuggestedSections">Sections that are common but optional — "Föreslås i Lägg till
/// sektion".</param>
/// <remarks>
/// There is deliberately no "suppressed sections" member, though the handoff's §7 table has a
/// "Visas ej som förslag" column. Nothing is inherited from a base set — each branschgrupp lists
/// its own suggestions outright — so a subtraction could never subtract anything. Vård's "not
/// Projekt" is expressed by Projekt simply not being in its list. A field whose only possible
/// outcomes are "no effect" or "crash at startup" is a trap for the next editor, not a guard; the
/// first draft of this type had one, and mutation testing exposed it (deleting the filter that
/// read it broke no test, because it could never fire).
/// <para>
/// This is only about what the engine OFFERS. Handoff rule (a) is untouched: a section the user
/// wrote herself is always kept and shown. The engine never hides her content.
/// </para>
/// </remarks>
public sealed record BranschgruppRules(
    string Id,
    string Rationale,
    IReadOnlyList<SectionRecommendation> StandardSections,
    IReadOnlyList<SectionRecommendation> SuggestedSections);

/// <summary>
/// One recommendable section: the lexicon's canonical <paramref name="SectionId"/> plus the
/// Swedish <paramref name="Heading"/> that is written into the CV when the user accepts it.
/// <para>
/// The heading is document CONTENT, not a UI label — which is why it comes from the asset and
/// not from <c>messages/sv.json</c>. It MUST be a heading the parsing lexicon can resolve back
/// to <paramref name="SectionId"/>: a heading the segmenter cannot see is a section whose body
/// is swallowed by the preceding one on the next import (the live #815 bug PR-1 fixed). The
/// provider enforces this at startup; the contract test pins it.
/// </para>
/// </summary>
public sealed record SectionRecommendation(string SectionId, string Heading);
