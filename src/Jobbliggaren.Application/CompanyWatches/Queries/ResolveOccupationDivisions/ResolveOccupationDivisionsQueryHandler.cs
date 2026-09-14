using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ResolveOccupationDivisions;

/// <summary>
/// Composes the two delivered ports and nothing else: the deriver answers "which occupation groups
/// does this word denote", the profile query answers "where are those groups' employers". No
/// <c>job_ads</c> read here — both ports own their own reads — so nothing is owed to ADR 0113 from
/// this handler.
/// </summary>
public sealed class ResolveOccupationDivisionsQueryHandler(
    IOccupationCodeDeriver deriver,
    IOccupationDivisionProfileQuery profiles)
    : IQueryHandler<ResolveOccupationDivisionsQuery, OccupationDivisionsDto>
{
    public async ValueTask<OccupationDivisionsDto> Handle(
        ResolveOccupationDivisionsQuery query, CancellationToken cancellationToken)
    {
        var word = query.Word.Trim();
        var derived = await deriver.DeriveAsync(word, cancellationToken);
        if (derived.Candidates.Count == 0)
            return new OccupationDivisionsDto(word, []);

        var ids = derived.Candidates
            .Select(c => c.OccupationGroupConceptId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var byId = await profiles.GetDivisionProfilesAsync(ids, cancellationToken);

        var occupations = derived.Candidates
            .Select(c => ToCandidate(c, byId[c.OccupationGroupConceptId]))
            .ToList();
        return new OccupationDivisionsDto(word, occupations);
    }

    private static OccupationDivisionCandidateDto ToCandidate(
        OccupationCandidate candidate, OccupationDivisionProfile profile)
    {
        var state = profile.State switch
        {
            OccupationDivisionProfileState.Profiled => OccupationDivisionCandidateDto.StateProfiled,
            OccupationDivisionProfileState.TooFewAds => OccupationDivisionCandidateDto.StateTooFewAds,
            OccupationDivisionProfileState.NotProfiled => OccupationDivisionCandidateDto.StateNotProfiled,
            _ => throw new InvalidOperationException($"Okänt profiltillstånd: {profile.State}."),
        };

        if (profile.State != OccupationDivisionProfileState.Profiled)
        {
            return new OccupationDivisionCandidateDto(
                candidate.OccupationGroupConceptId, candidate.OccupationGroupLabel, candidate.MatchedOn,
                state, profile.TotalAds, null, null, null, null, null, profile.ProfiledAt);
        }

        var total = profile.TotalAds!.Value;
        var divisions = profile.Divisions!
            .Select(d => new DivisionShareDto(d.DivisionCode, d.AdCount, SharePercent(d.AdCount, total)))
            .ToList();
        var withoutDivision = profile.WithoutDivisionAdCount!.Value;
        var belowThreshold = total - divisions.Sum(d => d.AdCount) - withoutDivision;

        return new OccupationDivisionCandidateDto(
            candidate.OccupationGroupConceptId, candidate.OccupationGroupLabel, candidate.MatchedOn,
            state, total, divisions,
            belowThreshold, SharePercent(belowThreshold, total),
            withoutDivision, SharePercent(withoutDivision, total),
            profile.ProfiledAt);
    }

    /// <summary>Whole percent, half away from zero — the one rounding every renderer inherits.</summary>
    private static int SharePercent(int part, int total) =>
        (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);
}
