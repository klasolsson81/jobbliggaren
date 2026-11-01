using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.AddPreferredSkills;

/// <summary>
/// #2247 — adds one skill to the current user's confirmed skill set (<c>MatchPreferences.PreferredSkills</c>)
/// without replacing the rest: the job card's "Lägg till" on a skill the ad asks for and the profile lacks.
/// <para>
/// <see cref="ConceptIds"/> is ONE chip's concept group (#277): every member id, so the skill is stored the
/// way the settings card stores a confirmed group. Ids already stored are skipped, so a repeated click is a
/// no-op success.
/// </para>
/// <para>
/// A union, not a replace, because the card never holds the stored set: a read-then-PATCH from it would
/// let two quick clicks overwrite each other. Replayed on a concurrency conflict (ADR 0146), each replay
/// re-reads the row, so concurrent adds both land.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddPreferredSkillsCommand([property: JsonRequired] IReadOnlyList<string> ConceptIds)
    : ICommand<Result>, IAuthenticatedRequest, IReplayOnConcurrencyConflict;
