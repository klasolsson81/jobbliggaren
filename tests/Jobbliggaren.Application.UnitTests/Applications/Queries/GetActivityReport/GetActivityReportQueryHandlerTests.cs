using Jobbliggaren.Application.Applications.Queries.GetActivityReport;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Queries.GetActivityReport;

// #316 — AF-aktivitetsrapport read-model. Deterministisk projektion (NOLL AI).
//
// Täcker här (EF InMemory, fake/substitute för ICurrentUser/ITaxonomyReadModel/
// IDateTimeProvider): månadsfönster [start, end) (half-open), Draft-exkludering,
// JobSeeker-scoping, anonym-användare-tom-lista (men ekande år/månad),
// default-månad = innevarande månad, explicit år/månad, samt källprojektion
// (JobAd-kopplad vs ManualPosting). Location-resolvering kan INTE testas här —
// municipality_concept_id är en EF SHADOW-prop (STORED generated column ur
// raw_payload) som InMemory-providern inte beräknar (rad blir null). Den täcks
// i GetActivityReportLocationIntegrationTests (Testcontainers, riktig Postgres).
public class GetActivityReportQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // Klocka för default-månad: 2026-06-15 ⇒ innevarande månad = juni 2026.
    private readonly FakeDateTimeProvider _clock =
        new(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

    public GetActivityReportQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    // ITaxonomyReadModel-fake som speglar TaxonomyReadModel: okänt id ger
    // "Okänd kod ({id})"-fallback (graceful degradation, aldrig throw).
    private sealed class FakeTaxonomy(IReadOnlyDictionary<string, string>? map = null)
        : ITaxonomyReadModel
    {
        public ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
            IReadOnlyList<string> conceptIds, CancellationToken cancellationToken)
            => new(conceptIds
                .Select(id => new TaxonomyLabelDto(
                    id,
                    map is not null && map.TryGetValue(id, out var l)
                        ? l
                        : $"Okänd kod ({id})"))
                .ToList());

        public ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
            string prefix, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        // ADR 0084 — the activity-report handler never broadens occupation groups,
        // so this stub is an inert no-op (empty result, never throws).
        public ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
            IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<string>>([]);
    }

    private GetActivityReportQueryHandler CreateHandler(
        AppDbContext db, ICurrentUser? user = null, ITaxonomyReadModel? taxonomy = null) =>
        new(db, user ?? _currentUser, taxonomy ?? new FakeTaxonomy(), _clock);

    private async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test", _clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    // Skapar en Submitted-ansökan vars AppliedAt = appliedAt (stämplas via
    // TransitionTo med en klocka satt till exakt det datumet).
    private static DomainApplication SubmittedAt(
        JobSeekerId seekerId, JobAdId? jobAdId, ManualPosting? manual, DateTimeOffset appliedAt)
    {
        var clockAtApply = new FakeDateTimeProvider(appliedAt);
        var app = DomainApplication.Create(seekerId, jobAdId, null, manual, clockAtApply).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clockAtApply);
        return app;
    }

    private static ManualPosting ManualVo(string title = "Manuell titel", string company = "Manuellt företag") =>
        ManualPosting.Create(title, company, "https://example.com/manuell", null).Value;

    // ---------------------------------------------------------------
    // Månadsfönster [start, end) — half-open
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_AppliedAtOnFirstOfMonthMidnight_IsIncluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // 2026-06-01 00:00:00Z — gränsen start är INKLUSIVE.
        var onStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), onStart));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldHaveSingleItem().AppliedAt.ShouldBe(onStart);
    }

    [Fact]
    public async Task Handle_AppliedAtOnFirstOfNextMonthMidnight_IsExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // 2026-07-01 00:00:00Z — gränsen end är EXKLUSIVE (nästa månad).
        var onNextStart = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), onNextStart));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AppliedAtInPreviousMonth_IsExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // 2026-05-31 23:59:59Z — strax FÖRE start → exkluderas.
        var justBefore = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), justBefore));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Draft (AppliedAt == null) exkluderas
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_DraftApplication_IsExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // Draft (aldrig submittad) → AppliedAt == null → exkluderas.
        db.Applications.Add(DomainApplication.Create(seeker.Id, null, null, ManualVo(), _clock).Value);
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // JobSeeker-scoping — bara aktuell användares ansökningar
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_OtherUsersApplication_IsExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var mine = await SeedSeekerAsync(db, _userId, ct);
        var other = await SeedSeekerAsync(db, Guid.NewGuid(), ct);

        var applied = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(mine.Id, null, ManualVo("Min"), applied));
        db.Applications.Add(SubmittedAt(other.Id, null, ManualVo("Annans"), applied));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        var item = result.Applications.ShouldHaveSingleItem();
        item.Employer.ShouldBe("Manuellt företag");
        item.Title.ShouldBe("Min");
    }

    // ---------------------------------------------------------------
    // Anonym användare — tom lista men ekande år/månad
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsEmptyListButEchoesResolvedYearMonth()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await CreateHandler(db, user: anon).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldBeEmpty();
        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(6);
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUserAndNoExplicitMonth_EchoesDefaultPreviousMonth()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        // Klocka = 2026-06-15 ⇒ default = innevarande månad = juni 2026.
        var result = await CreateHandler(db, user: anon).Handle(new GetActivityReportQuery(), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(6);
    }

    // ---------------------------------------------------------------
    // Default-månad = innevarande månad relativt IDateTimeProvider.UtcNow
    // (Klas 2026-06-28: innevarande månad är alltid standard)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_NoExplicitMonth_DefaultsToCurrentMonthAndFiltersAccordingly()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // Klocka = 2026-06-15 ⇒ default-fönster = juni 2026.
        var inMay = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var inJune = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Maj"), inMay));
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Juni"), inJune));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(6);
        result.Applications.ShouldHaveSingleItem().Title.ShouldBe("Juni");
    }

    // ---------------------------------------------------------------
    // Explicit år/månad hedras
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_ExplicitYearMonth_IsHonored()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var inMarch = new DateTimeOffset(2026, 3, 12, 12, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Mars"), inMarch));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 3), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(3);
        result.Applications.ShouldHaveSingleItem().Title.ShouldBe("Mars");
    }

    // ---------------------------------------------------------------
    // Källprojektion — JobAd-kopplad vs ManualPosting
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_JobAdLinkedApplication_ProjectsEmployerTitleUrlSourceFromJobAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var jobAd = JobAd.Create(
            "Backend-utvecklare", Company.Create("Klarna").Value, "Beskrivning",
            "https://example.com/jobb/1", JobSource.Platsbanken,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), null, _clock).Value;
        db.JobAds.Add(jobAd);

        var applied = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, jobAd.Id, null, applied));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        var item = result.Applications.ShouldHaveSingleItem();
        item.Employer.ShouldBe("Klarna");
        item.Title.ShouldBe("Backend-utvecklare");
        item.Url.ShouldBe("https://example.com/jobb/1");
        item.Source.ShouldBe(JobSource.Platsbanken.Value);
        // Ort kan inte projiceras i InMemory (shadow-prop) → null här; täcks i
        // integrationstestet mot riktig Postgres.
        item.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ManualPostingApplication_ProjectsFromManualWithSourceManual()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var applied = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Frontend", "Spotify"), applied));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        var item = result.Applications.ShouldHaveSingleItem();
        item.Employer.ShouldBe("Spotify");
        item.Title.ShouldBe("Frontend");
        item.Url.ShouldBe("https://example.com/manuell");
        item.Source.ShouldBe("Manual");
        item.Location.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Ordning — AppliedAt stigande
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_MultipleApplications_OrderedByAppliedAtAscending()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var earlier = new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        // Sätt in i omvänd ordning för att bevisa att handlern sorterar.
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Senare"), later));
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Tidigare"), earlier));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.Count.ShouldBe(2);
        result.Applications[0].Title.ShouldBe("Tidigare");
        result.Applications[1].Title.ShouldBe("Senare");
    }
}
