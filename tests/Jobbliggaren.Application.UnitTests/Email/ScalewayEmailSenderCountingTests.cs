using System.Diagnostics;
using System.Net;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Email.Counting;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — what the Scaleway funnel counts. Every attempt that reaches the provider is counted once,
/// under its own kind and the class the funnel saw; nothing else is. And the count never changes the send: a tally
/// that throws or hangs leaves the caller with exactly the outcome it would have had without one (security-auditor 5d).
/// </summary>
public sealed partial class ScalewayEmailSenderTests
{
    private static readonly TimeSpan WithinTheTallyBudget = EmailSendTally.Budget + TimeSpan.FromSeconds(2);

    public static TheoryData<string, EmailKind> SendMethods => new()
    {
        { nameof(IEmailSender.SendMatchNotificationEmailAsync), EmailKind.MatchNotification },
        { nameof(IEmailSender.SendFollowedCompanyNotificationEmailAsync), EmailKind.FollowedCompanyNotification },
        { nameof(IEmailSender.SendEmailChangedNotificationAsync), EmailKind.EmailChangedNotification },
        {
            nameof(IEmailSender.SendAccountEmailChangeRequestedNotificationAsync),
            EmailKind.AccountEmailChangeRequestedNotification
        },
        { nameof(IEmailSender.SendLoginChallengeAsync), EmailKind.LoginChallenge },
        { nameof(IEmailSender.SendFeedbackReceivedNotificationAsync), EmailKind.FeedbackReceivedNotification },
        { nameof(IEmailSender.SendFeedbackReplyAsync), EmailKind.FeedbackReply },
    };

    [Fact]
    public void SendMethods_CoverEveryMethodOfThePort() =>
        SendMethods.Select(row => (string)row.Data.Item1)
            .ShouldBe(
                typeof(IEmailSender).GetMethods().Where(m => !m.IsSpecialName).Select(m => m.Name),
                ignoreOrder: true);

