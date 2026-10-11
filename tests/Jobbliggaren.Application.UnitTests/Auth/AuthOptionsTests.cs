using Jobbliggaren.Application.Auth;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class AuthOptionsTests
{
    // The compose default opens the gate (Klas-direktiv 2026-10-11), so this code default is what
    // keeps every host the compose file does not feed closed.
    [Fact]
    public void RegistrationsOpen_Unset_IsClosed() =>
        new AuthOptions().RegistrationsOpen.ShouldBeFalse();
}
