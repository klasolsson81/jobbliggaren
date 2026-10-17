using Jobbliggaren.Api.Activity;
using Jobbliggaren.Application.Auth.Activity;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth.AccountActivity;

/// <summary>
/// #2161 (ADR 0172 D4) — the middleware hands the authenticated request's account to the recorder and nothing else:
/// an anonymous request is passed on untouched. The recorder is substituted with what the production one returns, a
/// completed task.
/// </summary>
public sealed class AccountActivityMiddlewareTests
{
    private readonly IAccountActivityRecorder _recorder = Substitute.For<IAccountActivityRecorder>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly List<string> _order = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AccountActivityMiddleware Middleware() => new(_ =>
    {
        _order.Add("next");
        return Task.CompletedTask;
    });

    [Fact]
    public async Task An_anonymous_request_records_nothing_and_goes_on()
    {
        _user.IsAuthenticated.Returns(false);

        await Middleware().InvokeAsync(new DefaultHttpContext(), _user, _recorder);

        await _recorder.DidNotReceiveWithAnyArgs().RecordRequestAsync(default, Ct);
        _order.ShouldBe(["next"]);
    }

    [Fact]
    public async Task An_authenticated_request_records_its_account_with_the_requests_cancellation_before_it_goes_on()
    {
        var userId = Guid.NewGuid();
        _user.IsAuthenticated.Returns(true);
        _user.UserId.Returns(userId);
        _recorder.RecordRequestAsync(userId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _order.Add("record");
            return Task.CompletedTask;
        });
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        await Middleware().InvokeAsync(context, _user, _recorder);

        await _recorder.Received(1).RecordRequestAsync(userId, aborted.Token);
        _order.ShouldBe(["record", "next"]);
    }
}
