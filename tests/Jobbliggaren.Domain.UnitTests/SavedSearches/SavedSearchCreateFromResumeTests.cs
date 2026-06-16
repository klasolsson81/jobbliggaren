using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Domain.SavedSearches.Events;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.SavedSearches;

// Fas 4 STEG B — SavedSearch.CreateFromResume: identical to Create plus a derived-from-CV
// provenance event (no stored DerivedFromResumeId — ADR 0040 Beslut 3, event-only, no migration;
// parity Resume.CreateFromParsed). The confirmed ssyk-4 ids are plain client input on the
// criteria, never the deriver result (bearing invariant).
public class SavedSearchCreateFromResumeTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId Owner = new(Guid.NewGuid());

    private static SearchCriteria ConfirmedCriteria(params string[] occupationGroups) =>
        SearchCriteria.Create(
            occupationGroup: occupationGroups.Length == 0 ? ["grp_12345"] : occupationGroups,
            municipality: null, region: null, employmentType: null, worktimeExtent: null,
            q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value;

    [Fact]
    public void CreateFromResume_WithConfirmedOccupations_ReturnsSuccess_WithThoseIds()
    {
        var result = SavedSearch.CreateFromResume(
            Owner, "Systemutvecklare i Stockholm", ConfirmedCriteria("grp_aaa", "grp_bbb"),
            notificationEnabled: true, sourceParsedResumeId: Guid.NewGuid(), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobSeekerId.ShouldBe(Owner);
        result.Value.NotificationEnabled.ShouldBeTrue();
        result.Value.Criteria.OccupationGroup.ShouldBe(["grp_aaa", "grp_bbb"]);
    }

    [Fact]
    public void CreateFromResume_RaisesBothCreatedAndProvenanceEvents()
    {
        var sourceId = Guid.NewGuid();

        var result = SavedSearch.CreateFromResume(Owner, "CV-sök", ConfirmedCriteria(), false, sourceId, Clock);

        result.Value.DomainEvents.OfType<SavedSearchCreatedDomainEvent>().ShouldHaveSingleItem();
        var provenance = result.Value.DomainEvents.OfType<SavedSearchDerivedFromResumeDomainEvent>()
            .ShouldHaveSingleItem();
        provenance.SavedSearchId.ShouldBe(result.Value.Id);
        provenance.JobSeekerId.ShouldBe(Owner);
        provenance.SourceParsedResumeId.ShouldBe(sourceId);
        provenance.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void CreateFromResume_WithoutSourceId_StillRaisesProvenanceEvent_WithNullSource()
    {
        var result = SavedSearch.CreateFromResume(
            Owner, "CV-sök", ConfirmedCriteria(), false, sourceParsedResumeId: null, Clock);

        result.Value.DomainEvents.OfType<SavedSearchDerivedFromResumeDomainEvent>()
            .ShouldHaveSingleItem().SourceParsedResumeId.ShouldBeNull();
    }

    [Fact]
    public void CreateFromResume_WithInvalidName_ReturnsFailure()
    {
        var result = SavedSearch.CreateFromResume(
            Owner, "  ", ConfirmedCriteria(), false, Guid.NewGuid(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NameRequired");
    }
}
