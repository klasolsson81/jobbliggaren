using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.Extensions.Logging;
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
    private readonly IRateBudget _budget = Substitute.For<IRateBudget>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoginChallengeIssuerTests()
    {
        _budget.TryConsumeAsync(Arg.Any<RateBudgetScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _store.PutAsync(Arg.Any<NewLoginChallenge>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<NewLoginChallenge>().Credentials switch
            {
                ChallengeCredentials.CodeAndLink => new IssuedCredentials(Code, Link),
                ChallengeCredentials.LinkOnly => new IssuedCredentials(null, Link),
                _ => new IssuedCredentials(null, null),
            });
    }

    private async Task<LoginChallengeIssuer> IssuerAsync(
        string subject, Guid userId, CapturingLogger<LoginChallengeIssuer>? logger = null, string typed = Email)
    {
        var lookup = Substitute.For<ILoginAccountLookup>();
        lookup.FindAccountAsync(typed, Arg.Any<CancellationToken>())
            .Returns(subject == "no-account" ? null : new LoginAccount(userId, Email));

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
            new LoginSubjectResolver(lookup, db), _store, _budget, _sender, _audit,
            (ILogger<LoginChallengeIssuer>?)logger ?? NullLogger<LoginChallengeIssuer>.Instance);
    }

    private static LoginChallengeDispatch Dispatch(
        CodeBudgetState budget = CodeBudgetState.Admitted, string typed = Email) =>
        new(ChallengeId.Generate(), typed, budget, "203.0.113.0", "probe/1.0");

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
                Arg.Is<NewLoginChallenge>(c => c.Id == dispatch.ChallengeId && c.Recipient == Email
                    && c.Credentials == ChallengeCredentials.CodeAndLink && c.ReplacesLiveChallenge),
                Arg.Any<CancellationToken>());
            _sender.SendLoginChallengeAsync(
                Email, new LoginChallengeEmail.CodeAndLink(Code, Link), Arg.Any<CancellationToken>());
        });
        _audit.Received(1).LoginChallengeIssued(userId, LoginChallengeKind.CodeAndLink, "203.0.113.0", "probe/1.0");
    }

    // Identity's lookup normaliser upper-cases: another letter case finds the account, and so does U+017F (ſ),
    // which upper-cases to S. LoginAccount carries what UserAccountService.FindAccountAsync answers for both:
    // the row's own spelling.
    [Theory]
    [InlineData("active", "Person@Example.com", CodeBudgetState.Admitted)]
    [InlineData("active", "perſon@example.com", CodeBudgetState.Admitted)]
    [InlineData("active", "perſon@example.com", CodeBudgetState.Exhausted)]
    [InlineData("pending-deletion", "perſon@example.com", CodeBudgetState.Admitted)]
    [InlineData("profile-missing", "perſon@example.com", CodeBudgetState.Admitted)]
    public async Task An_accounts_challenge_is_recorded_for_and_mailed_to_the_accounts_own_spelling(
        string subject, string typed, CodeBudgetState budget)
    {
        var issuer = await IssuerAsync(subject, Guid.NewGuid(), typed: typed);

        await issuer.IssueAsync(Dispatch(budget, typed), Ct);

        await _store.Received(1).PutAsync(
            Arg.Is<NewLoginChallenge>(c => c.Recipient == Email), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendLoginChallengeAsync(
            Email, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().SendLoginChallengeAsync(
            typed, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_address_without_an_account_is_recorded_and_mailed_as_typed()
    {
        const string typed = "Nobody@Example.com";
        var issuer = await IssuerAsync("no-account", Guid.NewGuid(), typed: typed);

        await issuer.IssueAsync(Dispatch(typed: typed), Ct);

        await _store.Received(1).PutAsync(
            Arg.Is<NewLoginChallenge>(c => c.Recipient == typed), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendLoginChallengeAsync(
            typed, new LoginChallengeEmail.RegistrationClosed(), Arg.Any<CancellationToken>());
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
    public async Task A_failed_send_logs_its_own_event_with_the_kind_and_type_and_never_the_address()
    {
        // EmailDeliveryException is what ScalewayEmailSender throws when the provider refuses the send or
        // cannot be reached; it carries the underlying type name.
        var logger = new CapturingLogger<LoginChallengeIssuer>();
        var issuer = await IssuerAsync("active", Guid.NewGuid(), logger);
        _sender.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("login-challenge", nameof(HttpRequestException)));

        await issuer.IssueAsync(Dispatch(), Ct);

        var (level, eventId, message) = logger.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        eventId.ShouldBe(1014);
        message.ShouldContain(nameof(LoginChallengeKind.CodeAndLink));
        message.ShouldContain(nameof(HttpRequestException));
        message.ShouldNotContain("@");
    }

    [Theory]
    [InlineData("no-account")]
    [InlineData("profile-missing")]
    public async Task Past_the_global_cap_an_address_without_an_account_gets_its_record_and_no_mail(string subject)
    {
        var logger = new CapturingLogger<LoginChallengeIssuer>();
        var issuer = await IssuerAsync(subject, Guid.NewGuid(), logger);
        _budget.TryConsumeAsync(
                LoginChallengePolicy.UnknownAddressMailBudget, LoginChallengePolicy.UnknownAddressMailSubject,
                Arg.Any<CancellationToken>())
            .Returns(false);

        await issuer.IssueAsync(Dispatch(), Ct);

        await _store.Received(1).PutAsync(Arg.Any<NewLoginChallenge>(), Arg.Any<CancellationToken>());
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
        var (level, eventId, message) = logger.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        eventId.ShouldBe(1015);
        message.ShouldNotContain("@");
    }

    [Theory]
    [InlineData("active")]
    [InlineData("pending-deletion")]
    public async Task An_account_holders_mail_is_never_counted_against_the_global_cap(string subject)
    {
        // A cap that counted account holders would itself be the attacker-chosen login stop it exists to
        // prevent: a flood of made-up addresses would spend it and silence every real login.
        var issuer = await IssuerAsync(subject, Guid.NewGuid());
        _budget.TryConsumeAsync(Arg.Any<RateBudgetScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await issuer.IssueAsync(Dispatch(), Ct);

        await _sender.Received(1).SendLoginChallengeAsync(Email, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task Within_the_global_cap_an_address_without_an_account_counts_once_and_gets_its_mail()
    {
        var issuer = await IssuerAsync("no-account", Guid.NewGuid());

        await issuer.IssueAsync(Dispatch(), Ct);

        await _budget.Received(1).TryConsumeAsync(
            LoginChallengePolicy.UnknownAddressMailBudget, LoginChallengePolicy.UnknownAddressMailSubject,
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendLoginChallengeAsync(
            Email, new LoginChallengeEmail.RegistrationClosed(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_issuer_takes_no_session_store()
    {
        // The consumer grants nothing.
        typeof(LoginChallengeIssuer).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .ShouldNotContain(typeof(ISessionStore));
    }
}
