using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Application.SavedSearches.Queries.ListSavedSearches;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.SavedSearches.Queries;

// ADR 0043-utvidgning (CTO 2026-05-17, Approach A) — civic-utility: concept-id
// får aldrig nå /sokningar-ytan. ListSavedSearchesQueryHandler berikar varje
// sparad sökning med namn via ITaxonomyReadModel.ResolveLabelsAsync IN-PROCESS
// (singleton O(1)-lookup; ingen HTTP-endpoint, ingen klient-fan-out, ingen
// Beslut D-cap-relevans — annan yta än /taxonomy/labels-endpointen).
//
// DTO-form: label-projektioner per dimension (IReadOnlyList<TaxonomyLabelDto>)
// speglar ITaxonomyReadModel.ResolveLabelsAsync-kontraktet. C2 (architect
// F5.5/F6): Ssyk → OccupationGroup + Municipality i både VO och DTO (ingen
// FE-konsument av SavedSearch-API:t — fritt rename); labels per dimension =
// OccupationGroupLabels + MunicipalityLabels + RegionLabels.
//
// CA2012: NSubstitute-stubbning av ValueTask-returnerande port-medlemmar är
// ett känt analyzer-false-positive (substitute-anropet konsumeras aldrig — det
// interceptas av NSubstitute). Suppression scoped till mock-setup.
#pragma warning disable CA2012
public class ListSavedSearchesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITaxonomyReadModel _taxonomy =
        Substitute.For<ITaxonomyReadModel>();
    private readonly Guid _userId = Guid.NewGuid();

    public ListSavedSearchesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);

        // Default: porten ekar tillbaka fallback-form för okänt id så att
        // tester som inte bryr sig om namn ändå inte kraschar. Specifika
        // tester stubbar om för sina concept-id.
        _taxonomy.ResolveLabelsAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                ((IReadOnlyList<string>)ci[0])
                    .Select(id => new TaxonomyLabelDto(id, $"Okänd kod ({id})"))
                    .ToList()));
    }

    private static SavedSearch NewSaved(
        JobSeekerId seekerId,
        string name,
        IEnumerable<string>? occupationGroup = null,
        IEnumerable<string>? municipality = null,
        IEnumerable<string>? region = null) =>
        SavedSearch.Create(
            seekerId, name,
            SearchCriteria.Create(
                occupationGroup: occupationGroup ?? ["grp_12345"],
                municipality: municipality,
                region: region,
                q: null,
                sortBy: JobAdSortBy.PublishedAtDesc).Value,
            false, FakeDateTimeProvider.Default).Value;

    // ---- Befintliga invarianter (får EJ regrediera) -----------------------

    [Fact]
    public async Task Handle_ReturnsOnlyOwnSavedSearches()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var other = JobSeeker.Register(Guid.NewGuid(), "Other", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(other);

        db.SavedSearches.Add(NewSaved(seeker.Id, "Min A"));
        db.SavedSearches.Add(NewSaved(seeker.Id, "Min B"));
        db.SavedSearches.Add(NewSaved(other.Id, "Annans"));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);
        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(s => s.Name == "Min A" || s.Name == "Min B");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new ListSavedSearchesQueryHandler(db, currentUser, _taxonomy);

        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoJobSeeker_ReturnsEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);

        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    // ---- Namn-berikning (ADR 0043-utvidgning, Approach A) -----------------

    [Fact]
    public async Task Handle_ShouldPopulateOccupationGroupMunicipalityAndRegionLabels_WhenConceptIdsResolve()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        db.SavedSearches.Add(NewSaved(
            seeker.Id, "IT i Stockholm",
            occupationGroup: ["MVqp_eS8_kDZ"],
            municipality: ["AvNB_uwa_6n6"],
            region: ["CifL_Rsz_Hb7"]));
        await db.SaveChangesAsync(CancellationToken.None);

        _taxonomy.ResolveLabelsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Contains("MVqp_eS8_kDZ")),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                (IReadOnlyList<TaxonomyLabelDto>)
                [
                    new TaxonomyLabelDto("MVqp_eS8_kDZ", "Mjukvaru- och systemutvecklare"),
                ]));
        _taxonomy.ResolveLabelsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Contains("AvNB_uwa_6n6")),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                (IReadOnlyList<TaxonomyLabelDto>)
                [
                    new TaxonomyLabelDto("AvNB_uwa_6n6", "Stockholm"),
                ]));
        _taxonomy.ResolveLabelsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Contains("CifL_Rsz_Hb7")),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                (IReadOnlyList<TaxonomyLabelDto>)
                [
                    new TaxonomyLabelDto("CifL_Rsz_Hb7", "Stockholms län"),
                ]));

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);
        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.OccupationGroupLabels.ShouldContain(l =>
            l.ConceptId == "MVqp_eS8_kDZ" && l.Label == "Mjukvaru- och systemutvecklare");
        dto.MunicipalityLabels.ShouldContain(l =>
            l.ConceptId == "AvNB_uwa_6n6" && l.Label == "Stockholm");
        dto.RegionLabels.ShouldContain(l =>
            l.ConceptId == "CifL_Rsz_Hb7" && l.Label == "Stockholms län");
        // Råa concept-id-fälten orörda (additiv label-projektion).
        dto.OccupationGroup.ShouldBe(["MVqp_eS8_kDZ"]);
        dto.Municipality.ShouldBe(["AvNB_uwa_6n6"]);
        dto.Region.ShouldBe(["CifL_Rsz_Hb7"]);
    }

    [Fact]
    public async Task Handle_ShouldPropagateFallbackLabel_WhenConceptIdIsUnknown()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        db.SavedSearches.Add(NewSaved(
            seeker.Id, "Stale", occupationGroup: ["borttagen-kod"]));
        await db.SaveChangesAsync(CancellationToken.None);

        // Porten ger fallback (befintlig ResolveLabelsAsync-semantik — aldrig
        // throw/null). Default-stubben i ctor producerar "Okänd kod (<id>)".

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);
        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.OccupationGroupLabels.ShouldContain(l =>
            l.ConceptId == "borttagen-kod"
            && l.Label == "Okänd kod (borttagen-kod)");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyLabelLists_WhenCriteriaHasNoConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        // Endast fritext-q, inga concept-id.
        db.SavedSearches.Add(SavedSearch.Create(
            seeker.Id, "Bara q",
            SearchCriteria.Create(
                occupationGroup: null, municipality: null, region: null,
                q: "remote", sortBy: JobAdSortBy.PublishedAtDesc).Value,
            false, FakeDateTimeProvider.Default).Value);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);
        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.OccupationGroupLabels.ShouldBeEmpty();
        dto.MunicipalityLabels.ShouldBeEmpty();
        dto.RegionLabels.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldNotThrow_WhenSavedSearchHasNoConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        db.SavedSearches.Add(SavedSearch.Create(
            seeker.Id, "Tom",
            // q måste vara ≥2 tecken (SearchCriteria-invariant) — "x" fick
            // Create att faila → .Value kastade (test-arrange-defekt, ej
            // prodkod). "xy" = giltigt, behåller testintentionen "endast q,
            // inga concept-id".
            SearchCriteria.Create(
                occupationGroup: null, municipality: null, region: null,
                q: "xy", sortBy: JobAdSortBy.PublishedAtDesc).Value,
            false, FakeDateTimeProvider.Default).Value);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);

        var act = async () =>
            await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task Handle_ShouldInvokeTaxonomyPort_ForEachSavedSearchWithConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        db.SavedSearches.Add(NewSaved(seeker.Id, "A", occupationGroup: ["a1"], region: ["r1"]));
        db.SavedSearches.Add(NewSaved(seeker.Id, "B", occupationGroup: ["b1"], municipality: ["m1"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);
        await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        // Porten anropas (batch-signatur IReadOnlyList<string>, ej
        // per-element-fan-out — speglar ResolveLabelsAsync-kontraktet).
        // Minst ett anrop måste ha skett; ingen råa-concept-id-fan-out.
        await _taxonomy.ReceivedWithAnyArgs().ResolveLabelsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldKeepJobSeekerScoping_WhenResolvingLabels()
    {
        // Namn-berikning får inte bryta den befintliga JobSeeker-scoping-
        // invarianten (cross-tenant-läcka). Annans sökning syns aldrig,
        // ens med berikning aktiv.
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Owner", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var other = JobSeeker.Register(Guid.NewGuid(), "Other", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(other);
        db.SavedSearches.Add(NewSaved(seeker.Id, "Min", occupationGroup: ["mine"]));
        db.SavedSearches.Add(NewSaved(other.Id, "Annans", occupationGroup: ["theirs"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListSavedSearchesQueryHandler(db, _currentUser, _taxonomy);
        var result = await handler.Handle(new ListSavedSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Name.ShouldBe("Min");
        result.ShouldNotContain(s => s.Name == "Annans");
    }
}
#pragma warning restore CA2012
