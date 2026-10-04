using System.Reflection;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;
using Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Mediator;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1974 (ADR 0151) — the admin surface's messages carry the admin gate, and the account directory reaches
/// only the three admin queries. The directory can list every account's address, so it is the tool that
/// would reopen the account-existence oracle the login page closes; the gate sits on the message, not on
/// the port, which is why the port's consumers are pinned here.
/// </summary>
public class AdminAccountDirectoryTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    /// <summary>Every assembly that can resolve a port: Application declares, Infrastructure, Api, Worker and Migrate compose.</summary>
    private static readonly Assembly[] OwnedAssemblies =
    [
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AdminJobAdsEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
    ];

    private static IEnumerable<Type> AdminMessages() =>
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && type.Namespace?.StartsWith("Jobbliggaren.Application.Admin", StringComparison.Ordinal) == true
                && typeof(IMessage).IsAssignableFrom(type));

    [Fact]
    public void Every_message_on_the_admin_surface_carries_the_admin_gate()
    {
        AdminMessages().ShouldNotBeEmpty();
        AdminMessages()
            .Where(type => !typeof(IAdminRequest).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ShouldBeEmpty();
    }

    [Fact]
    public void No_admin_message_records_a_recent_search()
    {
        AdminMessages()
            .Where(type => typeof(ICapturesRecentSearch).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_account_directory_is_injected_only_into_the_three_admin_account_queries()
    {
        var consumers = OwnedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(Declared).Cast<MethodBase>().Concat(type.GetMethods(Declared))
                .Any(member => member.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(IAccountDirectory))))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe(
            new[]
            {
                typeof(CountAccountsByStatusQueryHandler).FullName!,
                typeof(GetAccountDetailsQueryHandler).FullName!,
                typeof(SearchAccountsQueryHandler).FullName!,
            }.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            "A new consumer of the account directory reads every account's address; it needs the admin gate "
            + "and ADR 0151's review first. Found: " + string.Join(", ", consumers));
    }
}
