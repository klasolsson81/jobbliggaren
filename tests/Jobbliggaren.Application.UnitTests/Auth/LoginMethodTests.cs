using Jobbliggaren.Application.Auth;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class LoginMethodTests
{
    [Fact]
    public void LoginMethod_Unset_IsNoMethodAtAll() =>
        Enum.IsDefined(default(LoginMethod)).ShouldBeFalse();

    [Fact]
    public void LoginMethod_KeepsItsNumbers_WhenAProviderIsAdded()
    {
        // The audit line records the member; a renumbering would make an old line mean another method.
        ((int)LoginMethod.Code).ShouldBe(1);
        ((int)LoginMethod.Link).ShouldBe(2);
        ((int)LoginMethod.Google).ShouldBe(3);
        ((int)LoginMethod.GitHub).ShouldBe(4);
    }

    [Fact]
    public void LoginMethod_HasExactlyTheMembersThatAreShipped()
    {
        // #1745: LinkedIn = 5 is noted on #1746 and added there, never ahead of its provider (senior-cto-advisor §5).
        Enum.GetNames<LoginMethod>().ShouldBe(["Code", "Link", "Google", "GitHub"]);
    }
}
