using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1682 — the read side behind <see cref="IOccupationDivisionProfileQuery"/>. Applies the two
/// product thresholds — the share cut and the derived floor — at read time over the store's full
/// measurement, and maps the storage sentinels to the typed <c>NotInRegisterAdCount</c> scalar so
/// no bare string crosses the Application boundary (§5). The age gate runs first and in SQL, so an
/// over-age profile costs no scan.
/// </summary>
internal sealed class OccupationDivisionProfileQuery(
    OccupationDivisionProfileStore store,
    IDateTimeProvider clock,
    IOptions<OccupationDivisionProfileOptions> options) : IOccupationDivisionProfileQuery
{
    public async ValueTask<IReadOnlyDictionary<string, OccupationDivisionProfile>> GetDivisionProfilesAsync(
        IReadOnlyList<string> occupationGroupConceptIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occupationGroupConceptIds);

        var opts = options.Value;
        var notBefore = clock.UtcNow.AddHours(-opts.MaxReadAgeHours);
        var read = await store.ReadAsync(occupationGroupConceptIds, notBefore, cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<string, OccupationDivisionProfile>(StringComparer.Ordinal);

        if (read.ProfiledAt is not { } profiledAt)
        {
            foreach (var id in occupationGroupConceptIds)
                result[id] = OccupationDivisionProfile.NotProfiled;
            return result;
        }

        var byGroup = read.Rows
            .GroupBy(r => r.OccupationGroupConceptId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var id in occupationGroupConceptIds)
        {
            var rows = byGroup.GetValueOrDefault(id) ?? [];
            var total = rows.Sum(r => r.AdCount);

            if (total < opts.MinimumAdsPerGroup)
            {
                result[id] = OccupationDivisionProfile.TooFewAds(total, profiledAt);
                continue;
            }

            // Both sentinels are "the employer gave us no huvudgrupp"; the reader sees one honest line.
            var notInRegister = rows
                .Where(r => IsSentinel(r.DivisionCode))
                .Sum(r => r.AdCount);

            // Integer arithmetic, no rounding step: AdCount * 100 >= percent * total is the exact
            // predicate "share >= p %", and it cannot admit a division by a rounding artefact.
            var divisions = rows
                .Where(r => !IsSentinel(r.DivisionCode) && r.AdCount * 100L >= (long)opts.MinimumSharePercent * total)
                .OrderByDescending(r => r.AdCount)
                .ThenBy(r => r.DivisionCode, StringComparer.Ordinal)
                .Select(r => new OccupationDivisionShare(r.DivisionCode, r.AdCount))
                .ToList();

            result[id] = OccupationDivisionProfile.Profiled(total, divisions, notInRegister, profiledAt);
        }

        return result;
    }

    private static bool IsSentinel(string divisionCode) =>
        divisionCode is OccupationDivisionProfileRow.NotInRegisterCode
            or OccupationDivisionProfileRow.NoSniCode;
}
