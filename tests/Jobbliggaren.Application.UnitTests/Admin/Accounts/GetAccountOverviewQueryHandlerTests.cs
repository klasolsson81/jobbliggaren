using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountOverview;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Time;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts;

public sealed class GetAccountOverviewQueryHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset SampledAt = new(2026, 10, 25, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ShouldAggregateCalendarPeriods_WhenTheDirectoryReturnsDailyRegistrations()
    {
        var directory = Substitute.For<IAccountDirectory>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(SampledAt);
        var calendar = new SwedishCalendar();
        var today = new DateOnly(2026, 10, 25);
        var days = Enumerable.Range(0, 90)
            .Select(index => new AccountRegistrationDay(today.AddDays(index - 89), index >= 60 ? 1 : 0))
            .ToArray();
        var counts = new AccountStatusCounts(Total: 33, Active: 27, PendingDeletion: 2, ProfileMissing: 3, Suspended: 1);
        directory.GetOverviewAsync(Arg.Any<IReadOnlyList<CivilDayWindow>>(), Arg.Any<CancellationToken>())
            .Returns(new AccountDirectoryOverview(counts, days));

        var result = await new GetAccountOverviewQueryHandler(directory, clock, calendar)
            .Handle(new GetAccountOverviewQuery(), Ct);

        result.SampledAt.ShouldBe(SampledAt);
        result.Counts.ShouldBe(counts);
        result.Days.ShouldBe(days);
        result.NewAccounts.Today.Count.ShouldBe(1);
        result.NewAccounts.Yesterday.Count.ShouldBe(1);
        result.NewAccounts.Last7Days.Count.ShouldBe(7);
        result.NewAccounts.Last30Days.Count.ShouldBe(30);
        result.NewAccounts.Today.From.ShouldBe(calendar.DayWindow(today).Start);
        result.NewAccounts.Today.Before.ShouldBe(SampledAt);
        result.NewAccounts.Yesterday.From.ShouldBe(calendar.DayWindow(today.AddDays(-1)).Start);
        result.NewAccounts.Yesterday.Before.ShouldBe(calendar.DayWindow(today).Start);
        result.NewAccounts.Last7Days.From.ShouldBe(calendar.DayWindow(today.AddDays(-6)).Start);
        result.NewAccounts.Last7Days.Before.ShouldBe(SampledAt);
        result.NewAccounts.Last30Days.From.ShouldBe(calendar.DayWindow(today.AddDays(-29)).Start);
        result.NewAccounts.Last30Days.Before.ShouldBe(SampledAt);
        _ = clock.Received(1).UtcNow;
        await directory.Received(1).GetOverviewAsync(Arg.Any<IReadOnlyList<CivilDayWindow>>(), Ct);
    }

    [Theory]
    [InlineData("2026-03-29T12:30:00Z", "2026-03-29", 23)]
    [InlineData("2026-10-25T12:30:00Z", "2026-10-25", 25)]
    [InlineData("2026-07-14T22:00:00Z", "2026-07-15", 24)]
    public async Task Handle_ShouldSupplyNinetyConsecutiveUtcWindows_WhenCalendarTimeDiffersFromUtc(
        string instant, string civilDate, int fullDayHours)
    {
        var sampledAt = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        var today = DateOnly.Parse(civilDate, System.Globalization.CultureInfo.InvariantCulture);
        var directory = Substitute.For<IAccountDirectory>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(sampledAt);
        var calendar = new SwedishCalendar();
        IReadOnlyList<CivilDayWindow>? requested = null;
        directory.GetOverviewAsync(Arg.Any<IReadOnlyList<CivilDayWindow>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requested = call.ArgAt<IReadOnlyList<CivilDayWindow>>(0);
                return new AccountDirectoryOverview(new AccountStatusCounts(0, 0, 0, 0),
                    requested.Select(day => new AccountRegistrationDay(day.Day, 0)).ToArray());
            });

        var result = await new GetAccountOverviewQueryHandler(directory, clock, calendar)
            .Handle(new GetAccountOverviewQuery(), Ct);

        var windows = requested.ShouldNotBeNull();
        windows.Count.ShouldBe(90);
        windows[0].Day.ShouldBe(today.AddDays(-89));
        windows[^1].Day.ShouldBe(today);
        windows[^1].End.ShouldBe(sampledAt);
        (calendar.DayWindow(today).End - calendar.DayWindow(today).Start).TotalHours.ShouldBe(fullDayHours);
        for (var index = 0; index < windows.Count; index++)
        {
            windows[index].Day.ShouldBe(today.AddDays(index - 89));
            windows[index].Start.ShouldBe(calendar.DayWindow(windows[index].Day).Start);
            windows[index].Start.Offset.ShouldBe(TimeSpan.Zero);
            windows[index].End.Offset.ShouldBe(TimeSpan.Zero);
            if (index < windows.Count - 1)
                windows[index].End.ShouldBe(windows[index + 1].Start);
        }
        result.Days.Count.ShouldBe(90);
        result.Days.ShouldAllBe(day => day.NewAccounts == 0);
        result.NewAccounts.Today.Count.ShouldBe(0);
        result.NewAccounts.Last30Days.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ShouldPropagateSourceFailure_WhenTheDirectoryCannotRead()
    {
        var directory = Substitute.For<IAccountDirectory>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(SampledAt);
        var failure = new TimeoutException();
        directory.GetOverviewAsync(Arg.Any<IReadOnlyList<CivilDayWindow>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AccountDirectoryOverview>(failure));

        var thrown = await Should.ThrowAsync<TimeoutException>(async () =>
            await new GetAccountOverviewQueryHandler(directory, clock, new SwedishCalendar())
                .Handle(new GetAccountOverviewQuery(), Ct));

        thrown.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Authorization_ShouldRefuseAnOrdinaryCaller_BeforeTheDirectoryRuns()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsInRole(Roles.Admin).Returns(false);
        var directory = Substitute.For<IAccountDirectory>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(SampledAt);
        var handler = new GetAccountOverviewQueryHandler(directory, clock, new SwedishCalendar());
        var behavior = new AdminAuthorizationBehavior<GetAccountOverviewQuery, AccountOverviewDto>(user);

        await Should.ThrowAsync<ForbiddenException>(async () =>
            await behavior.Handle(new GetAccountOverviewQuery(), handler.Handle, Ct));

        await directory.DidNotReceiveWithAnyArgs().GetOverviewAsync(default!, Ct);
    }
}
