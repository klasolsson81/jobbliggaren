using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Commands.SetCompanyWatchFilter;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// Bevakning F4a (#803) — <see cref="SetCompanyWatchFilterCommandHandler"/>. The handler owns exactly
/// two decisions and both are pinned here: (1) an ALL-EMPTY selection means CLEAR (the transport
/// carries a form's shape; the domain's canonical "no filter" is the NULL column), and (2) cross-user
/// isolation — another user's watch is indistinguishable from an unknown id (NotFound, never 403,
/// which would confirm the id exists) and the attempt is logged (ADR 0031). Validation/normalization
/// belong to <see cref="WatchFilterSpec"/> and the soft-delete precondition to the aggregate; this
/// suite proves the handler SURFACES those results rather than re-deciding them.
/// Mirrors <see cref="UnfollowCompanyCommandHandlerTests"/> (fake DbContext + NSubstitute).
/// </summary>
public class SetCompanyWatchFilterCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public SetCompanyWatchFilterCommandHandlerTests() => _currentUser.UserId.Returns(_userId);

    private SetCompanyWatchFilterCommandHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _failedAccess);

    private static SetCompanyWatchFilterCommand Command(
        Guid watchId,
        IReadOnlyList<string>? municipalities = null,
        IReadOnlyList<string>? regions = null,
        bool onlyMatched = false) =>
        new(watchId, municipalities ?? [], regions ?? [], onlyMatched);

    // Seeds an ACTIVE watch for `userId`, optionally pre-filtered (the "had a filter" starting state).
    private async Task<CompanyWatch> SeedWatchAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        CancellationToken ct,
        WatchFilterSpec? filter = null,
        bool unfollowed = false)
    {
        var watch = CompanyWatch.Follow(
            userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        if (filter is not null)
            watch.SetFilter(filter).IsSuccess.ShouldBeTrue("SetFilter ska lyckas på en aktiv watch");
        if (unfollowed)
            watch.SoftDelete(_clock); // OBS: SoftDelete nollställer även Filter (RF-2 sub-bind)
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch;
    }

    private static async Task<CompanyWatch> ReadBackAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CompanyWatchId id, CancellationToken ct) =>
        await db.CompanyWatches.IgnoreQueryFilters().SingleAsync(w => w.Id == id, ct);

    // ── Happy path — full replace on an owned, active watch ──────────────────────────────────────

    [Fact]
    public async Task Handle_OnOwnedActiveWatch_SetsFilterFromSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedWatchAsync(db, _userId, ct);

        var result = await Handler(db).Handle(
            Command(watch.Id.Value, ["kommun_a"], ["lan_skane"], onlyMatched: true), ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = await ReadBackAsync(db, watch.Id, ct);
        stored.Filter.ShouldNotBeNull();
        stored.Filter!.Municipalities.ShouldBe(["kommun_a"]);
        stored.Filter.Regions.ShouldBe(["lan_skane"],
            "regionsaxeln måste nå domänen — annars tappas hela län-valet tyst");
        stored.Filter.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_OnWatchWithExistingFilter_ReplacesItWholesale()
    {
        // Full-replace, not merge: the command carries the user's CURRENT selection, so an axis the
        // user deselected must disappear (a merge would make deselection impossible).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var existing = WatchFilterSpec.Create(["kommun_old"], ["lan_old"], onlyMatched: true).Value;
        var watch = await SeedWatchAsync(db, _userId, ct, filter: existing);

        var result = await Handler(db).Handle(
            Command(watch.Id.Value, regions: ["lan_new"], onlyMatched: false), ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = await ReadBackAsync(db, watch.Id, ct);
        stored.Filter!.Municipalities.ShouldBeEmpty("den avvalda kommun-axeln ska försvinna");
        stored.Filter.Regions.ShouldBe(["lan_new"]);
        stored.Filter.OnlyMatched.ShouldBeFalse();
    }

    // ── The empty selection = CLEAR decision (the handler's one transport→domain mapping) ────────

    [Fact]
    public async Task Handle_WithEmptySelection_ClearsExistingFilterToNull()
    {
        // An all-empty selection is a VALUE of "make the filter be what I selected", and the domain's
        // canonical "no filter" is NULL — never an inert stored spec (WatchFilterSpec.Create would
        // reject that anyway). If this regressed, the user could never turn a filter OFF.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var existing = WatchFilterSpec.Create(["kommun_a"], ["lan_skane"], onlyMatched: true).Value;
        var watch = await SeedWatchAsync(db, _userId, ct, filter: existing);

        var result = await Handler(db).Handle(Command(watch.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        (await ReadBackAsync(db, watch.Id, ct)).Filter.ShouldBeNull(
            "tomt val = rensa filtret till den kanoniska NULL-formen");
    }

    [Fact]
    public async Task Handle_WithWhitespaceOnlySelection_ClearsFilter()
    {
        // code-reviewer Major (F4a): the handler used to count the RAW lists, so a payload whose only
        // entries are blank ([""] — what a form emits when the user removes the last chip) looked
        // NON-empty, went to WatchFilterSpec.Create, was normalized to nothing, failed the empty-spec
        // invariant, and came back as "Minst ett filter krävs" — a validation error thrown at a user who
        // was trying to REMOVE the filter, leaving the old one active with no way to clear it. Emptiness
        // is now asked of the Domain SSOT (IsEmptySelection), which decides on the NORMALIZED lists.
        // This test fails against the pre-fix handler.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var existing = WatchFilterSpec.Create(["kommun_a"], ["lan_skane"], onlyMatched: true).Value;
        var watch = await SeedWatchAsync(db, _userId, ct, filter: existing);

        var result = await Handler(db).Handle(
            Command(watch.Id.Value, municipalities: [""], regions: ["  "], onlyMatched: false), ct);

        result.IsSuccess.ShouldBeTrue(
            "ett blank-only-val ÄR ett tomt val — det ska rensa filtret, aldrig returnera valideringsfel");
        (await ReadBackAsync(db, watch.Id, ct)).Filter.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithEmptySelection_OnWatchWithoutFilter_IsIdempotentSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedWatchAsync(db, _userId, ct); // no filter to begin with

        var result = await Handler(db).Handle(Command(watch.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue("clear är idempotent — att rensa ett tomt filter är inget fel");
        (await ReadBackAsync(db, watch.Id, ct)).Filter.ShouldBeNull();
    }

    // ── Aggregate + VO results are SURFACED, not re-decided ──────────────────────────────────────

    [Fact]
    public async Task Handle_OnSoftDeletedWatch_WithSelection_SurfacesNotActive()
    {
        // The soft-delete precondition lives in the aggregate (SetFilter → CompanyWatch.NotActive).
        // The handler must surface it verbatim — not translate it into a 404, which would lie to the
        // owner about their own (unfollowed) watch.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedWatchAsync(db, _userId, ct, unfollowed: true);

        var result = await Handler(db).Handle(
            Command(watch.Id.Value, ["kommun_a"]), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.NotActive");
        (await ReadBackAsync(db, watch.Id, ct)).Filter.ShouldBeNull(
            "en borttagen bevakning får inte få ett filter bakvägen");
    }

    [Fact]
    public async Task Handle_OnSoftDeletedWatch_WithEmptySelection_Succeeds()
    {
        // ClearFilter is a deliberate idempotent no-op in ANY state (clearing never widens exposure),
        // so the clear path must NOT inherit SetFilter's active-watch precondition.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedWatchAsync(db, _userId, ct, unfollowed: true);

        var result = await Handler(db).Handle(Command(watch.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        (await ReadBackAsync(db, watch.Id, ct)).Filter.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithInvalidRegionConceptId_SurfacesVoValidationError()
    {
        // The VO owns the value rules; the handler must return its DomainError rather than throw or
        // silently drop the bad axis.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = await SeedWatchAsync(db, _userId, ct);

        var result = await Handler(db).Handle(
            Command(watch.Id.Value, regions: ["inte giltig"]), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.InvalidRegion");
        (await ReadBackAsync(db, watch.Id, ct)).Filter.ShouldBeNull();
    }

    // ── Owner scoping / IDOR (BC-6) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_OnAnotherUsersWatch_ReturnsNotFoundAndLogsCrossUserAttempt()
    {
        // BC-6 IDOR pin: a 403 (or any answer that differs from the unknown-id answer) would turn the
        // endpoint into an existence oracle for another user's watch ids. The response is NotFound —
        // identical to an unknown id — and the attempt is logged for failed-access detection.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var otherUsersWatch = await SeedWatchAsync(db, Guid.NewGuid(), ct);

        var result = await Handler(db).Handle(
            Command(otherUsersWatch.Id.Value, ["kommun_a"], onlyMatched: true), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.NotFound");
        _failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatch", otherUsersWatch.Id.Value, _userId, "SetCompanyWatchFilter");
        (await ReadBackAsync(db, otherUsersWatch.Id, ct)).Filter.ShouldBeNull(
            "en annan användares bevakning får inte muteras");
    }

    [Fact]
    public async Task Handle_OnUnknownId_ReturnsNotFoundWithoutCrossUserLog()
    {
        // The mirror half of the IDOR pin: an unknown id must NOT log a cross-user attempt (that would
        // fill the failed-access signal with noise from plain typos and blunt a real detection).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(Command(Guid.NewGuid(), ["kommun_a"]), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.NotFound");
        _failedAccess.DidNotReceiveWithAnyArgs().LogCrossUserAttempt(default!, default, default, default!);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new SetCompanyWatchFilterCommandHandler(db, anon, _failedAccess)
            .Handle(Command(Guid.NewGuid(), ["kommun_a"]), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.Unauthorized");
    }
}
