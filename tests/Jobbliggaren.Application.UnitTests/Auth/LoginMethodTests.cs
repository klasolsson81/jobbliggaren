using Jobbliggaren.Application.Auth;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class LoginMethodTests
{
    [Fact]
    public void LoginMethod_Unset_IsNoMethodAtAll() =>
        Enum.IsDefined(default(LoginMethod)).ShouldBeFalse();
}
