using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.SearchSkills;

/// <summary>
/// ADR 0079 STEG 3 PR-C — skill typeahead for the editable skill chips' "add" affordance.
/// The skill taxonomy is a flat ~20,679-concept vocabulary with no browsable hierarchy
/// (unlike occupations, whose tree is fetched whole and filtered client-side), so the FE
/// searches server-side per keystroke. Reference data (non-PII taxonomy labels) — mirrors
/// the <c>GetTaxonomyTreeQuery</c> shape (thin query, no auth requirement on the query
/// itself; the endpoint is rate-limited). A blank/too-short query returns an empty list
/// (graceful typeahead — never a 400 mid-typing).
/// <para>
/// #277 — the result is GROUPED: a search for "C#" yields ONE addable
/// <see cref="SkillOptionGroupDto"/> carrying BOTH the ESCO + AF member concept-ids (a
/// singleton carries one member), so the user never sees twin duplicates and selecting one
/// chip confirms both ids on the full-replace save.
/// </para>
/// </summary>
public sealed record SearchSkillsQuery(string Query) : IQuery<IReadOnlyList<SkillOptionGroupDto>>;
