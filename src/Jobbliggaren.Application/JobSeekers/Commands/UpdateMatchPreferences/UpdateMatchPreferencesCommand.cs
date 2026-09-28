using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMatchPreferences;

/// <summary>
/// Writes the current user's match preferences one part at a time (#1918, ADR 0147). A present
/// part replaces that whole part; an absent part is left as stored. At least one part is required.
/// Replayed on a concurrency conflict (ADR 0146): the handler re-reads the row and lays only this
/// request's parts over it, so two tabs writing two different parts both land.
/// <para>
/// Every member of a present part is required on the wire (<c>JsonRequired</c>, plus a non-null
/// rule in the validator, since an explicit <c>null</c> passes <c>JsonRequired</c>). The one
/// exception is <see cref="OccupationsPartInput.PreferredOccupationExperience"/>: absent keeps the
/// stated years of every occupation still chosen, <c>[]</c> clears them.
/// </para>
/// </summary>
public sealed record UpdateMatchPreferencesCommand(
    OccupationsPartInput? Occupations = null,
    SkillsPartInput? Skills = null,
    LocationsPartInput? Locations = null,
    EmploymentTypesPartInput? EmploymentTypes = null,
    ExperiencePartInput? Experience = null)
    : ICommand<Result>, IAuthenticatedRequest, IReplayOnConcurrencyConflict;

public sealed record OccupationsPartInput(
    [property: JsonRequired] IReadOnlyList<string> PreferredOccupationGroups,
    IReadOnlyList<OccupationExperienceInput>? PreferredOccupationExperience = null);

public sealed record SkillsPartInput(
    [property: JsonRequired] IReadOnlyList<string> PreferredSkills);

/// <summary>Regions, municipalities and distans are one part: they fold into one "ort" dimension.</summary>
public sealed record LocationsPartInput(
    [property: JsonRequired] IReadOnlyList<string> PreferredRegions,
    [property: JsonRequired] IReadOnlyList<string> PreferredMunicipalities,
    [property: JsonRequired] bool PreferredRemote);

public sealed record EmploymentTypesPartInput(
    [property: JsonRequired] IReadOnlyList<string> PreferredEmploymentTypes);

/// <summary><c>null</c> clears the stated years; the member itself must be present.</summary>
public sealed record ExperiencePartInput(
    [property: JsonRequired] int? ExperienceYears);
