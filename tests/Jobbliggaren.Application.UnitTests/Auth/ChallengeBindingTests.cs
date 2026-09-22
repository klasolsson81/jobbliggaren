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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Constructor_ShouldThrow_WhenThePurposeIsNotDefined(int purpose)
    {
        // An undefined number would otherwise name a working protector of its own.
        Should.Throw<ArgumentOutOfRangeException>(() => new ChallengeBinding((ChallengePurpose)purpose, Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTheUserIdIsEmpty()
    {
        // The empty id is what an unset current user would assert.
        Should.Throw<ArgumentException>(() => new ChallengeBinding(ChallengePurpose.Reauthentication, Guid.Empty));
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

    [Fact]
    public void ChallengeBinding_ShouldHaveNoSetter_OnEitherProperty()
    {
        // Get-only is the invariant, not a style choice (dotnet-architect, #1793): the type is a record, so the
        // compiler's copy constructor bypasses the validating one, and that is harmless ONLY while neither
        // property can be set afterwards. An `init` accessor would let `with { Purpose = (ChallengePurpose)9 }`
        // compile and reach the store; the three refusal facts above would stay green, since they call the
        // constructor.
        var properties = typeof(ChallengeBinding).GetProperties();
        properties.Select(p => p.Name).ShouldBe(["Purpose", "UserId"], ignoreOrder: true);
        foreach (var property in properties)
            property.SetMethod.ShouldBeNull($"{property.Name} must be get-only");
    }
}
