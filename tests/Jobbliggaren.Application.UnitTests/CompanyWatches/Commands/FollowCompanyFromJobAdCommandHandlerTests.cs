using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// #455 — the follow-from-card handler resolves org.nr via <see cref="IJobAdEmployerReader"/> (faked
/// here — the facet column is Postgres-computed, so the real resolution is Testcontainers-only)
/// and delegates to the shared idempotent <c>CompanyWatchFollowExecutor</c>. These prove the branching:
/// resolved org.nr → follow, absent ad → 404, null org.nr → 400, plus idempotency + resurrect + the
/// sole-prop-still-followable D8 guarantee.
/// </summary>
public class FollowCompanyFromJobAdCommandHandlerTests
{
    private readonly IJobAdEmployerReader _employerReader = Substitute.For<IJobAdEmployerReader>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDbExceptionInspector _inspector = Substitute.For<IDbExceptionInspector>();
    private readonly IProtectedIdentityTokenizer _tokenizer = Substitute.For<IProtectedIdentityTokenizer>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _jobAdId = Guid.NewGuid();
    private const string LegalOrgNr = "5592804784";        // third digit 9 → legal entity
    private const string SoleProprietorOrgNr = "9001011234"; // personnummer-shaped
    // Deterministic, distinct-from-plaintext test token (64-char ⇒ IsPersonnummerShaped true, like a
    // real HMAC). #544: a sole-prop org.nr is stored as this token, never plaintext.
    private static string FakeToken(string orgNr) => "hmac" + orgNr.PadLeft(60, '0');

    public FollowCompanyFromJobAdCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _tokenizer.Tokenize(Arg.Any<string>()).Returns(ci => FakeToken(ci.Arg<string>()));
    }

    private void ReaderReturns(string? orgNr) =>
        _employerReader
            .GetOrganizationNumbersByJobAdIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(_jobAdId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?> { [_jobAdId] = orgNr });

    private void ReaderReturnsNoAd() =>
        _employerReader
            .GetOrganizationNumbersByJobAdIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?>());

    private FollowCompanyFromJobAdCommandHandler Handler(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _employerReader, _currentUser, _clock, _inspector, _tokenizer);

    [Fact]
    public async Task Handle_WhenAdHasOrgNumber_CreatesActiveWatchAndReturnsId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        ReaderReturns(LegalOrgNr);

        var result = await Handler(db).Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsSuccess.ShouldBeTrue();
        var watch = await db.CompanyWatches.SingleAsync(ct);
        watch.UserId.ShouldBe(_userId);
        watch.OrganizationNumber.Value.ShouldBe(LegalOrgNr);
        watch.DeletedAt.ShouldBeNull();
        result.Value.ShouldBe(watch.Id.Value);
    }

    [Fact]
    public async Task Handle_WhenSoleProprietorOrgNumber_StillFollows_D8_NotAFeatureGap()
    {
        // ADR 0087 D8 rejected excluding personnummer-shaped org.nr from following (the enskild-firma
        // feature gap). The IsPersonnummerShaped heuristic gates SURFACING, never the follow itself.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        ReaderReturns(SoleProprietorOrgNr);

        var result = await Handler(db).Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsSuccess.ShouldBeTrue();
        var watch = await db.CompanyWatches.SingleAsync(ct);
        // #544 (ADR 0090 D5): the sole-prop org.nr (= a personnummer) is stored HMAC-tokenised at
        // rest, never the plaintext — while the follow itself is still allowed (D8: no feature gap).
        watch.OrganizationNumber.Value.ShouldBe(FakeToken(SoleProprietorOrgNr));
        watch.OrganizationNumber.Value.ShouldNotBe(SoleProprietorOrgNr);
        watch.OrganizationNumber.IsPersonnummerShaped().ShouldBeTrue(); // still masked at surfacing
    }

    [Fact]
    public async Task Handle_WhenAlreadyFollowed_IsIdempotentSingleRowSameId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        ReaderReturns(LegalOrgNr);
        var handler = Handler(db);

        var first = await handler.Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);
        var second = await handler.Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        first.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);
        (await db.CompanyWatches.IgnoreQueryFilters().CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RefollowAfterUnfollow_ResurrectsSameRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var existing = CompanyWatch.Follow(_userId, OrganizationNumber.Create(LegalOrgNr).Value, _clock).Value;
        existing.SoftDelete(FakeDateTimeProvider.Default);
        db.CompanyWatches.Add(existing);
        await db.SaveChangesAsync(ct);
        ReaderReturns(LegalOrgNr);

        var refollowClock = new FakeDateTimeProvider(_clock.UtcNow.AddDays(10));
        var result = await new FollowCompanyFromJobAdCommandHandler(
                db, _employerReader, _currentUser, refollowClock, _inspector, _tokenizer)
            .Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(existing.Id.Value); // SAME row resurrected
        (await db.CompanyWatches.IgnoreQueryFilters().CountAsync(ct)).ShouldBe(1);
        var watch = await db.CompanyWatches.IgnoreQueryFilters().SingleAsync(ct);
        watch.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenAdNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        ReaderReturnsNoAd();

        var result = await Handler(db).Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.NotFound");
        (await db.CompanyWatches.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenAdHasNoOrgNumber_ReturnsValidationError()
    {
        // B2 not-re-ingested ad → org.nr null → not followable → 400 (CTO deldom 5).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        ReaderReturns(null);

        var result = await Handler(db).Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.EmployerOrganizationNumberMissing");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        (await db.CompanyWatches.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenAdOrgNumberIsMalformed_ReturnsValidationError()
    {
        // The STORED column is derived from raw_payload — a malformed (non-10-digit) value is possible.
        // OrganizationNumber.Create rejects it → not followable → 400 (same branch as NULL, CTO deldom 5).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        ReaderReturns("123"); // not 10 digits

        var result = await Handler(db).Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.EmployerOrganizationNumberMissing");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        (await db.CompanyWatches.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await new FollowCompanyFromJobAdCommandHandler(
                db, _employerReader, anon, _clock, _inspector, _tokenizer)
            .Handle(new FollowCompanyFromJobAdCommand(_jobAdId), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatch.Unauthorized");
        // The employer reader is never consulted when unauthenticated (no wasted round-trip).
        await _employerReader.DidNotReceiveWithAnyArgs()
            .GetOrganizationNumbersByJobAdIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }
}
