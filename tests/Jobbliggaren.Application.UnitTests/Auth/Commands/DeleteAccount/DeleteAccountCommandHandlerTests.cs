using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.Commands.DeleteAccount;

/// <summary>
/// GDPR cascade-soft-delete-handler (CLAUDE.md §5.4). Branch-täckande tester
/// för DeleteAccountCommandHandler:
///   1. Ej autentiserad           → Failure "Auth.NotAuthenticated"
///   2. Ingen JobSeeker för userId → Failure "Auth.JobSeekerNotFound"
///   3. Redan soft-deletad         → idempotent Success utan ny mutation
///   4. Happy path med barn-aggregat → hela ägar-trädet soft-deletat
/// Den fjärde är den säkerhetskritiska assertionen: inget user-ägt aggregat
/// (ansökningar + FollowUp/Note-barn, CV + versioner) lämnas oraderat = ingen
/// kvarvarande PII.
/// </summary>
public class DeleteAccountCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static ICurrentUser AuthenticatedAs(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenNotAuthenticated_ReturnsNotAuthenticatedFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new DeleteAccountCommandHandler(db, currentUser, Clock);

        var result = await handler.Handle(new DeleteAccountCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenNoJobSeekerForUser_ReturnsJobSeekerNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = AuthenticatedAs(Guid.NewGuid());

        var handler = new DeleteAccountCommandHandler(db, currentUser, Clock);

        var result = await handler.Handle(new DeleteAccountCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.JobSeekerNotFound");
        // #203: a missing JobSeeker is a NotFound (→404), not a Validation (→400).
        result.Error.Kind.ShouldBe(Jobbliggaren.Domain.Common.ErrorKind.NotFound);
    }

    [Fact]
    public async Task DeleteAccountCommandHandler_WhenJobSeekerAlreadySoftDeleted_ReturnsIdempotentSuccessWithoutNewMutation()
    {
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Raderad Användare", Clock).Value;
        seeker.SoftDelete(Clock);
        var firstDeletedAt = seeker.DeletedAt;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        // Klockan stegas fram — om handlern muterade igen skulle DeletedAt
        // skrivas om med detta senare värde.
        var laterClock = new FakeDateTimeProvider(Clock.UtcNow.AddDays(1));
        var handler = new DeleteAccountCommandHandler(db, AuthenticatedAs(userId), laterClock);

        var result = await handler.Handle(new DeleteAccountCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);

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

        var seeker = JobSeeker.Register(userId, "Aktiv Användare", Clock).Value;

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

        var handler = new DeleteAccountCommandHandler(db, AuthenticatedAs(userId), Clock);

        var result = await handler.Handle(new DeleteAccountCommand(), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);

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
}
