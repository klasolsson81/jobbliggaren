using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;
using Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts;

/// <summary>
/// The admin account queries (#1974, ADR 0151): what they map, what they refuse before the directory runs,
/// and what their text never carries.
/// </summary>
public sealed class AdminAccountQueriesTests
{
    private const string Sentinel = "termsentinel1974";
    private static readonly DateTimeOffset DeletedAt = new(2026, 10, 4, 22, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AccountDirectoryEntry Entry(AccountStatus status, bool admin = false, bool withProfile = true) =>
        new(
            Guid.NewGuid(),
            $"{status}@example.test",
            admin,
            EmailConfirmed: true,
            status,
            withProfile ? new JobSeekerId(Guid.NewGuid()) : null,
            withProfile ? DeletedAt.AddDays(-40) : null,
            status == AccountStatus.PendingDeletion ? DeletedAt : null);

    [Fact]
    public async Task The_search_maps_each_entry_and_counts_applications_only_for_an_active_account()
    {
        var active = Entry(AccountStatus.Active, admin: true);
        var pending = Entry(AccountStatus.PendingDeletion);
        var missing = Entry(AccountStatus.ProfileMissing, withProfile: false);
        var directory = Substitute.For<IAccountDirectory>();
        directory.SearchAsync(Arg.Any<AccountDirectorySearch>(), Arg.Any<CancellationToken>())
            .Returns(new AccountDirectoryPage([active, pending, missing], 3));
        await using var db = TestAppDbContextFactory.Create();

        var page = await new SearchAccountsQueryHandler(directory, db).Handle(
            new SearchAccountsQuery(null, null, AccountSort.RegisteredNewest, 1, 25), Ct);

        page.TotalCount.ShouldBe(3);
        var rows = page.Items.ToDictionary(row => row.Id);
        rows[active.UserId].Role.ShouldBe(AccountRole.Admin);
        rows[active.UserId].ApplicationCount.ShouldBe(0);
        rows[active.UserId].DeletionEarliest.ShouldBeNull();
        rows[pending.UserId].Role.ShouldBe(AccountRole.User);
        rows[pending.UserId].ApplicationCount.ShouldBeNull();
        rows[pending.UserId].DeletionEarliest.ShouldBe(new DateOnly(2026, 11, 3));
        rows[missing.UserId].ApplicationCount.ShouldBeNull();
        rows[missing.UserId].RegisteredAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_details_are_null_for_an_unknown_id_and_carry_no_counts_for_an_inactive_account()
    {
        var pending = Entry(AccountStatus.PendingDeletion);
        var directory = Substitute.For<IAccountDirectory>();
        directory.FindAsync(pending.UserId, Arg.Any<CancellationToken>()).Returns(pending);
        await using var db = TestAppDbContextFactory.Create();
        var handler = new GetAccountDetailsQueryHandler(directory, db);

        (await handler.Handle(new GetAccountDetailsQuery(Guid.NewGuid()), Ct)).ShouldBeNull();

        var details = await handler.Handle(new GetAccountDetailsQuery(pending.UserId), Ct);
        details.ShouldNotBeNull();
        details.Status.ShouldBe(AccountStatus.PendingDeletion);
        details.DeletionEarliest.ShouldBe(new DateOnly(2026, 11, 3));
        details.ApplicationCount.ShouldBeNull();
        details.ResumeCount.ShouldBeNull();
        details.SavedSearchCount.ShouldBeNull();
    }

    public static TheoryData<IMessage> AdminQueries() =>
    [
        new SearchAccountsQuery(Sentinel, null, AccountSort.RegisteredNewest, 1, 25),
        new CountAccountsByStatusQuery(Sentinel),
        new GetAccountDetailsQuery(Guid.NewGuid()),
    ];

    [Theory]
    [MemberData(nameof(AdminQueries))]
    public async Task A_caller_without_the_admin_role_is_refused_before_the_directory_runs(IMessage query)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(Roles.Admin).Returns(false);
        var directory = Substitute.For<IAccountDirectory>();
        var behavior = new AdminAuthorizationBehavior<IMessage, object?>(currentUser);

        await Should.ThrowAsync<ForbiddenException>(async () =>
            await behavior.Handle(query, (_, ct) => RunAsync(directory, query, ct), Ct));

        await directory.DidNotReceiveWithAnyArgs().SearchAsync(default!, Ct);
        await directory.DidNotReceiveWithAnyArgs().CountByStatusAsync(default, Ct);
        await directory.DidNotReceiveWithAnyArgs().FindAsync(default, Ct);
    }

    [Theory]
    [MemberData(nameof(AdminQueries))]
    public async Task An_admin_reaches_the_directory(IMessage query)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsInRole(Roles.Admin).Returns(true);
        var directory = Substitute.For<IAccountDirectory>();
        directory.SearchAsync(Arg.Any<AccountDirectorySearch>(), Arg.Any<CancellationToken>())
            .Returns(new AccountDirectoryPage([], 0));
        directory.CountByStatusAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AccountStatusCounts(0, 0, 0, 0));
        var behavior = new AdminAuthorizationBehavior<IMessage, object?>(currentUser);

