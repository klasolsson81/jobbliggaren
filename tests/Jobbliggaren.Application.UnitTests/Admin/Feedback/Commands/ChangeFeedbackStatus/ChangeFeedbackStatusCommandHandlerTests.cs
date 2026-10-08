using Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.ChangeFeedbackStatus;

/// <summary>
/// #1979 — an administrator moves a submission between Ny, Pågår, Åtgärdad and Avstår. The handler changes the tracked
/// row; <c>UnitOfWorkBehavior</c> saves it, which the tests do in its place.
/// </summary>
public sealed class ChangeFeedbackStatusCommandHandlerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = T0.AddDays(2);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly JobSeekerId _owner = new(Guid.NewGuid());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>The handler's call, then the save <c>UnitOfWorkBehavior</c> makes after it.</summary>
    private async Task<Result> ChangeAsync(Guid id, FeedbackStatus to)
    {
        var result = await new ChangeFeedbackStatusCommandHandler(_db, new FakeDateTimeProvider(Later))
            .Handle(new ChangeFeedbackStatusCommand(id, to), Ct);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
        return result;
    }

    private Task<FeedbackSubmission> StoredAsync(FeedbackRows.Saved saved) =>
        _db.FeedbackSubmissions.AsNoTracking().SingleAsync(s => s.Id == saved.SubmissionId, Ct);

    [Theory]
    [InlineData(FeedbackStatus.InProgress)]
    [InlineData(FeedbackStatus.Resolved)]
    [InlineData(FeedbackStatus.Declined)]
    public async Task Handle_AnotherStatus_IsSavedWithTheTimeOfTheChange(FeedbackStatus to)
    {
        var saved = await FeedbackRows.SubmitAsync(_db, _owner, FeedbackPage.Jobs, 3, null, T0, Ct);

        var result = await ChangeAsync(saved.SubmissionId.Value, to);

        result.IsSuccess.ShouldBeTrue();
        var stored = await StoredAsync(saved);
        stored.Status.ShouldBe(to);
        stored.StatusChangedAt.ShouldBe(Later);
    }

    [Fact]
    public async Task Handle_AClosedItem_CanBeReopened()
    {
        var saved = await FeedbackRows.SubmitAsync(_db, _owner, FeedbackPage.Jobs, 3, null, T0, Ct);
        (await ChangeAsync(saved.SubmissionId.Value, FeedbackStatus.Resolved)).IsSuccess.ShouldBeTrue();

        (await ChangeAsync(saved.SubmissionId.Value, FeedbackStatus.New)).IsSuccess.ShouldBeTrue();

        (await StoredAsync(saved)).Status.ShouldBe(FeedbackStatus.New);
    }

    [Fact]
    public async Task Handle_TheCurrentStatus_IsRefusedAndChangesNothing()
    {
        var saved = await FeedbackRows.SubmitAsync(_db, _owner, FeedbackPage.Jobs, 3, null, T0, Ct);

        var result = await ChangeAsync(saved.SubmissionId.Value, FeedbackStatus.New);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.StatusUnchanged");
        var stored = await StoredAsync(saved);
        stored.Status.ShouldBe(FeedbackStatus.New);
        stored.StatusChangedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_AnUnknownId_IsNotFound()
    {
        await FeedbackRows.SubmitAsync(_db, _owner, FeedbackPage.Jobs, 3, null, T0, Ct);

        var result = await ChangeAsync(Guid.NewGuid(), FeedbackStatus.Resolved);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("Feedback.NotFound");
    }
}
