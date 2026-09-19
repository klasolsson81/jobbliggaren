using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// Pins the attempt budget's parameters to ADR 0142's literals. The assertions spell the numbers out rather
/// than reading the constants back: a test that read the constant would move with it and could not fail.
/// </summary>
public sealed class LoginChallengePolicyTests
{
    private const string LapseTrigger5 =
        "ADR 0142 'Attempt budget' lapse trigger 5: a change to the code length, the attempt count or the "
        + "mint budget lapses the accepted risk and needs a new measurement recorded in an amendment";

    [Fact]
    public void The_code_is_six_digits_with_three_attempts_and_lives_fifteen_minutes()
    {
        LoginChallengePolicy.CodeLength.ShouldBe(6, LapseTrigger5);
        LoginChallengePolicy.MaxAttempts.ShouldBe(3, LapseTrigger5);
        LoginChallengePolicy.ChallengeTtl.ShouldBe(TimeSpan.FromMinutes(15), LapseTrigger5);
    }

    [Fact]
    public void The_mail_budget_is_three_per_ten_minutes()
    {
        LoginChallengePolicy.MailBudget.Limit.ShouldBe(3, LapseTrigger5);
        LoginChallengePolicy.MailBudget.Window.ShouldBe(TimeSpan.FromMinutes(10), LapseTrigger5);
    }

    [Fact]
    public void The_code_budget_is_ten_per_twenty_four_hours()
    {
        LoginChallengePolicy.CodeBudget.Limit.ShouldBe(10, LapseTrigger5);
        LoginChallengePolicy.CodeBudget.Window.ShouldBe(TimeSpan.FromHours(24), LapseTrigger5);
    }

    [Fact]
    public void The_cooldown_admits_one_call_per_window_it_is_given()
    {
        var scope = LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60));

        scope.Limit.ShouldBe(1);
        scope.Window.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void The_three_scopes_have_distinct_names_so_their_counters_never_collide()
    {
        // The names are Redis key segments. Two scopes sharing one would share one counter, and the key's
        // TTL would be set by whichever was counted first.
        string[] names =
        [
            LoginChallengePolicy.MailBudget.Name,
            LoginChallengePolicy.CodeBudget.Name,
            LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60)).Name,
        ];

        names.Distinct().Count().ShouldBe(3);
    }

    [Theory]
    [InlineData("", 1, 60)]
    [InlineData("scope", 0, 60)]
    [InlineData("scope", 1, 0)]
    public void A_scope_refuses_an_empty_name_a_zero_limit_or_a_zero_window(string name, int limit, int seconds)
    {
        Should.Throw<ArgumentException>(() => new RateBudgetScope(name, limit, TimeSpan.FromSeconds(seconds)));
    }
}