        await behavior.Handle(query, (_, ct) => RunAsync(directory, query, ct), Ct);

        directory.ReceivedCalls().ShouldNotBeEmpty();
    }

    private static async ValueTask<object?> RunAsync(IAccountDirectory directory, IMessage query, CancellationToken ct)
    {
        var db = Substitute.For<IAppDbContext>();
        return query switch
        {
            SearchAccountsQuery search => await new SearchAccountsQueryHandler(directory, db).Handle(search, ct),
            CountAccountsByStatusQuery counts => await new CountAccountsByStatusQueryHandler(directory).Handle(counts, ct),
            GetAccountDetailsQuery details => await new GetAccountDetailsQueryHandler(directory, db).Handle(details, ct),
            _ => throw new InvalidOperationException("Not an account query."),
        };
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(SearchAccountsQuery.MaxPage + 1, 25)]
    [InlineData(1, 0)]
    [InlineData(1, SearchAccountsQuery.MaxPageSize + 1)]
    public void The_search_refuses_a_page_outside_its_bounds(int page, int pageSize)
    {
        new SearchAccountsQueryValidator()
            .Validate(new SearchAccountsQuery(null, null, AccountSort.RegisteredNewest, page, pageSize))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void The_search_accepts_its_largest_page()
    {
        new SearchAccountsQueryValidator()
            .Validate(new SearchAccountsQuery(null, null, AccountSort.RegisteredNewest, SearchAccountsQuery.MaxPage, SearchAccountsQuery.MaxPageSize))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_page_twenty_one_million_deep_is_valid_at_a_page_size_of_one()
    {
        new SearchAccountsQueryValidator()
            .Validate(new SearchAccountsQuery(null, null, AccountSort.RegisteredNewest, 21_000_000, 1))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void The_largest_page_s_offset_fits_an_int_at_the_largest_page_size()
    {
        (((long)SearchAccountsQuery.MaxPage - 1) * SearchAccountsQuery.MaxPageSize).ShouldBeLessThanOrEqualTo(int.MaxValue);
    }

    [Fact]
    public void A_term_as_long_as_an_address_may_be_is_accepted_and_one_more_character_is_not()
    {
        var longest = new string('q', 256);
        var validator = new SearchAccountsQueryValidator();

        validator.Validate(new SearchAccountsQuery(longest, null, AccountSort.RegisteredNewest, 1, 25))
            .IsValid.ShouldBeTrue();
        validator.Validate(new SearchAccountsQuery(longest + "q", null, AccountSort.RegisteredNewest, 1, 25))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void The_details_refuse_an_empty_id()
    {
        new GetAccountDetailsQueryValidator().Validate(new GetAccountDetailsQuery(Guid.Empty)).IsValid.ShouldBeFalse();
        new GetAccountDetailsQueryValidator().Validate(new GetAccountDetailsQuery(Guid.NewGuid())).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_term_too_long_or_with_a_control_character_is_refused_without_being_quoted(bool tooLong)
    {
        var term = tooLong ? new string('q', AccountAddressTermRules.MaxLength) + Sentinel : $"{Sentinel}\u0000";

        var search = new SearchAccountsQueryValidator()
            .Validate(new SearchAccountsQuery(term, null, AccountSort.RegisteredNewest, 1, 25));
        var counts = new CountAccountsByStatusQueryValidator().Validate(new CountAccountsByStatusQuery(term));

        foreach (var result in new[] { search, counts })
        {
            result.IsValid.ShouldBeFalse();
            result.Errors.ShouldAllBe(error => !error.ErrorMessage.Contains(Sentinel));
        }
    }

    [Fact]
    public void An_unknown_status_or_sort_is_refused()
    {
        var validator = new SearchAccountsQueryValidator();

        validator.Validate(new SearchAccountsQuery(null, (AccountStatus)99, AccountSort.RegisteredNewest, 1, 25))
            .IsValid.ShouldBeFalse();
        validator.Validate(new SearchAccountsQuery(null, null, (AccountSort)99, 1, 25)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void No_record_that_carries_the_term_or_an_address_prints_it()
    {
        var entry = Entry(AccountStatus.Active) with { Email = $"{Sentinel}@example.test" };
        object[] records =
        [
            new SearchAccountsQuery(Sentinel, AccountStatus.Active, AccountSort.AddressAscending, 1, 25),
            new CountAccountsByStatusQuery(Sentinel),
            new AccountDirectorySearch(Sentinel, null, AccountSort.RegisteredNewest, 1, 25),
            entry,
            new AccountListItemDto(entry.UserId, entry.Email, AccountRole.User, AccountStatus.Active, true, null, null, 0),
            new AccountDetailsDto(entry.UserId, entry.Email, AccountRole.User, AccountStatus.Active, true, null, null, 0, 0, 0),
        ];

        foreach (var record in records)
            record.ToString()!.ShouldNotContain(Sentinel);
    }
}
