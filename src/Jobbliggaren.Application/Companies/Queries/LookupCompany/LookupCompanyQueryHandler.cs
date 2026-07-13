using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Companies.Queries.LookupCompany;

/// <summary>
/// #454 (ADR 0088 D4/D5) — the lookup flow, in guard order:
/// <list type="number">
/// <item>VO format gate (<see cref="OrganizationNumber.Create"/> — Result passthrough);</item>
/// <item><b>refuse-posture (D4, security-bound):</b> a personnummer-shaped org.nr returns a
/// Validation-class failure BEFORE any outbound call — never transmitted to a registry, never
/// cached, never surfaced. The refusal copy is NEUTRAL and never echoes the typed value back
/// (a potential personnummer must not be reflected into a response payload or log). This is THE
/// single policy point a #456-sanctioned posture flip would edit (Klas product directive
/// 2026-07-02: enskild-firma searchability is the DPIA #456 headline question);</item>
/// <item>registry lookup via the (cache-decorated) port — <c>Unavailable</c>/<c>NotFound</c> map to
/// 200-with-status, NEVER a throw (civic degradation, parity <c>RedisLandingStatsCache</c>);</item>
/// <item>on <c>Found</c>: enrich with our own bounded projections — active-ad count (#447 idiom),
/// SSYK-gated matching count (#452 idiom, null = not-assessed), and the user's own follow state
/// (owner-scoped surrogate id only).</item>
/// </list>
/// Reads PUBLIC registry/corpus data + the CURRENT user's own profile/watch — no cross-user surface
/// (ADR 0087 D8). <b>Never logs the org.nr</b> (org.nr surfacing-guard log-scan pins this file).
/// </summary>
public sealed class LookupCompanyQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICompanyRegistry registry,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch)
    : IQueryHandler<LookupCompanyQuery, Result<CompanyLookupDto>>
{
    // #452 parity — "matchande annonser" = grade >= Good in the Fast band (Fast==Full oracle for a
    // >= Good COUNT, ADR 0087 D5-tillägg; see ListCompanyWatchesQueryHandler.MatchingGrades).
    private static readonly IReadOnlyList<MatchGrade> MatchingGrades =
        [MatchGrade.Good, MatchGrade.Strong];

    public async ValueTask<Result<CompanyLookupDto>> Handle(
        LookupCompanyQuery query, CancellationToken cancellationToken)
    {
        var orgNrResult = OrganizationNumber.Create(query.OrganizationNumber);
        if (orgNrResult.IsFailure)
            return Result.Failure<CompanyLookupDto>(orgNrResult.Error);

        var orgNr = orgNrResult.Value;

        // ADR 0088 D4 (security-bound Posture A) — refuse BEFORE the port is invoked. Pinned by the
        // transmission-fail-closed test: the registry must never receive a personnummer-shaped value.
        // Copy is neutral and does NOT echo the typed value (no reflection of a potential
        // personnummer); "för närvarande" keeps it honest — the posture is a #456-gated decision,
        // not a permanent product truth.
        if (orgNr.IsPersonnummerShaped())
        {
            return Result.Failure<CompanyLookupDto>(DomainError.Validation(
                "CompanyLookup.ProtectedIdentity",
                "Numret kan vara ett personnummer och kan därför inte slås upp för närvarande."));
        }

        var lookup = await registry.LookupAsync(orgNr, cancellationToken);

        if (lookup.Status == CompanyRegistryStatus.Unavailable)
            return Result.Success(CompanyLookupDto.Empty(CompanyLookupDto.StatusUnavailable));

        if (lookup.Status == CompanyRegistryStatus.NotFound || lookup.Entry is null)
            return Result.Success(CompanyLookupDto.Empty(CompanyLookupDto.StatusNotFound));

        // ---- Found: enrich with our own bounded, org.nr-keyed projections. ----

        // #447 idiom — public open-role count for THIS org.nr (STORED generated shadow column;
        // the Status == Active predicate below IS the whole exclusion — JobAd has no soft-delete
        // axis and no query filter, #821). Bounded single-key aggregate.
        var orgNrValue = (string?)orgNr.Value;
        var activeAdCount = await db.JobAds
            .AsNoTracking()
            .CountAsync(
                j => EF.Property<string?>(j, "OrganizationNumber") == orgNrValue
                     && j.Status == JobAdStatus.Active,
                cancellationToken);

        // #452 idiom — the current user's >= Good matching count, SSYK-gated: no stated occupation
        // => null (not-assessed, the FE renders the nudge), never a misleading hard 0. Reads only
        // PUBLIC job_ads + the user's OWN Fast profile (ICurrentUser-scoped) — no cross-user JOIN.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        int? matchingAdCount = null;
        if (profile.Fast.SsykGroupConceptIds.Count > 0)
        {
            var matchingByOrgNr = await perUserSearch.CountPerUserByEmployerAsync(
                [orgNr.Value], profile, MatchingGrades, cancellationToken);
            matchingAdCount = matchingByOrgNr.GetValueOrDefault(orgNr.Value);
        }

        // Follow state — the user's own active watch of this org.nr (the soft-delete query filter
        // hides unfollowed rows). Owner-scoped; only the surrogate id is surfaced (parity the follow
        // endpoints — an org.nr never doubles as a wire id).
        Guid? companyWatchId = null;
        if (currentUser.UserId is { } userId)
        {
            var watch = await db.CompanyWatches
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.UserId == userId && w.OrganizationNumber == orgNr, cancellationToken);
            companyWatchId = watch?.Id.Value;
        }

        // D8(c) mask+flag at the surfacing boundary (verbatim DisambiguateEmployersQueryHandler
        // idiom). Under the D4 refuse-posture isProtected is always false here — kept as
        // defense-in-depth so no future code path can surface a raw personnummer-shaped value.
        var isProtected = orgNr.IsPersonnummerShaped();
        return Result.Success(new CompanyLookupDto(
            Status: CompanyLookupDto.StatusFound,
            OrganizationNumber: isProtected ? null : lookup.Entry.OrganizationNumber,
            IsProtectedIdentity: isProtected,
            CompanyName: isProtected ? null : lookup.Entry.Name,
            ActiveAdCount: activeAdCount,
            MatchingAdCount: matchingAdCount,
            CompanyWatchId: companyWatchId));
    }
}
