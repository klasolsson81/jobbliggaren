using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Infrastructure.Taxonomy;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// #2199 PR 2b — the capture gate as production composes it: the real parser and the religious occupations over
/// the committed artefacts. Both are pure and deterministic, and a substitute would let a test agree with itself
/// rather than with production; every id a test calls religious is then one the real adapter singles out, which
/// <c>ReligiousOccupationsArtefactTests</c> pins.
/// </summary>
internal static class RealRecentSearchCaptureGate
{
    private static readonly Lazy<RecentSearchCaptureGate> Gate = new(() => new RecentSearchCaptureGate(
        new SearchQueryParser(),
        new ReligiousOccupations(OccupationNameLineageLoader.Load(), TaxonomySnapshotSeeder.LoadSnapshot())));

    public static RecentSearchCaptureGate Instance => Gate.Value;
}
