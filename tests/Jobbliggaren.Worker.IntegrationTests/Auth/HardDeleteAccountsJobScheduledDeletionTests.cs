using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Auth;

public partial class HardDeleteAccountsJobIntegrationTests
{
    [Fact]
    public async Task ScheduleThenRun_ShouldPreserveCiphertextWithoutKeys_ThenEraseOwnedGraphAndKeepControl()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var deletedAt = now.AddDays(-31);
        var target = await SeedActiveAccountAsync(ct, deletedAt.AddDays(-1));
        var control = await SeedActiveAccountAsync(ct);
        var targetProfile = await ProfileIdAsync(target, ct);
        var controlProfile = await ProfileIdAsync(control, ct);
        var targetApplication = await SeedEncryptedApplicationAsync(targetProfile, deletedAt.AddHours(-1), ct);
        var controlApplication = await SeedEncryptedApplicationAsync(controlProfile, now, ct);
        await SeedResumeForJobSeekerAsync(targetProfile, ct, deletedAt.AddHours(-1));
        await SeedParsedResumeForJobSeekerAsync(targetProfile, ct, deletedAt.AddHours(-1));
        await SeedResumeForJobSeekerAsync(controlProfile, ct);
        await SeedFeedbackAsync(targetProfile, FeedbackPage.Jobs, deletedAt.AddHours(-1), false, ct, withScreenshot: true);
        await SeedFeedbackAsync(controlProfile, FeedbackPage.Jobs, now, false, ct, withScreenshot: true);
        var targetScreenshot = await FeedbackScreenshotAsync(targetProfile, ct);
        var controlScreenshot = await FeedbackScreenshotAsync(controlProfile, ct);
        await SeedAuditEntryAsync(target, targetProfile.Value, ct);
        await SeedAuditEntryAsync(control, controlProfile.Value, ct);
        await LinkEveryKnownProviderAsync(target, ct);
        var ciphertext = await ResumeCiphertextAsync(targetProfile, ct);
        ciphertext.ShouldStartWith("v1:");
        var applicationCiphertext = await ApplicationCiphertextAsync(targetApplication, ct);
        applicationCiphertext.Count.ShouldBe(3);
        applicationCiphertext.ShouldAllBe(value => value.StartsWith("v1:", StringComparison.Ordinal));
        var unwraps = _fixture.Deks.UnwrapCount;

        var receipt = await ScheduleAtAsync(target, deletedAt, ct);

        _fixture.Deks.UnwrapCount.ShouldBe(unwraps);
        (await ResumeCiphertextAsync(targetProfile, ct)).ShouldBe(ciphertext);
        (await ApplicationCiphertextAsync(targetApplication, ct)).ShouldBe(applicationCiphertext);
        (await TimelineStampsAsync(targetApplication, ct)).ShouldHaveSingleItem().ShouldBe(receipt.DeletedAt);
        (await TimelineStampsAsync(controlApplication, ct)).ShouldHaveSingleItem().ShouldBeNull();
        (await CountFeedbackAsync(targetProfile, ct)).ShouldBe((1, 1, 1));
        await AssertFeedbackScreenshotUnchangedAsync(targetScreenshot, ct);
        await AssertFeedbackScreenshotUnchangedAsync(controlScreenshot, ct);
        using (var scope = _fixture.Services.CreateScope())
        {
            var access = await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(target, ct);
            access.ShouldNotBeNull().DeletedAt.ShouldBe(receipt.DeletedAt);
            access.CanAuthenticate.ShouldBeFalse();
            (await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
                .CountAsync(login => login.UserId == target, ct)).ShouldBe(0);
        }

        await RunJobAsync(now, ct);

