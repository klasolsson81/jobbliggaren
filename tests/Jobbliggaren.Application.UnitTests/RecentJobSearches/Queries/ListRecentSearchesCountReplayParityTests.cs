using System.Collections;
using System.Reflection;
using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.RecentJobSearches.Queries;
using Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Queries;

/// <summary>
/// #1471 — a recent-search row's count and the list its link produces must rest on the SAME
/// criterion, axis for axis. The count is whatever <see cref="IJobAdSearchQuery.CountAsync"/>
/// receives; the replay is whatever <see cref="RecentJobSearchDto"/> carries to
/// <c>buildRecentSearchHref</c>. Nothing bound the two at the VALUE level before this test:
/// <c>RecentJobSearchProjectionParityTests</c> reasons about property names on the entity and the
/// DTO, and that guard was green while the handler counted WITH the employer axis and projected
/// nothing for it — the user saw a number, clicked, and got a wider list. Same class as #1407 on
/// the distans axis; this guard is written on the filter criteria rather than on the entity so
/// the next axis fails here before it ships.
/// </summary>
/// <remarks>
/// Every row is seeded through the production path (<see cref="SearchCriteria.Create"/> →
/// <see cref="RecentJobSearch.Capture"/>), so no assertion rests on a premise production cannot
/// produce (AGENTS.md §5 <c>Tests:</c>). Where a premise names a state the CURRENT writer does
/// not produce, the test names the actor that did.
/// </remarks>
public class ListRecentSearchesCountReplayParityTests
{
    // A legal-entity org.nr: third digit >= 2 (OrganizationNumber.IsPersonnummerShaped is the
    // house discriminator). The same value RecentSearchesTests persists through real Postgres.
    private const string LegalEntityOrgNr = "5566010101";

    // Every JobAdFilterCriteria axis → the RecentJobSearchDto property the replay reads it from.
    // Declared rather than derived, so a new axis with no row here FAILS below: an inclusion
    // spec cannot notice that it is measuring nothing, and silence must not pass.
    private static readonly IReadOnlyDictionary<string, string> ReplayProjection =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OccupationGroup"] = "OccupationGroupList",
            ["Municipality"] = "MunicipalityList",
            ["Region"] = "RegionList",
            ["EmploymentType"] = "EmploymentTypeList",
            ["WorktimeExtent"] = "WorktimeExtentList",
            ["Employer"] = "EmployerList",
            ["Remote"] = "Remote",
            ["Q"] = "Q",
        };

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly Guid _userId = Guid.NewGuid();

    public ListRecentSearchesCountReplayParityTests()
    {
        _currentUser.UserId.Returns(_userId);
#pragma warning disable CA2012 // ValueTask från NSubstitute-stub konsumeras varje gång av handlern
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                IReadOnlyList<TaxonomyLabelDto> labels = call.ArgAt<IReadOnlyList<string>>(0)
                    .Select(id => new TaxonomyLabelDto(id, $"Label-{id}"))
                    .ToList();
                return ValueTask.FromResult(labels);
            });
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Handle_CountsAndReplaysTheSameCriterion_OnEveryAxis()
    {
        var (counted, dto) = await RunAsync(EveryAxisSet(employer: [LegalEntityOrgNr]));

        AssertCountAndReplayAgree(counted, dto);
    }

    // Third digit < '2' — the house discriminator (OrganizationNumber.IsPersonnummerShaped).
    private const string PersonnummerShapedOrgNr = "1010101010";

    [Fact]
    public async Task Handle_WithholdsAPersonnummerShapedEmployer_FromCountAndReplayAlike()
    {
        // The actor that produced this row is not the current writer: RecentJobSearchCaptureBehavior
        // has refused a personnummer-shaped employer since A2 (2026-08-19), and
        // RecentJobSearchCaptureBehaviorTests.Handle_PersonnummerShapedEmployer_RunsTheSearchButCapturesNothing
        // pins that. Rows written before that date can carry one — LRU-capped, never purged — and
        // this is what the handler does with them: ADR 0087 D8(c)'s masked arm, on every consumer.
        var (counted, dto) = await RunAsync(EveryAxisSet(employer: [PersonnummerShapedOrgNr]));

        counted.Employer.ShouldBeEmpty("the count must not run on a value the wire will not carry");
        dto.EmployerList.ShouldBeEmpty();
        JsonSerializer.Serialize(dto).Contains(PersonnummerShapedOrgNr, StringComparison.Ordinal).ShouldBeFalse(
            "a personnummer-shaped org.nr must reach no property of the DTO under any name");
        AssertCountAndReplayAgree(counted, dto);
    }

    [Fact]
    public async Task Handle_KeepsTheLegalEntity_WhenAPersonnummerShapedValueSitsBesideIt()
    {
        var (counted, dto) = await RunAsync(
            EveryAxisSet(employer: [LegalEntityOrgNr, PersonnummerShapedOrgNr]));

        counted.Employer.ShouldBe([LegalEntityOrgNr]);
        dto.EmployerList.ShouldBe([LegalEntityOrgNr]);
        AssertCountAndReplayAgree(counted, dto);
    }

    // The two sides, read from the one Handle call: what the port was asked to count, and what
    // the wire is handed to replay.
    private async Task<(JobAdFilterCriteria Counted, RecentJobSearchDto Dto)> RunAsync(
        SearchCriteria criteria)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        JobAdFilterCriteria? counted = null;
