using Jobbliggaren.Application.Auth.Activity;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2161 (ADR 0172 D3) — the activity record means what its two writers make it mean: "last login" is a session the
/// grant issued, "last active" a request the session handler accepted. <see cref="UserJobAdMatchClaimerTests"/>' IL
/// sweep of every production assembly finds each method's callers, through the port or the adapter, however the
/// instance was obtained.
/// </summary>
public class AccountActivityRecorderConsumerTests
{
    [Fact]
    public void Only_the_session_grant_records_a_login() =>
        CallersOf(nameof(IAccountActivityRecorder.RecordLoginAsync)).ShouldBe(
            [$"{typeof(PasswordlessSessionGrant).FullName}.GrantAsync"],
            "ADR 0172 D1: 'last login' is a session PasswordlessSessionGrant issued.");

    [Fact]
    public void Only_the_request_middleware_records_a_request() =>
        CallersOf(nameof(IAccountActivityRecorder.RecordRequestAsync)).ShouldBe(
            ["Jobbliggaren.Api.Activity.AccountActivityMiddleware.InvokeAsync"],
            "ADR 0172 D1: 'last active' is a request the session handler accepted.");

    private static List<string> CallersOf(string method) =>
        [.. UserJobAdMatchClaimerTests.ReferencesTo(typeof(IAccountActivityRecorder), method)
            .Concat(UserJobAdMatchClaimerTests.ReferencesTo(typeof(SqlAccountActivityRecorder), method))
            .Distinct()
            .Order(StringComparer.Ordinal)];
}