    [Theory]
    [MemberData(nameof(SendMethods))]
    public async Task ScalewayEmailSender_EachSendMethod_CountsOneAcceptedAttemptOfItsOwnKind(
        string method, EmailKind kind)
    {
        await SendAsync(CreateSut(), method, CancellationToken.None);

        _tally.Records.ShouldBe([(kind, EmailSendOutcome.Accepted)]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EmailSendOutcome.Refused, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.Unauthorized, EmailSendOutcome.Refused, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.Forbidden, EmailSendOutcome.Refused, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.NotFound, EmailSendOutcome.Refused, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.UnprocessableEntity, EmailSendOutcome.Refused, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailSendOutcome.RateLimited, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.InternalServerError, EmailSendOutcome.ProviderError, EmailDeliveryDisposition.Unknown)]
    [InlineData(HttpStatusCode.BadGateway, EmailSendOutcome.ProviderError, EmailDeliveryDisposition.Unknown)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailSendOutcome.ProviderError, EmailDeliveryDisposition.Unknown)]
    [InlineData(HttpStatusCode.MultipleChoices, EmailSendOutcome.Other, EmailDeliveryDisposition.Unknown)]
    public async Task ScalewayEmailSender_ProviderAnswersWithAFailureStatus_CountsItsClassAndKeepsItsDisposition(
        HttpStatusCode status, EmailSendOutcome outcome, EmailDeliveryDisposition disposition)
    {
        _handler.RespondWith(status, "{}");

        var ex = await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await CreateSut().SendFeedbackReplyAsync(Recipient, SampleReplyContent(), CancellationToken.None));

        ex.Disposition.ShouldBe(disposition);
        _tally.Records.ShouldBe([(EmailKind.FeedbackReply, outcome)]);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, EmailSendOutcome.Unreachable)]
    [InlineData(HttpRequestError.ConnectionError, EmailSendOutcome.Unreachable)]
    [InlineData(HttpRequestError.SecureConnectionError, EmailSendOutcome.Unreachable)]
    [InlineData(HttpRequestError.ProxyTunnelError, EmailSendOutcome.Unreachable)]
    [InlineData(HttpRequestError.ResponseEnded, EmailSendOutcome.Other)]
    [InlineData(HttpRequestError.Unknown, EmailSendOutcome.Other)]
    public async Task ScalewayEmailSender_TransportFailsWithoutAResponse_CountsTheClassOfItsPhase(
        HttpRequestError error, EmailSendOutcome outcome)
    {
        _handler.ThrowOnSend(new HttpRequestException(error, "transport failure"));

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await CreateSut().SendLoginChallengeAsync(Recipient, SampleLoginContent(), CancellationToken.None));

        _tally.Records.ShouldBe([(EmailKind.LoginChallenge, outcome)]);
    }

    [Fact]
    public async Task ScalewayEmailSender_SendTimesOut_CountsATimeout()
    {
        _handler.ThrowOnSend(new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException()));

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await CreateSut().SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), CancellationToken.None));

        _tally.Records.ShouldBe([(EmailKind.MatchNotification, EmailSendOutcome.Timeout)]);
    }

    [Fact]
    public async Task ScalewayEmailSender_TheCallerCancels_CountsNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _handler.ThrowOnSend(new TaskCanceledException("A task was canceled."));

        await Should.ThrowAsync<TaskCanceledException>(async () =>
            await CreateSut().SendLoginChallengeAsync(Recipient, SampleLoginContent(), cts.Token));

        _tally.Records.ShouldBeEmpty("a cancellation the caller asked for leaves through the filter, uncounted");
    }

    /// <summary>A template is rendered before the funnel, so a fault in it hands nothing over and counts nothing.</summary>
    [Fact]
    public async Task ScalewayEmailSender_ATemplateFaults_CountsNothingAndPostsNothing()
    {
        var thrown = await Record.ExceptionAsync(async () =>
            await CreateSut().SendMatchNotificationEmailAsync(Recipient, null!, CancellationToken.None));

        thrown.ShouldNotBeNull();
        thrown.ShouldNotBeOfType<EmailDeliveryException>();
        _handler.Requests.ShouldBeEmpty();
        _tally.Records.ShouldBeEmpty();
    }

    public static TheoryData<string, Func<CancellationToken, Task>> FailingTallies => new()
    {
        { "throws at once", _ => throw new InvalidOperationException($"count failed for {Recipient}") },
        { "throws after an await", async _ => { await Task.Yield(); throw new InvalidOperationException(Recipient); } },
        { "never finishes", _ => new TaskCompletionSource().Task },
    };

    [Theory]
    [MemberData(nameof(FailingTallies))]
    public async Task ScalewayEmailSender_ATallyThatFails_LeavesAnAcceptedSendSuccessful(
        string because, Func<CancellationToken, Task> write)
    {
        var tallyLogger = new CapturingLogger();
        var clock = Stopwatch.StartNew();

        await Should.NotThrowAsync(
            async () => await CreateSut(new ScriptedEmailSendTally(tallyLogger, write))
                .SendLoginChallengeAsync(Recipient, SampleLoginContent(), CancellationToken.None),
            because);

        clock.Elapsed.ShouldBeLessThan(WithinTheTallyBudget, because);
        _handler.Requests.Count.ShouldBe(1);
        tallyLogger.Records.ShouldHaveSingleItem(because).Surface.ShouldNotContain(Recipient, Case.Insensitive);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailDeliveryDisposition.NotAccepted)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EmailDeliveryDisposition.Unknown)]
    public async Task ScalewayEmailSender_ATallyThatFails_LeavesAFailedSendWithItsOwnException(
        HttpStatusCode status, EmailDeliveryDisposition disposition)
    {
        _handler.RespondWith(status, "{}");
        var tally = new ScriptedEmailSendTally(
            new CapturingLogger(), _ => throw new InvalidOperationException("the count failed"));

        var ex = await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await CreateSut(tally).SendFeedbackReplyAsync(Recipient, SampleReplyContent(), CancellationToken.None));

        ex.Disposition.ShouldBe(disposition);
        ex.UnderlyingErrorType.ShouldBe(nameof(HttpRequestException));
        ex.EmailKind.ShouldBe(EmailKinds.FeedbackReplyToken);
    }

    [Fact]
    public async Task ScalewayEmailSender_ATallyThatFails_LeavesNoRecipientInEitherLog()
    {
        _handler.RespondWith(HttpStatusCode.BadRequest, $$"""{"message":"rejected {{Recipient}}"}""");
        var tallyLogger = new CapturingLogger();
        var tally = new ScriptedEmailSendTally(tallyLogger, _ => throw new InvalidOperationException(Recipient));

        await Should.ThrowAsync<EmailDeliveryException>(async () =>
            await CreateSut(tally).SendLoginChallengeAsync(Recipient, SampleLoginContent(), CancellationToken.None));

        LoggedSurface().ShouldNotContain(Recipient, Case.Insensitive);
        tallyLogger.Records.ShouldHaveSingleItem().Surface.ShouldNotContain(Recipient, Case.Insensitive);
    }

    private static FeedbackReplyEmail SampleReplyContent() =>
        new(
            Jobbliggaren.Domain.Feedback.FeedbackPage.Jobs,
            new DateTimeOffset(2026, 10, 8, 12, 30, 0, TimeSpan.Zero),
            Jobbliggaren.Domain.Feedback.FeedbackReplyText.Create("Tack för din feedback.").Value);

    private Task SendAsync(ScalewayEmailSender sut, string method, CancellationToken ct) => method switch
    {
        nameof(IEmailSender.SendMatchNotificationEmailAsync) =>
            sut.SendMatchNotificationEmailAsync(Recipient, SampleMatchContent(), ct),
        nameof(IEmailSender.SendFollowedCompanyNotificationEmailAsync) =>
            sut.SendFollowedCompanyNotificationEmailAsync(Recipient, SampleFollowContent(), ct),
        nameof(IEmailSender.SendEmailChangedNotificationAsync) =>
            sut.SendEmailChangedNotificationAsync(Recipient, ct),
        nameof(IEmailSender.SendAccountEmailChangeRequestedNotificationAsync) =>
            sut.SendAccountEmailChangeRequestedNotificationAsync(
                Recipient, DateTimeOffset.UnixEpoch.AddDays(1), DateTimeOffset.UnixEpoch.AddDays(2), ct),
        nameof(IEmailSender.SendLoginChallengeAsync) =>
            sut.SendLoginChallengeAsync(Recipient, SampleLoginContent(), ct),
        nameof(IEmailSender.SendFeedbackReceivedNotificationAsync) =>
            sut.SendFeedbackReceivedNotificationAsync(Recipient, SampleFeedbackContent(), ct),
        nameof(IEmailSender.SendFeedbackReplyAsync) =>
            sut.SendFeedbackReplyAsync(Recipient, SampleReplyContent(), ct),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Not a send method."),
    };
}
