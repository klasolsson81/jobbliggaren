using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.Commands.DeleteAccount;

/// <summary>
/// GDPR cascade-soft-delete-handler (CLAUDE.md §5.4). Branch-täckande tester
/// för DeleteAccountCommandHandler:
///   1. Ej autentiserad           → Failure "Auth.NotAuthenticated"
///   2. Ingen JobSeeker för userId → Failure "Auth.JobSeekerNotFound"
///   3. Already soft-deleted       → Gone without a new mutation or success audit
///   4. Happy path med barn-aggregat → hela ägar-trädet soft-deletat
/// Den fjärde är den säkerhetskritiska assertionen: inget user-ägt aggregat
/// (ansökningar + FollowUp/Note-barn, CV + versioner) lämnas oraderat = ingen
/// kvarvarande PII.
///
/// These direct handler tests bypass ReauthenticationBehavior and supply no grant. The ordinary-user
/// removal port permits only this fixture's caller. Step-up is covered by ReauthenticationServiceTests,
/// ReauthenticationBehaviorTests and DeleteMeTests.
/// </summary>
public class DeleteAccountCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private readonly IExternalLoginEraser _eraser = Substitute.For<IExternalLoginEraser>();

    private static ICurrentUser AuthenticatedAs(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.AccessRevision.Returns(0L);
        return currentUser;
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenNotAuthenticated_ReturnsNotAuthenticatedFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = CreateHandler(db, currentUser, Clock);

        var result = await handler.Handle(new DeleteAccountCommand(null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
        await _eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenNoJobSeekerForUser_ReturnsJobSeekerNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = AuthenticatedAs(Guid.NewGuid());

        var handler = CreateHandler(db, currentUser, Clock);

        var result = await handler.Handle(new DeleteAccountCommand(null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.JobSeekerNotFound");
        // #203: a missing JobSeeker is a NotFound (→404), not a Validation (→400).
        result.Error.Kind.ShouldBe(Jobbliggaren.Domain.Common.ErrorKind.NotFound);
        await _eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenJobSeekerAlreadySoftDeleted_ReturnsGoneWithoutNewMutation()
    {
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        seeker.SoftDelete(Clock);
        var firstDeletedAt = seeker.DeletedAt;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        // Klockan stegas fram — om handlern muterade igen skulle DeletedAt
        // skrivas om med detta senare värde.
        var laterClock = new FakeDateTimeProvider(Clock.UtcNow.AddDays(1));
        var currentUser = AuthenticatedAs(userId);
        var handler = CreateHandler(db, currentUser, laterClock);

        var result = await handler.Handle(new DeleteAccountCommand(null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(Jobbliggaren.Domain.Common.ErrorKind.Gone);
        await _eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, TestContext.Current.CancellationToken);

        // Idempotens: ingen ny soft-delete-mutation — DeletedAt oförändrat.
        var reloaded = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstAsync(js => js.UserId == userId, CancellationToken.None);
        reloaded.DeletedAt.ShouldBe(firstDeletedAt);
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenActiveJobSeekerWithChildren_CascadeSoftDeletesEntireOwnershipTree()
    {
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;

        // Två ansökningar; en med FollowUp + Note-barn (Application.SoftDelete
        // cascadar internt till FollowUp + ApplicationNote).
        var app1 = DomainApplication.Create(seeker.Id, null, "Brev 1", null, Clock).Value;
        app1.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(3), "Ringa upp", Clock);
        app1.AddNote("Internt anteckning", Clock);
        var app2 = DomainApplication.Create(seeker.Id, null, null, null, Clock).Value;

        // Två CV; vart och ett får en Master-version av factoryn
        // (Resume.SoftDelete cascadar internt till ResumeVersions).
        var resume1 = Resume.Create(seeker.Id, "Standard-CV", "Klas Olsson", Clock).Value;
        var resume2 = Resume.Create(seeker.Id, "Backend-CV", "Klas Olsson", Clock).Value;

        db.JobSeekers.Add(seeker);
        db.Applications.AddRange(app1, app2);
        db.Resumes.AddRange(resume1, resume2);
        await db.SaveChangesAsync(CancellationToken.None);

        // #482/#505 — LOAD-BEARING: detach the seeded graph so the handler's query
        // re-materialises Applications from the store with EMPTY FollowUps/Notes, exactly
        // as a fresh request-scoped DbContext does in production. Without this Clear the
        // seeded app1 stays in the identity map with its children still in memory, so the
        // cascade "works" even without .Include — the false pass this test used to give.
        // The child-DeletedAt assertions below therefore go RED against a no-Include
        // handler and only pass because of the .Include(FollowUps).Include(Notes) fix.
        db.ChangeTracker.Clear();

        var currentUser = AuthenticatedAs(userId);
        var handler = CreateHandler(db, currentUser, Clock);

        var result = await handler.Handle(new DeleteAccountCommand(null), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ProfileId.ShouldBe(seeker.Id.Value);
        result.Value.UserId.ShouldBe(userId);
        await _eraser.Received(1).EraseAllAsync(userId, CancellationToken.None);

        // GDPR cascade-completeness: bevisa att INGET user-ägt aggregat lämnas
        // oraderat. IgnoreQueryFilters() krävs eftersom soft-deletade rader
        // annars filtreras bort av global query filter.
        var reloadedSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstAsync(js => js.UserId == userId, CancellationToken.None);
        reloadedSeeker.DeletedAt.ShouldNotBeNull();

        var reloadedApps = await db.Applications
            .IgnoreQueryFilters()
            .Include(a => a.FollowUps)
            .Include(a => a.Notes)
            .Where(a => a.JobSeekerId == seeker.Id)
            .ToListAsync(CancellationToken.None);
        reloadedApps.Count.ShouldBe(2);
        reloadedApps.ShouldAllBe(a => a.DeletedAt != null);
        // Barn-aggregat under ansökningarna är också soft-deletade.
        reloadedApps.SelectMany(a => a.FollowUps).ShouldAllBe(f => f.DeletedAt != null);
        reloadedApps.SelectMany(a => a.Notes).ShouldAllBe(n => n.DeletedAt != null);
        // Sanity: barnen fanns faktiskt (annars vore ShouldAllBe vakuöst sant).
        reloadedApps.SelectMany(a => a.FollowUps).Count().ShouldBe(1);
        reloadedApps.SelectMany(a => a.Notes).Count().ShouldBe(1);

        var reloadedResumes = await db.Resumes
            .IgnoreQueryFilters()
            .Include(r => r.Versions)
            .Where(r => r.JobSeekerId == seeker.Id)
            .ToListAsync(CancellationToken.None);
        reloadedResumes.Count.ShouldBe(2);
        reloadedResumes.ShouldAllBe(r => r.DeletedAt != null);
        // Varje CV-version (inkl. Master) är soft-deletad — ingen kvar-PII.
        reloadedResumes.SelectMany(r => r.Versions).ShouldAllBe(v => v.DeletedAt != null);
        reloadedResumes.SelectMany(r => r.Versions).Count().ShouldBe(2);
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenProviderErasureFails_PropagatesBeforeTheOuterSuccessSave()
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var currentUser = AuthenticatedAs(userId);
        var fault = new InvalidOperationException("provider-erasure-fault");
        _eraser.EraseAllAsync(userId, CancellationToken.None).ThrowsAsync(fault);
        var handler = CreateHandler(db, currentUser, Clock);

        var actual = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.Handle(new DeleteAccountCommand(null), CancellationToken.None).AsTask());

        actual.ShouldBeSameAs(fault);
        await _eraser.Received(1).EraseAllAsync(userId, CancellationToken.None);
        db.AuditLogEntries.Local.ShouldBeEmpty();
        // A failed handler never reaches the outer success Save/Audit. Physical rollback is pinned by
        // DeleteMeTests.POST_me_delete_whose_erasure_fails_rolls_back_profile_and_keeps_session_and_links.
        db.ChangeTracker.Clear();
        (await db.JobSeekers.IgnoreQueryFilters().SingleAsync(js => js.UserId == userId, CancellationToken.None))
            .DeletedAt.ShouldBeNull();
    }

    private DeleteAccountCommandHandler CreateHandler(
        IAppDbContext db, ICurrentUser currentUser, Jobbliggaren.Domain.Common.IDateTimeProvider clock)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var reader = AccountAccessTestKit.ReaderFromProfiles(db,
            id => id == userId ? "owner@example.test" : null);
        var writer = AccountAccessTestKit.OrdinaryRemoval(currentUser);
        writer.AdvanceDeletionAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AccountAccessTestKit.Account(userId, "owner@example.test") with
            {
                AccessRevision = 1,
                CredentialCutoff = 1,
            });
        return new DeleteAccountCommandHandler(new AccountDeletionScheduler(db, clock, reader, writer, _eraser), currentUser);
    }
}
