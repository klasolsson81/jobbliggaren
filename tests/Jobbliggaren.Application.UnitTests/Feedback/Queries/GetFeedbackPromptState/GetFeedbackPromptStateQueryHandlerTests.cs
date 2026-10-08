using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;
using Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Feedback.Queries.GetFeedbackPromptState;

/// <summary>
/// #1979 — whether feedback is open, and the pages where this user's prompt stays hidden. The suppressions are
/// written by their one actor, <see cref="SubmitFeedbackCommandHandler"/>.
/// </summary>
public sealed class GetFeedbackPromptStateQueryHandlerTests : IAsyncDisposable
{
    private const string Recipient = "feedback-operator@example.test";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly FakeDateTimeProvider _clock = new(Now);
    private readonly ICurrentUser _me = Substitute.For<ICurrentUser>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetFeedbackPromptStateQueryHandlerTests() => _me.UserId.Returns(Guid.NewGuid());

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static FeedbackGate Gate(bool enabled = true, string? recipient = Recipient, bool canDeliver = true)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(canDeliver);
        return new FeedbackGate(
            Options.Create(new FeedbackOptions { Enabled = enabled, NotificationRecipient = recipient }), sender);
    }

    private async Task RegisterAsync(ICurrentUser user)
    {
        _db.JobSeekers.Add(JobSeeker.Register(user.UserId!.Value, TermsAcceptance.AcceptCurrent(_clock), _clock).Value);
        await _db.SaveChangesAsync(Ct);
    }

    private async Task SubmitAsync(ICurrentUser user, string page)
    {
        var handler = new SubmitFeedbackCommandHandler(
            _db, user, Gate(), _clock, Substitute.For<IDbExceptionInspector>(),
            Substitute.For<IFeedbackScreenshotNormalizer>());
        var result = await handler.Handle(
            new SubmitFeedbackCommand(Guid.NewGuid(), page, 4, null, ReportedClient.None, null), Ct);
        result.IsSuccess.ShouldBeTrue();
    }

    private Task<FeedbackPromptStateDto> ReadAsync(ICurrentUser user, FeedbackGate? gate = null) =>
        new GetFeedbackPromptStateQueryHandler(_db, user, gate ?? Gate())
            .Handle(new GetFeedbackPromptStateQuery(), Ct).AsTask();

    [Fact]
    public async Task Handle_Open_ListsTheAnsweredPageKeysInOrdinalOrder()
    {
        await RegisterAsync(_me);
        await SubmitAsync(_me, "statistics");
        await SubmitAsync(_me, "cv");
        await SubmitAsync(_me, "jobs");
        await SubmitAsync(_me, "cv");

        var state = await ReadAsync(_me);

        state.Open.ShouldBeTrue();
        state.AnsweredPages.ShouldBe(["cv", "jobs", "statistics"]);
    }

    [Fact]
    public async Task Handle_Open_ListsOnlyTheCurrentUsersPages()
    {
        var other = Substitute.For<ICurrentUser>();
        other.UserId.Returns(Guid.NewGuid());
        await RegisterAsync(_me);
        await RegisterAsync(other);
        await SubmitAsync(_me, "jobs");
        await SubmitAsync(other, "overview");

        (await ReadAsync(_me)).AnsweredPages.ShouldBe(["jobs"]);
        (await ReadAsync(other)).AnsweredPages.ShouldBe(["overview"]);
    }

    [Fact]
    public async Task Handle_Open_BeforeAnyFeedback_ListsNothing()
    {
        await RegisterAsync(_me);

        var state = await ReadAsync(_me);

        state.Open.ShouldBeTrue();
        state.AnsweredPages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AnAccountWithoutAProfile_IsClosed_AsSubmitRefusesIt()
    {
        // An Identity account with no job_seekers row: ADR 0151's ProfileMissing, which submit answers 404.
        var state = await ReadAsync(_me);

        state.Open.ShouldBeFalse();
        state.AnsweredPages.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, Recipient, true)]
    [InlineData(true, null, true)]
    [InlineData(true, Recipient, false)]
    public async Task Handle_Closed_IsClosedAndListsNoPagesEvenWhereFeedbackWasGiven(
        bool enabled, string? recipient, bool canDeliver)
    {
        await RegisterAsync(_me);
        await SubmitAsync(_me, "jobs");

        var state = await ReadAsync(_me, Gate(enabled, recipient, canDeliver));

        state.Open.ShouldBeFalse();
        state.AnsweredPages.ShouldBeEmpty();
    }
}
