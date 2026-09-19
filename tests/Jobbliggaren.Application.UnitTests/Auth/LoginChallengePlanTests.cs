using Jobbliggaren.Application.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// Part 1a's plan as one table (senior-cto-advisor, 2026-09-19): every subject crossed with both code-budget
/// states. An address without an account is never given a code in 1a — the new-address arm is part 1c.
/// </summary>
public sealed class LoginChallengePlanTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    public static TheoryData<string, CodeBudgetState, LoginChallengeKind> Table() => new()
    {
        { "active", CodeBudgetState.Admitted, LoginChallengeKind.CodeAndLink },
        { "active", CodeBudgetState.Exhausted, LoginChallengeKind.LinkOnly },
        { "pending-deletion", CodeBudgetState.Admitted, LoginChallengeKind.PendingDeletion },
        { "pending-deletion", CodeBudgetState.Exhausted, LoginChallengeKind.PendingDeletion },
        { "no-account", CodeBudgetState.Admitted, LoginChallengeKind.RegistrationClosed },
        { "no-account", CodeBudgetState.Exhausted, LoginChallengeKind.RegistrationClosed },
        { "profile-missing", CodeBudgetState.Admitted, LoginChallengeKind.RegistrationClosed },
        { "profile-missing", CodeBudgetState.Exhausted, LoginChallengeKind.RegistrationClosed },
    };

    private static LoginSubject Subject(string name) => name switch
    {
        "active" => new LoginSubject.Active(UserId),
        "pending-deletion" => new LoginSubject.PendingDeletion(UserId, DateTimeOffset.UnixEpoch),
        "no-account" => new LoginSubject.NoAccount(),
        "profile-missing" => new LoginSubject.ProfileMissing(UserId),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Every_subject_and_budget_state_maps_to_exactly_one_mail(
        string subject, CodeBudgetState codeBudget, LoginChallengeKind expected)
    {
        LoginChallengePlan.Decide(Subject(subject), codeBudget).ShouldBe(expected);
    }

    [Theory]
    [InlineData(LoginChallengeKind.CodeAndLink, ChallengeCredentials.CodeAndLink)]
    [InlineData(LoginChallengeKind.LinkOnly, ChallengeCredentials.LinkOnly)]
    [InlineData(LoginChallengeKind.PendingDeletion, ChallengeCredentials.None)]
    [InlineData(LoginChallengeKind.RegistrationClosed, ChallengeCredentials.None)]
    public void Only_an_active_account_is_minted_a_credential(LoginChallengeKind kind, ChallengeCredentials expected)
    {
        LoginChallengePlan.CredentialsFor(kind).ShouldBe(expected);
    }

    [Fact]
    public void The_table_covers_every_kind()
    {
        Table().Select(row => row.Data.Item3).Distinct().Order()
            .ShouldBe(Enum.GetValues<LoginChallengeKind>().Order());
    }
}
