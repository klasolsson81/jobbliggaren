using System.Text.Json;
using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackScreenshot;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Feedback;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.GetFeedbackScreenshot;

public sealed class GetFeedbackScreenshotQueryHandlerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private Task<FeedbackScreenshotDto?> ReadAsync(FeedbackSubmissionId id) =>
        new GetFeedbackScreenshotQueryHandler(_db).Handle(new GetFeedbackScreenshotQuery(id.Value), Ct).AsTask();

    private async Task<FeedbackSubmissionId> SubmitAsync()
    {
        var clock = new FakeDateTimeProvider(Now);
        var owner = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock).Value;
        _db.JobSeekers.Add(owner);
        await _db.SaveChangesAsync(Ct);
        return (await FeedbackRows.SubmitAsync(_db, owner.Id, FeedbackPage.Jobs, 4, null, Now, Ct)).SubmissionId;
    }

    private async Task<byte[]> AttachAsync(FeedbackSubmissionId id)
    {
        using var normalizer = new FeedbackScreenshotNormalizer();
        var normalized = await normalizer.NormalizeAsync(await FeedbackScreenshotFixtures.PngAsync(Ct), Ct);
        normalized.IsSuccess.ShouldBeTrue();
        var submission = await _db.FeedbackSubmissions.SingleAsync(s => s.Id == id, Ct);
        var attached = FeedbackScreenshot.AttachTo(submission,
            normalized.Value.Content, normalized.Value.Width, normalized.Value.Height);
        attached.IsSuccess.ShouldBeTrue();
        _db.FeedbackScreenshots.Add(attached.Value);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
        return normalized.Value.Content.ToArray();
    }

    [Fact]
    public async Task Handle_ASubmissionWithAnImage_ReturnsTheStoredNormalizedPng()
    {
        var id = await SubmitAsync();
        var expected = await AttachAsync(id);
        var result = await ReadAsync(id);
        result.ShouldNotBeNull().Content.ShouldBe(expected);
        _db.ChangeTracker.Entries<FeedbackScreenshot>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ASubmissionWithoutAnImage_IsNull() =>
        (await ReadAsync(await SubmitAsync())).ShouldBeNull();

    [Fact]
    public async Task Handle_AnUnknownSubmission_IsNull() =>
        (await ReadAsync(FeedbackSubmissionId.New())).ShouldBeNull();

    [Fact]
    public async Task Handle_UnreachableOrphanedImage_DegradesToNull()
    {
        var id = await SubmitAsync();
        await AttachAsync(id);
        // Unreachable invariant breach: production cascades never leave an image without its parent.
        // This isolated deletion models corruption and asserts safe read-side degradation only.
        _db.FeedbackSubmissions.Remove(await _db.FeedbackSubmissions.SingleAsync(s => s.Id == id, Ct));
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
        (await _db.FeedbackScreenshots.CountAsync(Ct)).ShouldBe(1);
        (await ReadAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task Serialize_AndToString_DoNotExposeTheImage()
    {
        var id = await SubmitAsync();
        var png = await AttachAsync(id);
        var result = (await ReadAsync(id)).ShouldNotBeNull();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result));
        document.RootElement.TryGetProperty("Content", out _).ShouldBeFalse();
        result.ToString().ShouldNotContain(Convert.ToBase64String(png));
    }
}
