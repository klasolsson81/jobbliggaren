using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Publication;

/// <summary>
/// The SQL form of the publication predicate agrees with <see cref="JobAdPublication.Classify"/> in every state (ADR 0160
/// D2, ADR 0174). The two share no code, so this matrix is what keeps them one rule; the SQL translation of the same
/// rule is pinned on Postgres by <c>ListedJobAdSnapshotSamplerTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every reachable cell is built the way production builds it. An imported ad is <c>JobAd.Import</c>; a never-imported
/// one is <c>JobAd.Create</c> with the user-settable <c>Source = Platsbanken</c>, which is what
/// <c>POST /api/v1/job-ads</c> stores. An imported ad is archived by <c>ArchiveExternalJobAdCommand</c> and the
/// snapshot-miss tracker whatever its deadline, and by <c>ExpireJobAdsJob</c> past it. A never-imported ad is archived
/// only by <c>ExpireJobAdsJob</c>, past its deadline. <c>EraseRecruiterAdsCommand</c> erases either, since its match
/// has no provenance filter. A deadline before, at or after now is the clock's doing.
/// </para>
/// <para>
/// Two kinds of state no path in <c>src/</c> produces are declared, and only their exclusion is asserted. One is a
/// never-imported ad archived with no deadline, or one not yet past. The other is an import from any source but
/// Platsbanken, because the only registered job source is Platsbanken.
/// </para>
/// </remarks>
public class JobAdPublicationIsListedAgreementTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly DateTimeOffset Now = Clock.UtcNow;

    public enum Provenance
    {
        Imported,
        NeverImported,
    }

    public enum Deadline
    {
        None,
        Before,
        At,
        After,
    }

    private static readonly string[] Statuses = ["Active", "Archived", "Erased"];

    public static TheoryData<Provenance, string, Deadline> ReachableCells()
    {
        var data = new TheoryData<Provenance, string, Deadline>();
        foreach (var (provenance, status, deadline) in Reachable())
            data.Add(provenance, status, deadline);
        return data;
    }

    private static IEnumerable<(Provenance Provenance, string Status, Deadline Deadline)> Reachable() =>
        from provenance in Enum.GetValues<Provenance>()
        from status in Statuses
        from deadline in Enum.GetValues<Deadline>()
        where IsReachable(provenance, status, deadline)
        select (provenance, status, deadline);

    public static TheoryData<Deadline> DeadlinesOfAnArchivedNeverImportedAdThatNoPathProduces() =>
        new(Deadline.None, Deadline.At, Deadline.After);

    [Theory]
    [MemberData(nameof(ReachableCells))]
    public void IsListed_AgreesWithClassify_InEveryReachableState(Provenance provenance, string status, Deadline deadline)
    {
        var ad = Build(provenance, status, deadline);

        Listed(ad).ShouldBe(
            JobAdPublication.Classify(ad.Status, ad.ExpiresAt, ad.External?.Source, Now) == JobAdPublicationState.Listed,
            $"{provenance} × {status} × {deadline}");
    }

    [Fact]
    public void TheMatrix_ListsExactlyTheImportedActiveAdsWhoseDeadlineHasNotPassed_AndReachesEveryState()
    {
        var listed = new List<(Provenance, string, Deadline)>();
        var states = new HashSet<JobAdPublicationState>();
        foreach (var (provenance, status, deadline) in Reachable())
        {
            var ad = Build(provenance, status, deadline);
            states.Add(JobAdPublication.Classify(ad.Status, ad.ExpiresAt, ad.External?.Source, Now));
            if (Listed(ad))
                listed.Add((provenance, status, deadline));
        }

        listed.ShouldBe(
        [
            (Provenance.Imported, "Active", Deadline.None),
            (Provenance.Imported, "Active", Deadline.At),
            (Provenance.Imported, "Active", Deadline.After),
        ], ignoreOrder: true);
        states.ShouldBe(Enum.GetValues<JobAdPublicationState>(), ignoreOrder: true,
            "a matrix that never produced one of the states could not tell the two forms apart there.");
    }

    [Fact]
    public void TheMatrix_CoversEveryDeclaredStatus()
    {
        var declared = typeof(JobAdStatus).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(JobAdStatus))
            .Select(f => ((JobAdStatus)f.GetValue(null)!).Value);

        declared.ShouldBe(Statuses, ignoreOrder: true,
            "a new status needs its producers named and its cells added before the two forms can be said to agree on it.");
    }

    [Theory]
    [MemberData(nameof(DeadlinesOfAnArchivedNeverImportedAdThatNoPathProduces))]
    public void Unreachable_AnArchivedNeverImportedAdWhoseDeadlineHasNotPassed_IsExcludedByBothForms(Deadline deadline)
    {
        // Built through Archive to see that both forms still exclude it if a future writer ever produced it.
        var ad = NeverImported(DeadlineOf(deadline));
        ad.Archive(Clock).IsSuccess.ShouldBeTrue();

        Listed(ad).ShouldBeFalse();
        JobAdPublication.Classify(ad.Status, ad.ExpiresAt, ad.External?.Source, Now)
            .ShouldNotBe(JobAdPublicationState.Listed);
    }

    [Theory]
    [InlineData(Deadline.None)]
    [InlineData(Deadline.At)]
    [InlineData(Deadline.After)]
    public void Unreachable_AnActiveAdImportedFromAnotherSource_IsExcludedByBothForms(Deadline deadline)
    {
        var ad = Imported(JobSource.LinkedIn, DeadlineOf(deadline));

        Listed(ad).ShouldBeFalse();
        JobAdPublication.Classify(ad.Status, ad.ExpiresAt, ad.External?.Source, Now)
            .ShouldBe(JobAdPublicationState.NotPublic);
    }

    private static bool Listed(JobAd ad) => JobAdPublication.IsListed(Now).Compile()(ad);

    private static bool IsReachable(Provenance provenance, string status, Deadline deadline) =>
        !(provenance == Provenance.NeverImported && status == "Archived" && deadline != Deadline.Before);

    private static JobAd Build(Provenance provenance, string status, Deadline deadline)
    {
        var expiresAt = DeadlineOf(deadline);
        var ad = provenance == Provenance.Imported ? Imported(JobSource.Platsbanken, expiresAt) : NeverImported(expiresAt);

        switch (status)
        {
            case "Archived":
                ad.Archive(Clock).IsSuccess.ShouldBeTrue();
                break;
            case "Erased":
                ad.Erase(Clock).IsSuccess.ShouldBeTrue();
                break;
        }

        ad.Status.Value.ShouldBe(status);
        return ad;
    }

    private static DateTimeOffset? DeadlineOf(Deadline deadline) => deadline switch
    {
        Deadline.None => null,
        Deadline.Before => Now.AddTicks(-1),
        Deadline.At => Now,
        Deadline.After => Now.AddTicks(1),
        _ => throw new ArgumentOutOfRangeException(nameof(deadline)),
    };

    private static JobAd Imported(JobSource source, DateTimeOffset? expiresAt)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(source, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: Now.AddDays(-10),
            expiresAt: expiresAt,
            clock: Clock,
            extractTerms: TestKeywordExtraction.None).Value;
    }

    private static JobAd NeverImported(DateTimeOffset? expiresAt) =>
        JobAd.Create(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare.",
            url: "https://example.com/annons",
            source: JobSource.Platsbanken,
            publishedAt: Now.AddDays(-10),
            expiresAt: expiresAt,
            clock: Clock).Value;
}
