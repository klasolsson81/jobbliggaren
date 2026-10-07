using System.Reflection;
using Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Infrastructure.Auth.Access;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

public sealed class AccountAccessProtocolTests
{
    [Theory]
    [InlineData(typeof(SuspendAccountCommand))]
    [InlineData(typeof(ReinstateAccountCommand))]
    public void Administrator_access_commands_must_carry_all_server_enforcement_markers(Type command)
    {
        command.IsAssignableTo(typeof(IAdminRequest)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IReauthenticatingRequest)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAccountAccessMutation)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAuditableCommand)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(DeleteAccountCommand))]
    [InlineData(typeof(ConfirmEmailChangeCommand))]
    public void Authenticated_identity_mutations_must_join_the_same_account_scope(Type command)
    {
        command.IsAssignableTo(typeof(IAuthenticatedRequest)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAccountAccessMutation)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAuditableCommand)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(RequestAccountEmailChangeCommand))]
    [InlineData(typeof(ChangeEmailCommand))]
    public void Address_requests_own_their_two_scopes_so_transport_cannot_run_inside_the_generic_mutation_transaction(Type command)
    {
        command.IsAssignableTo(typeof(IOwnsAccountTransaction)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAccountAccessMutation)).ShouldBeFalse();
        command.IsAssignableTo(typeof(IAuditableCommand)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IReauthenticatingRequest)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(IAccountAccessReader))]
    [InlineData(typeof(IAccountAccessWriter))]
    [InlineData(typeof(IAccountAccessCoordinator))]
    public void Access_contracts_belong_to_Application_while_the_shared_physical_adapter_belongs_to_Infrastructure(Type port)
    {
        port.IsInterface.ShouldBeTrue();
        port.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
        typeof(SqlAccountAccess).Assembly.ShouldBe(typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly);
        typeof(SqlAccountAccess).IsAssignableTo(port).ShouldBeTrue();
    }

    [Fact]
    public void A_transport_or_adapter_cannot_mint_or_confirm_a_credential_transition_capability()
    {
        // Authority is returned by the handler and becomes usable only in Application's known-commit
        // pipeline. A public constructor or activation method would let a transport skip that proof.
        var capability = typeof(CommittedSessionAuthorization);
        capability.Assembly.ShouldBe(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
        capability.IsSealed.ShouldBeTrue();
        capability.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
        capability.GetProperty(nameof(CommittedSessionAuthorization.Lifetime)).ShouldNotBeNull()
            .SetMethod.ShouldBeNull("the originally admitted lifetime cannot be replaced by transport");
        foreach (var factory in new[] { "AfterFirstInboxProof", "AfterOwnAddressChange" })
        {
            capability.GetMethod(factory, BindingFlags.Static | BindingFlags.NonPublic).ShouldNotBeNull()
                .IsAssembly.ShouldBeTrue("only Application may create the original commit authority");
        }
        capability.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.Name != nameof(ToString))
            .ShouldBeEmpty();
    }
}
