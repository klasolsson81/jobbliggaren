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
using Jobbliggaren.Infrastructure.Time;
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

    // ITaxonomyReadModel-fake som speglar TaxonomyReadModel: okänt id ger en rad
    // UTAN label (graceful degradation, aldrig throw) — porten namnger inte det
    // den inte kunde slå upp.
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
                        : null))
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

        // #477 Low 1 — the activity-report handler never builds a match profile, so the
        // containment lookup is an inert no-op here (empty result, never throws).
        public ValueTask<IReadOnlyList<string>> GetContainingRegionsAsync(
            IReadOnlyList<string> municipalityConceptIds, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<string>>([]);

        // Fas 4b 8b.4a — the activity-report handler never resolves an occupation FIELD
        // either; inert no-op (empty result, never throws), parity the sibling above.
        public ValueTask<IReadOnlyList<string>> GetContainingOccupationFieldsAsync(
            IReadOnlyList<string> occupationGroupConceptIds, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<string>>([]);
    }


    // The real calendar, not a stub: it is pure and deterministic, so the Swedish
    // boundary is exercised once here rather than asserted twice in two places,
    // and these tests stay discriminating under a mutation of the calendar
    // (idiom: RefreshLandingStatsJobTests).
    //
    // NOT because CLAUDE.md §5 `Tests:` forbids a stub — it does not. §5 attaches
    // the obligation to the ASSERTION, not the seam, and a stub returning 22:00Z
    // would return a value the real adapter does emit. `dotnet-architect` caught
    // that over-citation once already. This is the tighter option, not the
    // mandated one.
    private static readonly SwedishCalendar Calendar = new();

    private GetActivityReportQueryHandler CreateHandler(
        AppDbContext db, ICurrentUser? user = null, ITaxonomyReadModel? taxonomy = null,
        IDateTimeProvider? clock = null) =>
        new(db, user ?? _currentUser, taxonomy ?? new FakeTaxonomy(), clock ?? _clock, Calendar);

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
    // Månadsfönster [start, end) — half-open, on the SWEDISH civil calendar
    //
    // Every instant below is written as the UTC value with the Swedish wall clock
    // in the comment, because that is the only way to read them. The three tests
    // that open this block were re-seeded when the window moved (Klas-direktiv
    // 2026-07-28): their old instants were UTC midnights, which are 01:00 or 02:00
    // Swedish, so after the move they sat one to two hours INSIDE the window and
    // stopped gating the boundary their names claim. `>=` → `>` survived both.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_AppliedAtOnSwedishFirstOfMonthMidnight_IsIncluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // 2026-05-31T22:00:00Z = 2026-06-01 00:00:00 Swedish (CEST, +02:00) —
        // exactly the instant the Swedish June opens. The start is INCLUSIVE.
        var onStart = new DateTimeOffset(2026, 5, 31, 22, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), onStart));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldHaveSingleItem().AppliedAt.ShouldBe(onStart);
    }

    [Fact]
    public async Task Handle_AppliedAtOnSwedishFirstOfNextMonthMidnight_IsExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // 2026-06-30T22:00:00Z = 2026-07-01 00:00:00 Swedish — the exclusive end
        // of the Swedish June.
        var onNextStart = new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), onNextStart));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AppliedAtInPreviousSwedishMonth_IsExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        // 2026-05-31T21:59:59Z = 2026-05-31 23:59:59 Swedish — the last second of
        // the Swedish May, one second short of the June window.
        var justBefore = new DateTimeOffset(2026, 5, 31, 21, 59, 59, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), justBefore));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 6), ct);

        result.Applications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AppliedAtJustAfterSwedishMidnight_BucketsIntoTheNewMonth_NotTheUtcOne()
    {
        // THE BEHAVIOUR CHANGE, in one test. The retired comment on this handler
        // named this exact instant as a known, accepted defect: "2026-04-30 22:30Z
        // = May 1 00:30 in Swedish summer time … buckets into the UTC month but
        // shows the Stockholm date — a ~2 h/month edge. Accepted for v1."
        //
        // The FE has always rendered "Datum sökt" in Europe/Stockholm, so this row
        // showed a MAY date inside an APRIL report. Window and display now
        // coincide. Concretely: the row moves OUT of the April report a job seeker
        // may already have filed with Arbetsförmedlingen, and INTO May.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var justAfterSwedishMidnight = new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), justAfterSwedishMidnight));
        await db.SaveChangesAsync(ct);

        var may = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 5), ct);
        var april = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 4), ct);

        may.Applications.ShouldHaveSingleItem().AppliedAt.ShouldBe(justAfterSwedishMidnight);
        april.Applications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AppliedAtOnSwedishMonthStart_InWinter_IsIncluded_AndTheYearIsTheNewOne()
    {
        // 2025-12-31T23:00:00Z = 2026-01-01 00:00:00 Swedish (CET, +01:00). The
        // January boundary crosses the YEAR, which is why the echoed year is
        // asserted too: a DTO built from the boundary instant would say 2025.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var onStart = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), onStart));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 1), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(1);
        result.Applications.ShouldHaveSingleItem().AppliedAt.ShouldBe(onStart);
    }

    [Fact]
    public async Task Handle_AppliedAtOneSecondBeforeSwedishMonthStart_InWinter_IsExcluded()
    {
        // 2025-12-31T22:59:59Z = 2025-12-31 23:59:59 Swedish (CET) — still
        // December.
        //
        // THIS is the test a hardcoded +2 h boundary fails: that implementation
        // opens January at 2025-12-31T22:00:00Z and wrongly admits this row. Its
        // summer sibling above fails under a hardcoded +1 h. Between them no fixed
        // offset stands — the hole that let a fixed `-2h` pass every job test in
        // the predecessor PR, where every seed sat in May.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var justBefore = new DateTimeOffset(2025, 12, 31, 22, 59, 59, TimeSpan.Zero);
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), justBefore));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 1), ct);

        result.Applications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_LastDayOfSwedishJuly_IsIncluded_NotDroppedByAddMonths()
    {
        // The exclusive end derived as Start.AddMonths(1) gives 2026-07-30T22:00Z
        // against a real August boundary of 2026-07-31T22:00Z: the whole of
        // 31 July disappears. June has 30 days and AddMonths preserves the
        // day-of-month — month LENGTH, nothing to do with DST (both months CEST).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var midMonth = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);  // 12:00 15 Jul Swedish
        var lastDay = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);   // 14:00 31 Jul Swedish
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), midMonth));
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), lastDay));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 7), ct);

        result.Applications.Count.ShouldBe(2);
        result.Applications[^1].AppliedAt.ShouldBe(lastDay);
    }

    [Fact]
    public async Task Handle_LastThreeDaysOfSwedishMarch_AreIncluded_NotDroppedByAddMonths()
    {
        // The worst window in the year. Start.AddMonths(1) off 2026-02-28T23:00Z
        // gives 2026-03-28T23:00Z against a real April boundary of
        // 2026-03-31T22:00Z — THREE whole Swedish days (29, 30, 31 March), because
        // February has 28 and AddMonths preserves the day-of-month. The
        // spring-forward accounts only for the missing hour, which is why the UTC
        // gap measures 2 d 23 h rather than a flat three days.
        //
        // March is also the only 2026 window whose start (+01) and end (+02) carry
        // different offsets.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var d29 = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);          // 14:00 29 Mar Swedish
        var d30 = new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);          // 14:00 30 Mar Swedish
        var lastSecond = new DateTimeOffset(2026, 3, 31, 21, 59, 59, TimeSpan.Zero); // 23:59:59 31 Mar
        var aprilStart = new DateTimeOffset(2026, 3, 31, 22, 0, 0, TimeSpan.Zero);   // 00:00:00 1 Apr — OUT
        foreach (var instant in new[] { d29, d30, lastSecond, aprilStart })
            db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), instant));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 3), ct);

        result.Applications.Count.ShouldBe(3);
        result.Applications.Select(a => a.AppliedAt).ShouldNotContain(aprilStart);
    }

    [Fact]
    public async Task Handle_ExplicitDecember_RollsTheYearForTheExclusiveEnd()
    {
        // The December window ends at 2027's January boundary. An `end` expressed
        // as "month + 1" without a year rollover throws rather than mis-counts, so
        // this is a crash class the boundary tests above cannot reach.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var inDecember = new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero); // 13:00 31 Dec Swedish
        var inJanuary = new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero);  // 00:00 1 Jan 2027 — OUT
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), inDecember));
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo(), inJanuary));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetActivityReportQuery(2026, 12), ct);

        result.Applications.ShouldHaveSingleItem().AppliedAt.ShouldBe(inDecember);
    }

    // ---------------------------------------------------------------
    // Default month — the SWEDISH civil month, not the UTC one
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_NoExplicitMonth_JustAfterSwedishMidnightOnTheFirst_DefaultsToTheSwedishMonth()
    {
        // Clock at 2026-07-31T22:30:00Z = 2026-08-01 00:30 Swedish. UTC says the
        // month is July; Sweden says August. Reading clock.UtcNow.Month here gave
        // the previous month's report for the first one to two hours of every
        // Swedish month.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var clock = new FakeDateTimeProvider(new DateTimeOffset(2026, 7, 31, 22, 30, 0, TimeSpan.Zero));
        var inAugust = new DateTimeOffset(2026, 7, 31, 22, 45, 0, TimeSpan.Zero); // 00:45 1 Aug Swedish
        var inJuly = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);    // 14:00 31 Jul Swedish
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Augusti"), inAugust));
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Juli"), inJuly));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db, clock: clock).Handle(new GetActivityReportQuery(), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(8);
        result.Applications.ShouldHaveSingleItem().Title.ShouldBe("Augusti");
    }

    [Fact]
    public async Task Handle_NoExplicitMonth_JustAfterSwedishMidnightOnNewYearsDay_DefaultsToJanuaryOfTheNewYear()
    {
        // Clock at 2025-12-31T23:30:00Z = 2026-01-01 00:30 Swedish (CET). UTC says
        // (2025, 12); Sweden says (2026, 1) — the YEAR differs, not only the month,
        // and the DTO carries both fields.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var clock = new FakeDateTimeProvider(new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero));
        var inJanuary = new DateTimeOffset(2025, 12, 31, 23, 45, 0, TimeSpan.Zero);  // 00:45 1 Jan 2026
        var inDecember = new DateTimeOffset(2025, 12, 31, 22, 30, 0, TimeSpan.Zero); // 23:30 31 Dec 2025
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("Januari"), inJanuary));
        db.Applications.Add(SubmittedAt(seeker.Id, null, ManualVo("December"), inDecember));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db, clock: clock).Handle(new GetActivityReportQuery(), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(1);
        result.Applications.ShouldHaveSingleItem().Title.ShouldBe("Januari");
    }

    [Fact]
    public async Task Handle_NoExplicitMonth_ExactlyAtSwedishMidnightOnTheFirst_DefaultsToTheNewMonth()
    {
        // Clock exactly ON the boundary instant. The month a boundary opens is the
        // month it belongs to — the same claim StartOfDay_IsIdempotent_OnItsOwnResult
        // makes for the day.
        //
        // An earlier version of this comment justified the case by a `>` vs `>=`
        // mutation. There is no such comparison: ResolveMonth calls
        // calendar.MonthOf, and MonthOf converts and reads the wall clock. What
        // this case actually adds over its 00:30 siblings is the exact instant —
        // any implementation that treats the boundary as belonging to the OLD
        // month fails here and nowhere else in this class.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId, ct);

        var clock = new FakeDateTimeProvider(new DateTimeOffset(2026, 7, 31, 22, 0, 0, TimeSpan.Zero));

        var result = await CreateHandler(db, clock: clock).Handle(new GetActivityReportQuery(), ct);

        result.Year.ShouldBe(2026);
        result.Month.ShouldBe(8);
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
    public async Task Handle_NoAuthenticatedUserAndNoExplicitMonth_EchoesDefaultCurrentMonth()
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