        await AssertOwnedGraphAsync(target, targetProfile, present: false, ct);
        await AssertOwnedGraphAsync(control, controlProfile, present: true, ct);
        (await CountFeedbackAsync(targetProfile, ct)).ShouldBe((0, 0, 0));
        (await CountFeedbackAsync(controlProfile, ct)).ShouldBe((1, 1, 1));
        (await FeedbackScreenshotCountAsync(targetProfile, ct)).ShouldBe(0);
        await AssertFeedbackScreenshotUnchangedAsync(controlScreenshot, ct);
        using var verify = _fixture.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ParsedResumes.CountAsync(resume => resume.JobSeekerId == targetProfile, ct)).ShouldBe(0);
        var erasedAudit = await db.AuditLogEntries.AsNoTracking().SingleAsync(row => row.AggregateId == targetProfile.Value, ct);
        erasedAudit.UserId.ShouldBeNull();
        erasedAudit.IpAddress.ShouldBeNull();
        erasedAudit.UserAgent.ShouldBeNull();
        (await db.AuditLogEntries.AsNoTracking().SingleAsync(row => row.AggregateId == controlProfile.Value, ct))
            .UserId.ShouldBe(control);
    }

    [Fact]
    public async Task Run_ShouldRollBackWholeAccountAndContinue_WhenRealIdentityDeleteFails_ThenRetrySucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var deletedAt = now.AddDays(-31);
        var first = await SeedActiveAccountAsync(ct, deletedAt.AddDays(-1));
        var second = await SeedActiveAccountAsync(ct, deletedAt.AddDays(-1));
        var profiles = new Dictionary<Guid, JobSeekerId>
        {
            [first] = await ProfileIdAsync(first, ct),
            [second] = await ProfileIdAsync(second, ct),
        };
        var screenshots = new Dictionary<Guid, FeedbackScreenshot>();
        foreach (var (user, profile) in profiles)
        {
            await SeedEncryptedApplicationAsync(profile, deletedAt.AddHours(-1), ct);
            await SeedResumeForJobSeekerAsync(profile, ct, deletedAt.AddHours(-1));
            await SeedParsedResumeForJobSeekerAsync(profile, ct, deletedAt.AddHours(-1));
            await SeedFeedbackAsync(profile, FeedbackPage.Cv, deletedAt.AddHours(-1), false, ct, withScreenshot: true);
            screenshots[user] = await FeedbackScreenshotAsync(profile, ct);
            await SeedAuditEntryAsync(user, profile.Value, ct);
            await ScheduleAtAsync(user, deletedAt, ct);
            await AssertFeedbackScreenshotUnchangedAsync(screenshots[user], ct);
        }

        var run = Guid.NewGuid().ToString("N")[..20];
        await InstallIdentityDeleteFaultAsync(run, first, second, ct);
        try { await RunJobAsync(now, ct); }
        finally { await DropIdentityDeleteFaultAsync(run); }

        Guid failed;
        using (var scope = _fixture.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
            failed = (await identity.Users.AsNoTracking().Where(user => user.Id == first || user.Id == second)
                .Select(user => user.Id).ToListAsync(ct)).ShouldHaveSingleItem();
        }
        var completed = failed == first ? second : first;
        await AssertOwnedGraphAsync(failed, profiles[failed], present: true, ct);
        await AssertOwnedGraphAsync(completed, profiles[completed], present: false, ct);
        (await CountFeedbackAsync(profiles[failed], ct)).ShouldBe((1, 1, 1));
        (await CountFeedbackAsync(profiles[completed], ct)).ShouldBe((0, 0, 0));
        await AssertFeedbackScreenshotUnchangedAsync(screenshots[failed], ct);
        (await FeedbackScreenshotCountAsync(profiles[completed], ct)).ShouldBe(0);
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ParsedResumes.CountAsync(resume => resume.JobSeekerId == profiles[failed], ct)).ShouldBe(1);
            var audit = await db.AuditLogEntries.AsNoTracking()
                .SingleAsync(row => row.AggregateId == profiles[failed].Value, ct);
            audit.UserId.ShouldBe(failed);
            audit.IpAddress.ShouldNotBeNull();
            audit.UserAgent.ShouldNotBeNull();
        }

        await RunJobAsync(now, ct);

        await AssertOwnedGraphAsync(failed, profiles[failed], present: false, ct);
        (await CountFeedbackAsync(profiles[failed], ct)).ShouldBe((0, 0, 0));
        (await FeedbackScreenshotCountAsync(profiles[failed], ct)).ShouldBe(0);
        using var finalScope = _fixture.Services.CreateScope();
        (await finalScope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .SingleAsync(row => row.AggregateId == profiles[failed].Value, ct)).UserId.ShouldBeNull();
    }

    private async Task<FeedbackScreenshot> FeedbackScreenshotAsync(JobSeekerId profile, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var screenshot = await db.FeedbackScreenshots.AsNoTracking()
            .SingleAsync(row => row.JobSeekerId == profile, ct);
        var submission = await db.FeedbackSubmissions.AsNoTracking()
            .Where(row => row.Id == screenshot.SubmissionId)
            .Select(row => new { row.JobSeekerId, row.SubmittedAt }).SingleAsync(ct);
        screenshot.JobSeekerId.ShouldBe(submission.JobSeekerId);
        screenshot.SubmittedAt.ShouldBe(submission.SubmittedAt);
        return screenshot;
    }

    private async Task<int> FeedbackScreenshotCountAsync(JobSeekerId profile, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FeedbackScreenshots
            .CountAsync(screenshot => screenshot.JobSeekerId == profile, ct);
    }

    private async Task AssertFeedbackScreenshotUnchangedAsync(FeedbackScreenshot expected, CancellationToken ct)
    {
        var actual = await FeedbackScreenshotAsync(expected.JobSeekerId, ct);
        actual.Id.ShouldBe(expected.Id);
        actual.SubmissionId.ShouldBe(expected.SubmissionId);
        actual.JobSeekerId.ShouldBe(expected.JobSeekerId);
        actual.SubmittedAt.ShouldBe(expected.SubmittedAt);
        actual.Width.ShouldBe(expected.Width);
        actual.Height.ShouldBe(expected.Height);
        actual.Content.ToArray().ShouldBe(expected.Content.ToArray());
    }

    // The complete 1x1 encoder shape used by FeedbackScreenshotTests.AttachTo_NormalizedPng_CopiesTheSubmissionIdentityAndRetentionInstant.
    // FeedbackScreenshotNormalizerTests.NormalizeAsync_AStaticAllowedImage_EmitsOneLosslessRgba8Png pins the real writer's RGBA8 format.
    private static byte[] EncodeFeedbackOnePixelPng()
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteFeedbackPngChunk(png, "IHDR"u8, [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write([0, 20, 40, 60, 255]);
        WriteFeedbackPngChunk(png, "IDAT"u8, compressed.ToArray());
        WriteFeedbackPngChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteFeedbackPngChunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(kind);
        output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in kind.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        output.Write(word);
    }

    private async Task<JobSeekerId> ProfileIdAsync(Guid user, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobSeekers.IgnoreQueryFilters()
            .Where(profile => profile.UserId == user).Select(profile => profile.Id).SingleAsync(ct);
    }

    private async Task<AccountDeletionScheduled> ScheduleAtAsync(Guid target, DateTimeOffset deletedAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().JobSeekerId.ShouldBeNull();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([target], lifecycle: true, ct);
        var scheduler = ActivatorUtilities.CreateInstance<AccountDeletionScheduler>(scope.ServiceProvider, new FixedClock(deletedAt), Store(scope));
        var result = await scheduler.ScheduleAsync(target, administratorInitiated: true, ct);
        result.IsSuccess.ShouldBeTrue();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result.Value;
    }

    private async Task<string> ResumeCiphertextAsync(JobSeekerId profile, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQueryRaw<string>(
            "SELECT v.content_enc AS \"Value\" FROM resume_versions v JOIN resumes r ON r.id = v.resume_id WHERE r.job_seeker_id = {0}",
            profile.Value).SingleAsync(ct);
    }

    private async Task<Guid> SeedEncryptedApplicationAsync(JobSeekerId profile, DateTimeOffset createdAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>().SetOwner(profile);
        var dek = await scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>().GetOrCreateDataKeyAsync(profile, ct);
        CryptographicOperations.ZeroMemory(dek);
        var clock = new FixedClock(createdAt);
        var application = DomainApplication.Create(profile, JobAdId.New(), "Synthetic cover letter", null, clock).Value;
        application.AddNote("Synthetic application note", clock).IsSuccess.ShouldBeTrue();
        application.AddFollowUp(FollowUpChannel.Email, createdAt.AddDays(1), "Synthetic follow-up note", clock).IsSuccess.ShouldBeTrue();
        application.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();
        application.StatusChanges.Count.ShouldBe(1);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        return application.Id.Value;
    }

    private async Task<IReadOnlyList<string>> ApplicationCiphertextAsync(Guid application, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQueryRaw<string>(
            "SELECT cover_letter AS \"Value\" FROM applications WHERE id = {0} UNION ALL SELECT content FROM application_notes WHERE application_id = {0} UNION ALL SELECT note FROM follow_ups WHERE application_id = {0}",
            application).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<DateTimeOffset?>> TimelineStampsAsync(Guid application, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Applications.IgnoreQueryFilters()
            .Where(row => row.Id == new ApplicationId(application))
            .SelectMany(row => row.StatusChanges)
            .Select(change => change.DeletedAt).ToListAsync(ct);
    }

    private async Task AssertOwnedGraphAsync(Guid user, JobSeekerId profile, bool present, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.JobSeekers.IgnoreQueryFilters().AnyAsync(row => row.Id == profile, ct)).ShouldBe(present);
        (await db.Resumes.IgnoreQueryFilters().CountAsync(row => row.JobSeekerId == profile, ct)).ShouldBe(present ? 1 : 0);
        (await db.Applications.IgnoreQueryFilters().CountAsync(row => row.JobSeekerId == profile, ct)).ShouldBe(present ? 1 : 0);
        var ownedChildren = await db.Database.SqlQueryRaw<int>(
            "SELECT (SELECT count(*) FROM application_notes n JOIN applications a ON a.id = n.application_id WHERE a.job_seeker_id = {0})::int + (SELECT count(*) FROM follow_ups f JOIN applications a ON a.id = f.application_id WHERE a.job_seeker_id = {0})::int + (SELECT count(*) FROM application_status_changes s JOIN applications a ON a.id = s.application_id WHERE a.job_seeker_id = {0})::int AS \"Value\"",
            profile.Value).SingleAsync(ct);
        ownedChildren.ShouldBe(present ? 3 : 0);
        (await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AnyAsync(row => row.Id == user, ct)).ShouldBe(present);
        var keys = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM user_data_keys WHERE job_seeker_id = {0}", profile.Value).SingleAsync(ct);
        keys.ShouldBe(present ? 1 : 0);
    }

    private async Task InstallIdentityDeleteFaultAsync(string run, Guid first, Guid second, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string sequence = $"CREATE SEQUENCE hd_identity_seq_{run};";
        string function = $"""
            CREATE FUNCTION hd_identity_fn_{run}() RETURNS trigger AS $fn$
            BEGIN
                IF OLD.id IN ('{first}'::uuid, '{second}'::uuid)
                   AND nextval('hd_identity_seq_{run}') = 1 THEN
                    RAISE EXCEPTION 'one-shot Identity delete fault';
                END IF;
                RETURN OLD;
            END;
            $fn$ LANGUAGE plpgsql;
            """;
        string trigger = $"CREATE TRIGGER hd_identity_trg_{run} BEFORE DELETE ON identity.\"AspNetUsers\" FOR EACH ROW EXECUTE FUNCTION hd_identity_fn_{run}();";
        await db.Database.ExecuteSqlRawAsync(sequence, ct);
        await db.Database.ExecuteSqlRawAsync(function, ct);
        await db.Database.ExecuteSqlRawAsync(trigger, ct);
    }

    private async Task DropIdentityDeleteFaultAsync(string run)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string trigger = $"DROP TRIGGER IF EXISTS hd_identity_trg_{run} ON identity.\"AspNetUsers\";";
        string function = $"DROP FUNCTION IF EXISTS hd_identity_fn_{run}();";
        string sequence = $"DROP SEQUENCE IF EXISTS hd_identity_seq_{run};";
        await db.Database.ExecuteSqlRawAsync(trigger);
        await db.Database.ExecuteSqlRawAsync(function);
        await db.Database.ExecuteSqlRawAsync(sequence);
    }
}
