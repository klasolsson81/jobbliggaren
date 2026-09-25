using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Application.Dev.Commands.SeedAccount;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Dev.Commands.SeedAccount;

/// <summary>
/// The Development seed seam's decisions (ADR 0142 part 5a): a non-reserved address is refused before anything
/// is read, an address without an account is opened through the real <see cref="AccountRegistrar"/>, and only an
/// address the login resolves to <see cref="LoginSubject.Active"/> is Ready. The account read is the real resolver
/// over an in-memory context; the Identity port is a substitute answering what <c>UserAccountService</c> answers
/// (<c>PasswordlessAccountCreatorTests</c> is its contract).
/// </summary>
public sealed class DevSeedAccountCommandHandlerTests
{
    private const string Email = "seed@example.com";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly IDevSeedableAddressPolicy _policy = Substitute.For<IDevSeedableAddressPolicy>();
    private readonly ILoginAccountLookup _lookup = Substitute.For<ILoginAccountLookup>();
    private readonly IPasswordlessAccountCreator _accounts = Substitute.For<IPasswordlessAccountCreator>();
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DevSeedAccountCommandHandlerTests() => _policy.IsSeedable(Email).Returns(true);

    private DevSeedAccountCommandHandler Handler()
    {
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.Current.Returns(Guid.NewGuid());
        return new DevSeedAccountCommandHandler(
            _policy,
            new LoginSubjectResolver(_lookup, _db),
            new AccountRegistrar(
                _accounts, _db, FakeDateTimeProvider.Default, correlation, Substitute.For<IRequestContextProvider>()));
    }

    private async Task WithProfileAsync(bool softDeleted)
    {
        var profile = JobSeeker.Register(
            _userId, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default).Value;
        if (softDeleted)
            profile.SoftDelete(FakeDateTimeProvider.Default);
        _db.JobSeekers.Add(profile);
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_non_reserved_address_is_refused_before_anything_is_read_or_written()
    {
        _policy.IsSeedable(Email).Returns(false);

        (await Handler().Handle(new DevSeedAccountCommand(Email), Ct)).ShouldBe(DevSeedAccountOutcome.NotReserved);

        await _lookup.DidNotReceiveWithAnyArgs().FindAccountAsync(default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
    }

    [Fact]
    public async Task An_address_without_an_account_is_opened_and_is_Ready_when_it_resolves_Active()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>())
            .Returns(null, new LoginAccount(_userId, Email));
        _accounts.CreatePasswordlessUserAsync(Email, Arg.Any<CancellationToken>()).Returns(Result.Success(_userId));

        (await Handler().Handle(new DevSeedAccountCommand(Email), Ct)).ShouldBe(DevSeedAccountOutcome.Ready);

        var profile = await _db.JobSeekers.SingleAsync(js => js.UserId == _userId, Ct);
        profile.TermsAcceptance!.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);
        (await _db.AuditLogEntries.CountAsync(
            e => e.EventType == AccountRegistrar.AccountCreatedAuditEventType, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task An_Active_account_is_Ready_and_nothing_is_written()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));
        await WithProfileAsync(softDeleted: false);

        (await Handler().Handle(new DevSeedAccountCommand(Email), Ct)).ShouldBe(DevSeedAccountOutcome.Ready);

        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_profile_pending_deletion_is_Unavailable_and_left_as_it_was()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));
        await WithProfileAsync(softDeleted: true);

        (await Handler().Handle(new DevSeedAccountCommand(Email), Ct)).ShouldBe(DevSeedAccountOutcome.Unavailable);

        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().SingleAsync(js => js.UserId == _userId, Ct)).DeletedAt
            .ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task An_Identity_row_without_a_profile_is_Unavailable_and_no_profile_is_created()
    {
        // The state AccountRegistrar's save leaves behind when it throws after the Identity
        // write committed (the orphan sweep collects it later).
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));

        (await Handler().Handle(new DevSeedAccountCommand(Email), Ct)).ShouldBe(DevSeedAccountOutcome.Unavailable);

        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task An_address_the_creator_refuses_leaves_no_account_and_answers_Unavailable()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        _accounts.CreatePasswordlessUserAsync(Email, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.EmailNotStorable, AuthErrorCodes.EmailNotStorableMessage)));

        (await Handler().Handle(new DevSeedAccountCommand(Email), Ct)).ShouldBe(DevSeedAccountOutcome.Unavailable);

        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }
}