#pragma warning disable CA2012
        _search.CountAsync(
                Arg.Do<JobAdFilterCriteria>(c => counted = c), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(0));
#pragma warning restore CA2012

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(
            new ListRecentSearchesQuery(IncludeCount: true), CancellationToken.None);

        counted.ShouldNotBeNull("IncludeCount=true must reach the port, or nothing was counted");
        return (counted!, result.ShouldHaveSingleItem());
    }

    // Every axis set at once, so a divergence on any one of them shows. Values are shapes the
    // production validator accepts, not taxonomy truths — the port is a substitute.
    private static SearchCriteria EveryAxisSet(IReadOnlyList<string> employer) =>
        SearchCriteria.Create(
            occupationGroup: ["grp_12345"],
            municipality: ["sthlm_kn"],
            region: ["stockholm"],
            employmentType: ["tillsvidare"],
            worktimeExtent: ["heltid"],
            employer: employer,
            remote: true,
            q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;

    private static void AssertCountAndReplayAgree(JobAdFilterCriteria counted, RecentJobSearchDto dto)
    {
        var axes = typeof(JobAdFilterCriteria)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .ToArray();

        // Floor against a broken source set: with no axes, nothing below runs and the test is
        // green on nothing.
        axes.ShouldNotBeEmpty("the guard measures nothing if JobAdFilterCriteria exposes no axes");

        var unmapped = axes
            .Select(p => p.Name)
            .Where(name => !ReplayProjection.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        unmapped.ShouldBeEmpty(
            "every axis the count filters on needs a row in ReplayProjection naming the DTO "
            + $"property that replays it — otherwise this guard cannot see it. Unmapped: {string.Join(", ", unmapped)}");

        var stale = ReplayProjection.Keys
            .Where(name => axes.All(p => !string.Equals(p.Name, name, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        stale.ShouldBeEmpty(
            $"ReplayProjection names axes JobAdFilterCriteria no longer has: {string.Join(", ", stale)}");

        foreach (var axis in axes)
        {
            var projected = typeof(RecentJobSearchDto).GetProperty(ReplayProjection[axis.Name]);
            projected.ShouldNotBeNull(
                $"the count filters on {axis.Name} but RecentJobSearchDto has no "
                + $"{ReplayProjection[axis.Name]} to replay it from, so the row's count and the "
                + "list its link produces rest on different criteria (#1471)");

            var countedValue = axis.GetValue(counted);
            var replayedValue = projected!.GetValue(dto);
            SameValue(countedValue, replayedValue).ShouldBeTrue(
                $"axis {axis.Name}: counted {Render(countedValue)}, replayed {Render(replayedValue)}");
        }
    }

    // Lists compare element-wise in order; scalars by equality. Fail-closed on a type this
    // method does not know — a silently-false comparison would report a divergence that is not
    // there, a silently-true one would hide one that is.
    private static bool SameValue(object? counted, object? replayed) => (counted, replayed) switch
    {
        (null, null) => true,
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        (bool a, bool b) => a == b,
        (IEnumerable a, IEnumerable b) => a.Cast<object>().SequenceEqual(b.Cast<object>()),
        (null, _) or (_, null) => false,
        _ => throw new InvalidOperationException(
            $"SameValue does not know how to compare {counted.GetType()} to {replayed.GetType()} — add an arm."),
    };

    private static string Render(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        IEnumerable e => "[" + string.Join(", ", e.Cast<object>()) + "]",
        _ => value.ToString() ?? "?",
    };
}
