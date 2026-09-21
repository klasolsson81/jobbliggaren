using Jobbliggaren.Application.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The plan as one table (senior-cto-advisor, 2026-09-19 and 2026-09-20): every subject crossed with both
/// code-budget states and both registration states. The registration state moves only the two subjects that
/// have no usable account.
/// </summary>
public sealed class LoginChallengePlanTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    public static TheoryData<string, CodeBudgetState, RegistrationState, LoginChallengeKind> Table() => new()
    {
        { "active", CodeBudgetState.Admitted, RegistrationState.Closed, LoginChallengeKind.CodeAndLink },
        { "active", CodeBudgetState.Admitted, RegistrationState.Open, LoginChallengeKind.CodeAndLink },
        { "active", CodeBudgetState.Exhausted, RegistrationState.Closed, LoginChallengeKind.LinkOnly },
        { "active", CodeBudgetState.Exhausted, RegistrationState.Open, LoginChallengeKind.LinkOnly },
        { "pending-deletion", CodeBudgetState.Admitted, RegistrationState.Closed, LoginChallengeKind.PendingDeletion },
        { "pending-deletion", CodeBudgetState.Admitted, RegistrationState.Open, LoginChallengeKind.PendingDeletion },
        { "pending-deletion", CodeBudgetState.Exhausted, RegistrationState.Closed, LoginChallengeKind.PendingDeletion },
        { "pending-deletion", CodeBudgetState.Exhausted, RegistrationState.Open, LoginChallengeKind.PendingDeletion },
        { "no-account", CodeBudgetState.Admitted, RegistrationState.Closed, LoginChallengeKind.RegistrationClosed },
        { "no-account", CodeBudgetState.Exhausted, RegistrationState.Closed, LoginChallengeKind.RegistrationClosed },
        { "no-account", CodeBudgetState.Admitted, RegistrationState.Open, LoginChallengeKind.NewAccountCode },
        { "no-account", CodeBudgetState.Exhausted, RegistrationState.Open, LoginChallengeKind.NewAccountCodeLimitReached },
        { "profile-missing", CodeBudgetState.Admitted, RegistrationState.Closed, LoginChallengeKind.RegistrationClosed },
        { "profile-missing", CodeBudgetState.Exhausted, RegistrationState.Closed, LoginChallengeKind.RegistrationClosed },
        { "profile-missing", CodeBudgetState.Admitted, RegistrationState.Open, LoginChallengeKind.NewAccountCode },
        { "profile-missing", CodeBudgetState.Exhausted, RegistrationState.Open, LoginChallengeKind.NewAccountCodeLimitReached },
    };

    private static LoginSubject Subject(string name) => name switch
    {
        "active" => new LoginSubject.Active(UserId, "person@example.com"),
        "pending-deletion" => new LoginSubject.PendingDeletion(UserId, "person@example.com", DateTimeOffset.UnixEpoch),
        "no-account" => new LoginSubject.NoAccount(),
        "profile-missing" => new LoginSubject.ProfileMissing(UserId, "person@example.com"),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Every_subject_budget_state_and_registration_state_maps_to_exactly_one_mail(
        string subject, CodeBudgetState codeBudget, RegistrationState registration, LoginChallengeKind expected)
    {
        LoginChallengePlan.Decide(Subject(subject), codeBudget, registration).ShouldBe(expected);
    }

    [Theory]
    [InlineData(LoginChallengeKind.CodeAndLink, ChallengeCredentials.CodeAndLink)]
    [InlineData(LoginChallengeKind.LinkOnly, ChallengeCredentials.LinkOnly)]
    [InlineData(LoginChallengeKind.NewAccountCode, ChallengeCredentials.CodeOnly)]
    [InlineData(LoginChallengeKind.NewAccountCodeLimitReached, ChallengeCredentials.None)]
    [InlineData(LoginChallengeKind.PendingDeletion, ChallengeCredentials.None)]
    [InlineData(LoginChallengeKind.RegistrationClosed, ChallengeCredentials.None)]
    public void Each_kind_is_minted_the_credentials_its_mail_carries(
        LoginChallengeKind kind, ChallengeCredentials expected)
    {
        LoginChallengePlan.CredentialsFor(kind).ShouldBe(expected);
    }

    [Fact]
    public void A_subject_without_an_account_is_never_minted_a_link()
    {
        // ADR 0142 D1: a magic link is for an existing account only, and the defence is in the record.
        var mintedForNoAccount = Table()
            .Where(row => row.Data.Item1 is "no-account" or "profile-missing")
            .Select(row => LoginChallengePlan.CredentialsFor(row.Data.Item4))
            .Distinct();

        mintedForNoAccount.ShouldBe([ChallengeCredentials.None, ChallengeCredentials.CodeOnly], ignoreOrder: true);
    }

    [Fact]
    public void The_table_covers_every_kind()
    {
        Table().Select(row => row.Data.Item4).Distinct().Order()
            .ShouldBe(Enum.GetValues<LoginChallengeKind>().Order());
    }

    [Fact]
    public void An_unset_registration_state_is_closed()
    {
        // ADR 0083: absent configuration must not open the gate.
        default(RegistrationState).ShouldBe(RegistrationState.Closed);
    }
}
