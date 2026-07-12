using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedJobAds;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedJobAds.Queries;

public class ListSavedJobAdsQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public ListSavedJobAdsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static JobAd CreateJobAd(FakeDateTimeProvider clock, string title) =>
        JobAd.Create(
            title,
            Company.Create("Acme AB").Value,
            "Beskrivning",
            $"https://example.com/{title}",
            JobSource.Manual,
            clock.UtcNow,
            clock.UtcNow.AddDays(30),
            clock).Value;

    [Fact]
    public async Task Handle_ReturnsSavedJobAds_WithJobAdSummary_OrderedByCreatedAtDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);

        var jobAd1 = CreateJobAd(_clock, "Backendutvecklare");
        var jobAd2 = CreateJobAd(_clock, "Frontendutvecklare");
        db.JobAds.AddRange(jobAd1, jobAd2);

        var saved1 = SavedJobAd.Save(seeker.Id, jobAd1.Id, _clock.UtcNow);
        var saved2 = SavedJobAd.Save(seeker.Id, jobAd2.Id, _clock.UtcNow.AddMinutes(5));
        db.SavedJobAds.AddRange(saved1, saved2);
        await db.SaveChangesAsync(ct);

        var handler = new ListSavedJobAdsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new ListSavedJobAdsQuery(), ct);

        result.Count.ShouldBe(2);
        result[0].JobAdId.ShouldBe(jobAd2.Id.Value);
        result[0].JobAd.ShouldNotBeNull();
        result[0].JobAd!.Title.ShouldBe("Frontendutvecklare");
        result[1].JobAdId.ShouldBe(jobAd1.Id.Value);
        result[1].JobAd.ShouldNotBeNull();
        result[1].JobAd!.Title.ShouldBe("Backendutvecklare");
    }

    [Fact]
    public async Task Handle_WhenJobAdMissing_RowsJobAdSummaryIsNull()
    {
        // Tests ADR 0048 Beslut c fallback-vägen: bokmärket pekar på
        // JobAdId vars rad inte längre finns (raderad eller filtrerad bort) →
        // GroupJoin/DefaultIfEmpty ger j == null → JobAd null i DTO:n.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);

        var orphanJobAdId = new JobAdId(Guid.NewGuid());
        var saved = SavedJobAd.Save(seeker.Id, orphanJobAdId, _clock.UtcNow);
        db.SavedJobAds.Add(saved);
        await db.SaveChangesAsync(ct);

        var handler = new ListSavedJobAdsQueryHandler(db, _currentUser);
        var result = await handler.Handle(new ListSavedJobAdsQuery(), ct);

        result.ShouldHaveSingleItem();
        result[0].JobAd.ShouldBeNull();
        result[0].JobAdId.ShouldBe(orphanJobAdId.Value);
    }

    // #805-3: JobAdSummaryDto bär numera Status, och ListSavedJobAds är den fjärde
    // handlern som konstruerar den. Utan detta test är /sparade-läsvägens
    // Status-projektion helt otäckt: den kunde tappas (eller hårdkodas till
    // "Active") utan att någon svit blev röd.
    //
    // Arkiverings-grenen pinnar dessutom #805-3:s bärande premiss HÄR: en arkiverad
    // annons joinar FORTFARANDE (arkivering != radering; det globala query-filtret
    // är DeletedAt == null, och DeletedAt saknar writer, #821). Skulle någon
    // åter-koppla "annonsen är borta" till JobAd-nullheten faller
    // ShouldNotBeNull() — samma spärr som Api-integrationssvitens
    // *_WithArchivedJobAd_*-tester ger de tre ansöknings-handlarna.
    [Fact]
    public async Task Handle_ProjectsJobAdStatus_AndKeepsArchivedJobAdJoined()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);

        var activeAd = CreateJobAd(_clock, "Backendutvecklare");
        var archivedAd = CreateJobAd(_clock, "Frontendutvecklare");
        db.JobAds.AddRange(activeAd, archivedAd);

        db.SavedJobAds.Add(SavedJobAd.Save(seeker.Id, activeAd.Id, _clock.UtcNow));
        db.SavedJobAds.Add(
            SavedJobAd.Save(seeker.Id, archivedAd.Id, _clock.UtcNow.AddMinutes(5)));
        await db.SaveChangesAsync(ct);

        // Arkivera via domänmetoden — samma väg som retention-/expiry-jobben tar.
        archivedAd.Archive(_clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var handler = new ListSavedJobAdsQueryHandler(db, _currentUser);
        var result = await handler.Handle(new ListSavedJobAdsQuery(), ct);

        // Ordnad CreatedAt desc → det senare bokmärket (den arkiverade) först.
        result.Count.ShouldBe(2);
        result[0].JobAd.ShouldNotBeNull();
        result[0].JobAd!.Status.ShouldBe(JobAdStatus.Archived.Value);
        result[1].JobAd.ShouldNotBeNull();
        result[1].JobAd!.Status.ShouldBe(JobAdStatus.Active.Value);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new ListSavedJobAdsQueryHandler(db, currentUser);

        var result = await handler.Handle(new ListSavedJobAdsQuery(), ct);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_OnlyReturnsCurrentUsersSavedJobAds()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", _clock).Value;
        var otherSeeker = JobSeeker.Register(Guid.NewGuid(), "Other", _clock).Value;
        db.JobSeekers.AddRange(seeker, otherSeeker);

        var jobAd = CreateJobAd(_clock, "Backendutvecklare");
        db.JobAds.Add(jobAd);

        db.SavedJobAds.Add(SavedJobAd.Save(otherSeeker.Id, jobAd.Id, _clock.UtcNow));
        await db.SaveChangesAsync(ct);

        var handler = new ListSavedJobAdsQueryHandler(db, _currentUser);
        var result = await handler.Handle(new ListSavedJobAdsQuery(), ct);

        result.ShouldBeEmpty();
    }
}
