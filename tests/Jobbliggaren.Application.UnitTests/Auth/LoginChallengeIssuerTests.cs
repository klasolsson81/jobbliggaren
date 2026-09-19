using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The consumer's decision for one queued challenge. The store is a substitute returning only credentials the
/// Redis adapter mints (RedisLoginChallengeStoreTests is its contract); the account read is the real resolver
/// over a seeded in-memory context.
/// </summary>
public sealed class LoginChallengeIssuerTests
{
    private const string Email = "person@example.com";
    private static readonly LoginCode Code = LoginCode.FromRaw("042917");
    private static readonly LoginLinkToken Link = LoginLinkToken.FromRaw("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8");

    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IAuthAuditLogger _audit = Substitute.For<IAuthAuditLogger>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoginChallengeIssuerTests()
    {
        _store.PutAsync(Arg.Any<NewLoginChallenge>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<NewLoginChallenge>().Credentials switch
            {
                ChallengeCredentials.CodeAndLink => new IssuedCredentials(Code, Link),
                ChallengeCredentials.LinkOnly => new IssuedCredentials(null, Link),
                _ => new IssuedCredentials(null, null),
            });
    }

    private async Task<LoginChallengeIssuer> IssuerAsync(string subject, Guid userId)
    {
        var lookup = Substitute.For<ILoginAccountLookup>();
        lookup.FindUserIdAsync(Email, Arg.Any<CancellationToken>())
            .Returns(subject == "no-account" ? null : userId);

        var db = TestAppDbContextFactory.Create();
        if (subject is "active" or "pending-deletion")
        {
            var profile = JobSeeker.Register(
                userId, "Test", TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default),
                FakeDateTimeProvider.Default).Value;
            if (subject == "pending-deletion")
                profile.SoftDelete(FakeDateTimeProvider.Default);
            db.JobSeekers.Add(profile);
            await db.SaveChangesAsync(Ct);
        }

        return new LoginChallengeIssuer(
            new LoginSubjectResolver(lookup, db), _store, _sender, _audit, NullLogger<LoginChallengeIssuer>.Instance);
    }

    private static LoginChallengeDispatch Dispatch(CodeBudgetState budget = CodeBudgetState.Admitted) =>
        new(ChallengeId.Generate(), Email, budget, "203.0.113.0", "probe/1.0");

    [Fact]
    public async Task An_active_account_within_budget_gets_the_code_and_the_link_after_its_record_is_written()
    {
        var userId = Guid.NewGuid();
        var issuer = await IssuerAsync("active", userId);
        var dispatch = Dispatch();

        await issuer.IssueAsync(dispatch, Ct);

        Received.InOrder(() =>
        {
            _store.PutAsync(
                Arg.Is<NewLoginChallenge>(c => c.Id == dispatch.ChallengeId && c.Email == Email
                    && c.Credentials == ChallengeCredentials.CodeAndLink && c.ReplacesLiveChallenge),
                Arg.Any<CancellationToken>());
            _sender.SendLoginChallengeAsync(
                Email, new LoginChallengeEmail.CodeAndLink(Code, Link), Arg.Any<CancellationToken>());
        });
        _audit.Received(1).LoginChallengeIssued(userId, LoginChallengeKind.CodeAndLink, "203.0.113.0", "probe/1.0");
    }

    [Fact]
    public async Task An_active_account_past_its_code_budget_gets_a_link_only_mail_that_replaces_nothing()
    {
        var userId = Guid.NewGuid();
        var issuer = await IssuerAsync("active", userId);

        await issuer.IssueAsync(Dispatch(CodeBudgetState.Exhausted), Ct);

        await _store.Received(1).PutAsync(
            Arg.Is<NewLoginChallenge>(c => c.Credentials == ChallengeCredentials.LinkOnly && !c.ReplacesLiveChallenge),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendLoginChallengeAsync(
            Email, new LoginChallengeEmail.LinkOnly(Link), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CodeBudgetState.Admitted)]
    [InlineData(CodeBudgetState.Exhausted)]
    public async Task An_address_without_an_account_gets_a_record_the_closed_mail_and_no_audit(CodeBudgetState budget)
    {
        var issuer = await IssuerAsync("no-account", Guid.NewGuid());

        await issuer.IssueAsync(Dispatch(budget), Ct);

        // A record for every admitted request, known or not, so "burned" and "never existed" stay one answer
        // (ADR 0142 D2). It replaces the live challenge exactly when an existing account's would.
        await _store.Received(1).PutAsync(
            Arg.Is<NewLoginChallenge>(c => c.Credentials == ChallengeCredentials.None
                && c.ReplacesLiveChallenge == (budget == CodeBudgetState.Admitted)),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendLoginChallengeAsync(
            Email, new LoginChallengeEmail.RegistrationClosed(), Arg.Any<CancellationToken>());
        _audit.DidNotReceiveWithAnyArgs().LoginChallengeIssued(default, default, default, default);
    }

    [Fact]
    public async Task An_account_without_a_profile_gets_the_closed_mail()
    {
        var issuer = await IssuerAsync("profile-missing", Guid.NewGuid());

        await issuer.IssueAsync(Dispatch(), Ct);

        await _sender.Received(1).SendLoginChallengeAsync(
            Email, new LoginChallengeEmail.RegistrationClosed(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_account_pending_deletion_gets_no_credential_and_the_earliest_deletion_date()
    {
        var userId = Guid.NewGuid();
        var issuer = await IssuerAsync("pending-deletion", userId);

        await issuer.IssueAsync(Dispatch(), Ct);

        var expected = DateOnly.FromDateTime(FakeDateTimeProvider.Default.UtcNow.AddDays(30).UtcDateTime);
        await _store.Received(1).PutAsync(
            Arg.Is<NewLoginChallenge>(c => c.Credentials == ChallengeCredentials.None), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendLoginChallengeAsync(
            Email, new LoginChallengeEmail.PendingDeletion(expected), Arg.Any<CancellationToken>());
        _audit.Received(1).LoginChallengeIssued(
            userId, LoginChallengeKind.PendingDeletion, Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task A_failed_send_is_contained_and_leaves_no_audit_line()
    {
        var issuer = await IssuerAsync("active", Guid.NewGuid());
        _sender.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("login-challenge", nameof(HttpRequestException)));

        await Should.NotThrowAsync(() => issuer.IssueAsync(Dispatch(), Ct));

        _audit.DidNotReceiveWithAnyArgs().LoginChallengeIssued(default, default, default, default);
    }

    [Fact]
    public void The_issuer_takes_no_budget_and_no_session_store()
    {
        // The budgets are the request path's; the consumer decides nothing about them and grants nothing.
        var parameters = typeof(LoginChallengeIssuer).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).ToList();

        parameters.ShouldNotContain(typeof(IRateBudget));
        parameters.ShouldNotContain(typeof(ISessionStore));
    }
}
