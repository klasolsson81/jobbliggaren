using Jobbliggaren.Application.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1739 — the bound challenge's purpose is PERSISTED: its number names the record's protector and sits in the
/// index key. So the member set is pinned by name and by number, where a sentence would go stale.
/// </summary>
public class ChallengeBindingTests
{
    [Fact]
    public void ChallengePurpose_ShouldHoldExactlyItsTwoMembers_WhenEnumerated()
    {
        Enum.GetValues<ChallengePurpose>().ToDictionary(purpose => purpose.ToString(), purpose => (int)purpose)
            .ShouldBe(new Dictionary<string, int>
            {
                [nameof(ChallengePurpose.Reauthentication)] = 2,
                [nameof(ChallengePurpose.ChangeEmail)] = 3,
            }, ignoreOrder: true);
    }

    [Fact]
    public void ChallengePurpose_ShouldNotDefineZero_WhenAnUnsetValueIsChecked()
    {
        Enum.IsDefined(default(ChallengePurpose)).ShouldBeFalse();
    }

    [Fact]
    public void ChallengeBinding_ShouldCompareByPurposeAndUser_WhenTwoBindingsAreCompared()
    {
        var userId = Guid.NewGuid();

        new ChallengeBinding(ChallengePurpose.Reauthentication, userId)
            .ShouldBe(new ChallengeBinding(ChallengePurpose.Reauthentication, userId));
        new ChallengeBinding(ChallengePurpose.Reauthentication, userId)
            .ShouldNotBe(new ChallengeBinding(ChallengePurpose.ChangeEmail, userId));
        new ChallengeBinding(ChallengePurpose.Reauthentication, userId)
            .ShouldNotBe(new ChallengeBinding(ChallengePurpose.Reauthentication, Guid.NewGuid()));
    }
}
