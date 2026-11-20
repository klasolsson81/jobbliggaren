using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// ADR 0031, ADR 0188 (#2265) — a quick-link write on another user's search answers the same NotFound as an unknown id,
/// changes nothing, and is logged as a cross-user attempt; an unknown id is not one. The real handlers on real Postgres,
/// each search stored by the real capturer (<see cref="RecentSearchQuickLinkHarness"/>).
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class RecentSearchQuickLinkCrossUserTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly RecentSearchQuickLinkHarness _h = new();
    private readonly Guid _intruder = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        await _h.InitializeAsync(postgres);
        await _h.RegisterAsync(_intruder);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task Mark_ShouldBeNotFound_AndLogTheCrossUserAttempt_WhenTheSearchIsAnotherUsers()
    {
        await _h.CaptureAsync("agarenssokning");
        var owners = (await _h.RowsAsync()).ShouldHaveSingleItem().Id;

        var crossUser = await _h.MarkAsync(owners, asUser: _intruder);
        var unknown = await _h.MarkAsync(Guid.NewGuid(), asUser: _intruder);

        crossUser.Error.Kind.ShouldBe(ErrorKind.NotFound);
        (unknown.Error.Kind, unknown.Error.Code).ShouldBe(
            (crossUser.Error.Kind, crossUser.Error.Code), "the two answers cannot be told apart");
        _h.FailedAccess.Received(1).LogCrossUserAttempt("RecentJobSearch", owners, _intruder, "MarkRecentSearchQuickLink");
        _h.FailedAccess.ReceivedCalls().Count().ShouldBe(1, "an unknown id is not a cross-user attempt");
        (await _h.RowsAsync()).ShouldHaveSingleItem().QuickLinkSlot.ShouldBeNull();
    }

    [Fact]
    public async Task Unmark_ShouldBeNotFound_AndLogTheCrossUserAttempt_AndLeaveTheMark_WhenTheSearchIsAnotherUsers()
    {
        await _h.CaptureAsync("agarenssokning");
        var owners = (await _h.RowsAsync()).ShouldHaveSingleItem().Id;
        (await _h.MarkAsync(owners)).IsSuccess.ShouldBeTrue();

        var crossUser = await _h.UnmarkAsync(owners, asUser: _intruder);
        var unknown = await _h.UnmarkAsync(Guid.NewGuid(), asUser: _intruder);

        crossUser.Error.Kind.ShouldBe(ErrorKind.NotFound);
        (unknown.Error.Kind, unknown.Error.Code).ShouldBe(
            (crossUser.Error.Kind, crossUser.Error.Code), "the two answers cannot be told apart");
        _h.FailedAccess.Received(1).LogCrossUserAttempt("RecentJobSearch", owners, _intruder, "UnmarkRecentSearchQuickLink");
        _h.FailedAccess.ReceivedCalls().Count().ShouldBe(1, "an unknown id is not a cross-user attempt");
        (await _h.RowsAsync()).ShouldHaveSingleItem().QuickLinkSlot.ShouldBe(1);
    }
}
