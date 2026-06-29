using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;

/// <summary>
/// ADR 0079 STEG 3 PR-C — reverse-lookup (concept-id → canonical label) for the saved
/// skill chips' cold-load display, the skill analog of the occupation
/// <c>ResolveTaxonomyLabels</c> (ADR 0043). The flat ~20k-concept skill vocabulary is
/// never shipped to the FE as a tree, so a settings page that pre-fills the user's stored
/// <c>PreferredSkills</c> concept-ids resolves their labels via this query instead of
/// rendering opaque ids. Unknown/removed ids are dropped silently (graceful). Reference
/// data (non-PII taxonomy labels) — thin query, no auth requirement on the query itself
/// (the endpoint is auth-gated + rate-limited).
/// <para>
/// #277 — the result is GROUPED by shared exact-label surface: a saved twin-pair (the ESCO + AF
/// "C#" ids both in the user's PreferredSkills) renders as ONE chip carrying both member ids on
/// cold load (a singleton concept carries one member). No saved id is ever dropped.
/// </para>
/// </summary>
public sealed record ResolveSkillLabelsQuery(IReadOnlyList<string> ConceptIds)
    : IQuery<IReadOnlyList<SkillOptionGroupDto>>;
