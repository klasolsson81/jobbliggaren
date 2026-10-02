using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.JobSeekers.Commands;

/// <summary>
/// Wire-shape for one per-occupation experience overlay entry (ADR 0079-amendment). An Application input record (not the Domain
/// <c>OccupationExperience</c> VO — the Domain type never crosses the API boundary, CLAUDE.md §2.3);
/// the handlers map it to the VO so <c>MatchPreferences.Create</c> enforces the
/// cap/format/distinct/range/subset invariants. <see cref="Years"/> is nullable: null = "not stated".
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OccupationExperienceInput(string ConceptId, int? Years);
