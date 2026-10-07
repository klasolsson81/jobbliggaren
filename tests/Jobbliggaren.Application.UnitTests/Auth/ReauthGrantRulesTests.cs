using FluentValidation;
using Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Validation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1739 — the one grant rule, applied by every <see cref="IReauthenticatingRequest"/> implementer. The
/// tripwire pins that a validator EXISTS; this pins what every validator says about the grant, so they cannot
/// drift: empty is refused, a grant over the bound is refused, and nothing in between is.
/// </summary>
public sealed class ReauthGrantRulesTests
{
    private const string Grant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow

    public static TheoryData<string, Func<string?, IReauthenticatingRequest>, Func<IValidator>> Implementers() => new()
    {
        { nameof(DeleteAccountCommand), grant => new DeleteAccountCommand(grant), () => new DeleteAccountCommandValidator() },
        { nameof(ChangeEmailCommand), grant => new ChangeEmailCommand(grant, "ny@example.se"), () => new ChangeEmailCommandValidator() },
        {
            nameof(RequestAccountEmailChangeCommand),
            grant => new RequestAccountEmailChangeCommand(Guid.NewGuid(), "ny@example.se", grant),
            () => new RequestAccountEmailChangeCommandValidator()
        },
        {
            nameof(SuspendAccountCommand), grant => new SuspendAccountCommand(Guid.NewGuid(), grant),
            () => new SuspendAccountCommandValidator()
        },
        {
            nameof(ReinstateAccountCommand), grant => new ReinstateAccountCommand(Guid.NewGuid(), grant),
            () => new ReinstateAccountCommandValidator()
        },
    };

    private static FluentValidation.Results.ValidationResult Validate(IValidator validator, object command) =>
        validator.Validate(new ValidationContext<object>(command));

    [Theory]
    [MemberData(nameof(Implementers))]
    public void A_grant_within_the_bound_passes(
        string name, Func<string?, IReauthenticatingRequest> command, Func<IValidator> validator)
    {
        _ = name;
        var atTheBound = new string('a', ReauthGrantRules.MaximumLength);

        Validate(validator(), command(Grant)).Errors.ShouldNotContain(e => e.PropertyName == "ReauthGrant");
        Validate(validator(), command(atTheBound)).Errors.ShouldNotContain(e => e.PropertyName == "ReauthGrant");
    }

    [Theory]
    [MemberData(nameof(Implementers))]
    public void An_empty_grant_is_refused_on_the_grant_property(
        string name, Func<string?, IReauthenticatingRequest> command, Func<IValidator> validator)
    {
        _ = name;

        foreach (var empty in new[] { null, string.Empty })
            Validate(validator(), command(empty)).Errors.ShouldContain(e => e.PropertyName == "ReauthGrant");
    }

    [Theory]
    [MemberData(nameof(Implementers))]
    public void A_grant_over_the_bound_is_refused_on_the_grant_property(
        string name, Func<string?, IReauthenticatingRequest> command, Func<IValidator> validator)
    {
        _ = name;
        var overTheBound = new string('a', ReauthGrantRules.MaximumLength + 1);

        Validate(validator(), command(overTheBound)).Errors.ShouldContain(e => e.PropertyName == "ReauthGrant");
    }

    [Fact]
    public void The_implementers_are_the_whole_set()
    {
        // The tripwire pins that every implementer HAS a validator; the theories above pin what the validators say.
        // One more implementer would pass the tripwire with any validator and reach none of the theories, so the set
        // is pinned here: adding one means adding its row.
        typeof(IReauthenticatingRequest).Assembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && typeof(IReauthenticatingRequest).IsAssignableFrom(t))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe([nameof(ChangeEmailCommand), nameof(DeleteAccountCommand), nameof(ReinstateAccountCommand),
                nameof(RequestAccountEmailChangeCommand), nameof(SuspendAccountCommand)]);
    }

    [Fact]
    public void The_bound_admits_a_minted_grant()
    {
        Jobbliggaren.Application.Auth.Grants.GrantToken.Generate().Reveal().Length
            .ShouldBeLessThanOrEqualTo(ReauthGrantRules.MaximumLength);
    }
}
