using Jobbliggaren.Application.Auth.Grants;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1739 — what a caller may assert when it redeems a grant (ADR 0142 D3). A purpose is caller-asserted unless
/// it is declared bearer-bound, so the two purposes 3a adds can only be redeemed with their binding.
/// </summary>
public class GrantAssertionTests
{
    [Fact]
    public void Bearer_ShouldCarryNoBinding_WhenThePurposeIsLoginComplete()
    {
        var assertion = GrantAssertion.Bearer(GrantPurpose.LoginComplete);

        assertion.Purpose.ShouldBe(GrantPurpose.LoginComplete);
        assertion.Binding.ShouldBeNull();
    }

    [Theory]
    [InlineData(GrantPurpose.Reauthentication)]
    [InlineData(GrantPurpose.ChangeEmail)]
    public void Bearer_ShouldThrow_WhenThePurposeIsCallerAsserted(GrantPurpose purpose)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GrantAssertion.Bearer(purpose));
    }

    [Fact]
    public void Of_ShouldCarryTheBindingAndItsPurpose_WhenGivenASubject()
    {
        var binding = new GrantSubject.ChangeEmail(Guid.NewGuid(), "ny@example.se");

        var assertion = GrantAssertion.Of(binding);

        assertion.Purpose.ShouldBe(GrantPurpose.ChangeEmail);
        assertion.Binding.ShouldBe(binding);
    }

    [Fact]
    public void GrantPurpose_ShouldHaveExactlyOneSubjectVariantPerMember_WhenCountedByName()
    {
        // The adapter switches on both: a purpose without a variant could be persisted by no one, and a variant
        // without a purpose could not name its protector.
        var variants = typeof(GrantSubject).GetNestedTypes()
            .Where(type => type.IsSubclassOf(typeof(GrantSubject)))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        variants.ShouldBe(Enum.GetNames<GrantPurpose>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GrantPurpose_ShouldKeepItsPersistedNumbers_WhenMembersAreAdded()
    {
        // The number is persisted in the record and names the protector's sub-purpose: a renumbering would make
        // a live grant unreadable, and a reused number would open one purpose's payload as another's.
        ((int)GrantPurpose.LoginComplete).ShouldBe(1);
        ((int)GrantPurpose.Reauthentication).ShouldBe(2);
        ((int)GrantPurpose.ChangeEmail).ShouldBe(3);
    }
}
