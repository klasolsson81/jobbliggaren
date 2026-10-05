using System.Reflection;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1975 (ADR 0153) — who may reach a pending address change, and what each message of the flow must carry. The store
/// holds a code that moves an account to another inbox, so a new consumer is a new way to do that and is decided here.
/// The administrator's request is pinned to its markers BY NAME: <c>ReauthenticationTripwireTests</c>' pattern reads
/// "ChangeEmail", not "EmailChange", and widening it would catch the self-service steps that are outside the marker by
/// design.
/// </summary>
public class AccountEmailChangeChainTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] Composing =
    [
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
        typeof(AuthEndpoints).Assembly,
        typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
    ];

    private static string[] ConsumersOf(Type dependency) =>
        [.. Composing.SelectMany(a => a.GetTypes())
            .Where(t => t.GetConstructors(Declared).Cast<MethodBase>().Concat(t.GetMethods(Declared))
                .Any(m => m.GetParameters().Any(p => p.ParameterType == dependency)))
            .Select(t => t.FullName!)
            .Order()];

    [Fact]
    public void Only_the_handlers_of_the_flow_take_the_store()
    {
        ConsumersOf(typeof(IAccountEmailChangeStore)).ShouldBe(
        [
            .. new[]
            {
                typeof(CancelAccountEmailChangeCommandHandler).FullName!,
                typeof(GetPendingAccountEmailChangeQueryHandler).FullName!,
                typeof(RequestAccountEmailChangeCommandHandler).FullName!,
            }.Order(),
        ]);
    }

    [Fact]
    public void The_administrators_request_carries_the_admin_gate_the_re_authentication_and_its_audit_row()
    {
        typeof(RequestAccountEmailChangeCommand).GetInterfaces().ShouldContain(typeof(IAdminRequest));
        typeof(RequestAccountEmailChangeCommand).GetInterfaces().ShouldContain(typeof(IReauthenticatingRequest));
        typeof(RequestAccountEmailChangeCommand).GetInterfaces().ShouldContain(typeof(IAuditableCommand));
    }

    [Fact]
    public void The_cancel_and_the_read_carry_the_admin_gate_and_no_re_authentication()
    {
        // A cancel only removes exposure, so it costs no code; the read changes nothing.
        foreach (var message in new[] { typeof(CancelAccountEmailChangeCommand), typeof(GetPendingAccountEmailChangeQuery) })
        {
            message.GetInterfaces().ShouldContain(typeof(IAdminRequest));
            message.GetInterfaces().ShouldNotContain(typeof(IReauthenticatingRequest));
        }

        typeof(CancelAccountEmailChangeCommand).GetInterfaces().ShouldContain(typeof(IAuditableCommand));
        typeof(GetPendingAccountEmailChangeQuery).GetInterfaces().ShouldNotContain(typeof(IAuditableCommand));
    }

    public static TheoryData<string> PrintedRecords() =>
    [
        nameof(RequestAccountEmailChangeCommand),
        nameof(AdminAccountsEndpoints.AccountEmailChangeRequest),
        nameof(NewAccountEmailChange),
        nameof(AccountEmailChangeProof),
        nameof(AccountEmailChangePut.Written),
        nameof(ExpectedCurrentAddress),
        nameof(AddressSwapped),
    ];

    private const string Address = "kansligt.namn@example.se";
    private const string Secret = "secret-grant-or-code-value"; // gitleaks:allow

    private static object Sample(string name) => name switch
    {
        nameof(RequestAccountEmailChangeCommand) => new RequestAccountEmailChangeCommand(Guid.NewGuid(), Address, Secret),
        nameof(AdminAccountsEndpoints.AccountEmailChangeRequest) => new AdminAccountsEndpoints.AccountEmailChangeRequest(Address, Secret),
        nameof(NewAccountEmailChange) => new NewAccountEmailChange(Guid.NewGuid(), Address, Address),
        nameof(AccountEmailChangeProof) => new AccountEmailChangeProof(Guid.NewGuid(), Address, new ExpectedCurrentAddress(Secret)),
        nameof(AccountEmailChangePut.Written) => new AccountEmailChangePut.Written(
            LoginCode.FromRaw(Secret), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null!),
        nameof(ExpectedCurrentAddress) => new ExpectedCurrentAddress(Secret),
        nameof(AddressSwapped) => new AddressSwapped(Address),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(PrintedRecords))]
    public void Every_record_carrying_an_address_a_code_or_a_grant_prints_none_of_them(string name)
    {
        // A record's generated ToString prints every member, and a record reaches a log the day someone logs it.
        var sample = Sample(name);
        var printed = sample.ToString()!;

        sample.GetType().GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType.ShouldBe(sample.GetType());
        printed.ShouldNotContain(Address, Case.Insensitive);
        printed.ShouldNotContain(Secret, Case.Insensitive);
    }
}
