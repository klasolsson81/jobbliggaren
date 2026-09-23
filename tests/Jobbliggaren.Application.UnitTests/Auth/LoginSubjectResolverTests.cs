using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The login challenge's one classification of an address. The soft-deleted profile is produced by
/// <c>JobSeeker.SoftDelete</c> and the live one by <c>JobSeeker.Register</c>, the domain's own actors.
/// </summary>
public sealed class LoginSubjectResolverTests
{
    // The row's own spelling. Every test asks with another one, as Identity's case-folding lookup admits.
    private const string StoredEmail = "person@example.com";
    private const string TypedEmail = "Person@Example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (LoginSubjectResolver Resolver, IAppDbContext Db) Resolver(Guid? accountUserId)
    {
        var lookup = Substitute.For<ILoginAccountLookup>();
        lookup.FindAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(accountUserId is { } id ? new LoginAccount(id, StoredEmail) : null);
        var db = TestAppDbContextFactory.Create();
        return (new LoginSubjectResolver(lookup, db), db);
    }

    private static JobSeeker Profile(Guid userId) =>
        JobSeeker.Register(
            userId, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default)
        .Value;

    [Fact]
    public async Task An_address_without_an_account_is_no_account()
    {
        var (resolver, _) = Resolver(null);

        (await resolver.ResolveAsync("nobody@example.com", Ct)).ShouldBeOfType<LoginSubject.NoAccount>();
    }

    [Fact]
    public async Task An_account_with_a_live_profile_is_active()
    {
        var userId = Guid.NewGuid();
        var (resolver, db) = Resolver(userId);
        db.JobSeekers.Add(Profile(userId));
        await db.SaveChangesAsync(Ct);

        (await resolver.ResolveAsync(TypedEmail, Ct)).ShouldBe(new LoginSubject.Active(userId, StoredEmail));
    }

    [Fact]
    public async Task An_account_whose_profile_is_soft_deleted_is_pending_deletion_with_its_date()
    {
        var userId = Guid.NewGuid();
        var (resolver, db) = Resolver(userId);
        var profile = Profile(userId);
        profile.SoftDelete(FakeDateTimeProvider.Default);
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);

        var subject = await resolver.ResolveAsync(TypedEmail, Ct);

        subject.ShouldBe(new LoginSubject.PendingDeletion(userId, StoredEmail, profile.DeletedAt!.Value));
    }

    [Fact]
    public async Task An_account_without_a_profile_is_profile_missing()
    {
        var userId = Guid.NewGuid();
        var (resolver, _) = Resolver(userId);

        (await resolver.ResolveAsync(TypedEmail, Ct)).ShouldBe(new LoginSubject.ProfileMissing(userId, StoredEmail));
    }
}
