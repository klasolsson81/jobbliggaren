using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Commands.DeleteResume;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands;

public class DeleteResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // Opaque sealed-content stand-in — the delete path must NEVER read these bytes (DEK-free
    // erasure, §5 minimisation). They exist only so a captured original is a realistic row.
    private static readonly byte[] SealedBytes = [0x01, 0xAA, 0xBB, 0xCC];

    public DeleteResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    // Rich, valid content that passes Resume.ValidateContent — drives a CreateFromParsed
    // (Import-origin) Resume whose SourceParsedResumeId is set by construction.
    private static ResumeContent GapFilledContent() => new(
        new PersonalInfo("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
        experiences: new[]
        {
            new Experience("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster."),
        },
        skills: new[] { new Skill("C#", 8) },
        summary: "Erfaren backend-utvecklare.");

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    [Fact]
    public async Task Handle_WithValidCommand_SoftDeletesResumeAndCascadesToVersions()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new DeleteResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeCommand(resume.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // Soft-delete: DeletedAt sätts på aggregate och cascadar till versioner
        // (Resume.SoftDelete iterar _versions). Vi inspekterar in-memory-instansen
        // — global query filter på ResumeVersion gömmer dem från re-load.
        resume.GetType().GetProperty("DeletedAt")!.GetValue(resume).ShouldNotBeNull();
        foreach (var version in resume.Versions)
        {
            version.DeletedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new DeleteResumeCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeCommand(Guid.NewGuid());

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeCommand(Guid.NewGuid());

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var resume = await SeedResumeAsync(db, otherUserId);

        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeCommand(resume.Id.Value);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    // ---------------------------------------------------------------
    // F6 Prompt 3 — cascade-unset av JobSeeker.PrimaryResumeId
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_DeletingPrimaryResume_UnsetsJobSeekerPrimaryResumeId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        seeker.SetPrimaryResume(resume.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        seeker.PrimaryResumeId.ShouldBe(resume.Id);

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeCommand(resume.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_DeletingNonPrimaryResume_DoesNotChangeJobSeekerPrimaryResumeId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var primary = Resume.Create(seeker.Id, "Primary", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        var other = Resume.Create(seeker.Id, "Other", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(primary);
        db.Resumes.Add(other);
        await db.SaveChangesAsync(CancellationToken.None);
        seeker.SetPrimaryResume(primary.Id, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeCommand(other.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBe(primary.Id);
    }

    // ---------------------------------------------------------------
    // Fas 4b PR-9c (ADR 0100 §D5 / ADR 0103; CTO-bind F1=L-B, F3-ii) — deleting a CV cascades the
    // erasure of the promoted original file it was created from. The link is
    // Resume.SourceParsedResumeId → ResumeFile.ParsedResumeId; the handler projects ONLY the file
    // id and Removes a key-only stub, so the DELETE rides THIS UnitOfWork's SaveChanges (atomic
    // with the soft-delete). ExecuteDeleteAsync is unsupported on InMemory, but tracked Remove IS —
    // so the unit-testable seam is exactly this cascade.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_DeletingImportedResume_CascadeErasesCoupledOriginalFile()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var parsedId = new ParsedResumeId(Guid.NewGuid());
        var resume = Resume.CreateFromParsed(
            seeker.Id, "Importerat CV", GapFilledContent(), parsedId, FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);

        var file = ResumeFile.CaptureOriginal(
            seeker.Id, parsedId, SealedBytes, "application/pdf", "cv.pdf", SealedBytes.Length, false,
            FakeDateTimeProvider.Default).Value;
        db.ResumeFiles.Add(file);
        await db.SaveChangesAsync(CancellationToken.None);

        // A fresh request context would never have the original tracked; the handler only ever
        // PROJECTS its id then Removes a key-only stub. Detach the seeded row so the stub-Remove
        // does not collide with an already-tracked instance (ChangeTracker identity-map conflict).
        db.Entry(file).State = EntityState.Detached;

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var result = await handler.Handle(new DeleteResumeCommand(resume.Id.Value), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The CV is soft-deleted (same tracked instance the handler mutated) ...
        resume.DeletedAt.ShouldNotBeNull();
        // ... and its coupled original was HARD-deleted in the same SaveChanges (immediate erasure).
        (await db.ResumeFiles.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_DeletingTemplateResume_LeavesUnrelatedOriginalUntouched()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        // A Template-origin CV has a null SourceParsedResumeId (F2 back-compat) → cascade skipped.
        var resume = Resume.Create(seeker.Id, "Mall-CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);

        // An original owned by the same user but tied to a DIFFERENT (unpromoted) parse — it must
        // survive, because a null source link never triggers the cascade.
        var file = ResumeFile.CaptureOriginal(
            seeker.Id, new ParsedResumeId(Guid.NewGuid()), SealedBytes, "application/pdf", "cv.pdf",
            SealedBytes.Length, false, FakeDateTimeProvider.Default).Value;
        db.ResumeFiles.Add(file);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var result = await handler.Handle(new DeleteResumeCommand(resume.Id.Value), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.DeletedAt.ShouldNotBeNull();
        (await db.ResumeFiles.CountAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ImportedResumeWithNoCapturedOriginal_SoftDeletesWithoutThrowing()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        // SourceParsedResumeId is set, but no ResumeFile was ever captured for it (pre-PR-9a
        // import, or a body-flagged upload). The cascade query returns an empty set → no Remove,
        // no throw (F2 residual: those originals stay erasable via account-hard-delete only).
        var parsedId = new ParsedResumeId(Guid.NewGuid());
        var resume = Resume.CreateFromParsed(
            seeker.Id, "Importerat CV", GapFilledContent(), parsedId, FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var result = await handler.Handle(new DeleteResumeCommand(resume.Id.Value), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_DoesNotEraseTheirOriginal()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var otherSeeker = JobSeeker.Register(otherUserId, "Other User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(otherSeeker);

        var parsedId = new ParsedResumeId(Guid.NewGuid());
        var otherResume = Resume.CreateFromParsed(
            otherSeeker.Id, "Andras CV", GapFilledContent(), parsedId, FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(otherResume);

        var otherFile = ResumeFile.CaptureOriginal(
            otherSeeker.Id, parsedId, SealedBytes, "application/pdf", "cv.pdf", SealedBytes.Length, false,
            FakeDateTimeProvider.Default).Value;
        db.ResumeFiles.Add(otherFile);

        // The current user has their own JobSeeker but does NOT own the target resume.
        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        // IDOR fail-closed: the load resolves nothing for the current user → NotFound, so the
        // cascade is never reached and the other user's original is left intact.
        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(new DeleteResumeCommand(otherResume.Id.Value), CancellationToken.None).AsTask());

        (await db.ResumeFiles.CountAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_OriginalWithMatchingParsedIdButDifferentOwner_IsNotErased()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var parsedId = new ParsedResumeId(Guid.NewGuid());
        var resume = Resume.CreateFromParsed(
            seeker.Id, "Importerat CV", GapFilledContent(), parsedId, FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);

        // A ResumeFile whose ParsedResumeId matches the deleted CV's SourceParsedResumeId, but
        // whose owner differs. The cascade predicate scopes on BOTH keys (defence-in-depth, IDOR
        // parity), so this cross-owner row must NOT be erased.
        var foreignOwnerFile = ResumeFile.CaptureOriginal(
            new JobSeekerId(Guid.NewGuid()), parsedId, SealedBytes, "application/pdf", "cv.pdf",
            SealedBytes.Length, false, FakeDateTimeProvider.Default).Value;
        db.ResumeFiles.Add(foreignOwnerFile);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var result = await handler.Handle(new DeleteResumeCommand(resume.Id.Value), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.DeletedAt.ShouldNotBeNull();
        (await db.ResumeFiles.CountAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public void DeleteResumeCommand_DoesNotRequireFieldEncryptionKey_SoTheEraseIsDekFree()
    {
        // PR-9c DEK-free proof (ADR 0103, §5 minimisation): the cascade projects ONLY the file id
        // and Removes a key-only stub — it never materialises or decrypts the sealed bytea, so the
        // command must NOT carry the IRequiresFieldEncryptionKey opt-in (that marker warms an owner
        // DEK via FieldEncryptionKeyPrefetchBehavior before the handler runs). Contrast pin: the
        // promote path DOES require it (it writes encrypted Master content). Fails if a future edit
        // makes the delete path read encrypted bytes.
        typeof(IRequiresFieldEncryptionKey).IsAssignableFrom(typeof(DeleteResumeCommand)).ShouldBeFalse();
        typeof(IRequiresFieldEncryptionKey).IsAssignableFrom(typeof(PromoteParsedResumeCommand)).ShouldBeTrue();
    }
}
