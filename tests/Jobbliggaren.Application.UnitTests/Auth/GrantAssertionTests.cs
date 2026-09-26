using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1739, #1744, #1745 — what a caller may assert when it redeems a grant (ADR 0142 D3). A purpose is caller-asserted
/// unless it is declared bearer-bound, so only the registration purposes and the pending link can be redeemed without
/// their binding.
/// </summary>
public class GrantAssertionTests
{
    [Fact]
    public void Bearer_ShouldCarryNoBinding_WhenThePurposeIsLoginComplete()
    {
        var assertion = GrantAssertion.Bearer(GrantPurpose.LoginComplete);

        assertion.Purposes.ShouldBe([GrantPurpose.LoginComplete]);
        assertion.Binding.ShouldBeNull();
    }

    [Fact]
    public void Bearer_ShouldCarryBothRegistrationPurposes_WhenCompleteAcceptsACodeOrAProvider()
    {
        var assertion = GrantAssertion.Bearer(GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal);

        assertion.Purposes.ShouldBe([GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal]);
        assertion.Binding.ShouldBeNull();
    }

    [Theory]
    [InlineData(GrantPurpose.Reauthentication)]
    [InlineData(GrantPurpose.ChangeEmail)]
    public void Bearer_ShouldThrow_WhenThePurposeIsCallerAsserted(GrantPurpose purpose)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GrantAssertion.Bearer(purpose));
    }

    [Theory]
    [InlineData(GrantPurpose.Reauthentication)]
    [InlineData(GrantPurpose.ChangeEmail)]
    public void Bearer_ShouldThrow_WhenAnyOfSeveralPurposesIsCallerAsserted(GrantPurpose purpose)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GrantAssertion.Bearer(GrantPurpose.LoginComplete, purpose));
    }

    [Fact]
    public void Of_ShouldCarryTheBindingAndItsPurpose_WhenGivenASubject()
    {
        var binding = new GrantSubject.ChangeEmail(Guid.NewGuid(), "ny@example.se");

        var assertion = GrantAssertion.Of(binding);

        assertion.Purposes.ShouldBe([GrantPurpose.ChangeEmail]);
        assertion.Binding.ShouldBe(binding);
    }

    [Fact]
    public void Equality_ShouldHoldBetweenTwoAssertionsBuiltAlike_WhenTheyHoldTheirOwnPurposeLists()
    {
        // Handlers and their tests compare assertions they built separately; the purpose list takes part by value.
        var userId = Guid.NewGuid();

        GrantAssertion.Of(new GrantSubject.Reauthentication(userId))
            .ShouldBe(GrantAssertion.Of(new GrantSubject.Reauthentication(userId)));
        GrantAssertion.Bearer(GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal)
            .ShouldBe(GrantAssertion.Bearer(GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal));
        GrantAssertion.Bearer(GrantPurpose.LoginComplete)
            .ShouldNotBe(GrantAssertion.Bearer(GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal));
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
        ((int)GrantPurpose.LoginCompleteExternal).ShouldBe(4);
        ((int)GrantPurpose.PendingExternalLink).ShouldBe(5);
        ((int)GrantPurpose.LoginCompleteWithLink).ShouldBe(6);
    }

    [Theory]
    [InlineData(GrantPurpose.PendingExternalLink)]
    [InlineData(GrantPurpose.LoginCompleteWithLink)]
    public void Bearer_ShouldCarryNoBinding_WhenThePurposeIsOneOfTheLinkPurposes(GrantPurpose purpose)
    {
        // #1745 (dotnet-architect R2): the browser holds only the token, so both link purposes are bearer-bound.
        var assertion = GrantAssertion.Bearer(purpose);

        assertion.Purposes.ShouldBe([purpose]);
        assertion.Binding.ShouldBeNull();
    }

    [Fact]
    public async Task PendingExternalLink_ShouldCompareByItsAddressProviderAndSubject_WhenTwoAreBuilt()
    {
        // The address comes from the production GitHub adapter (GitHubIdentities); the other subject is the same
        // account's own identifier at another provider.
        var proof = await GitHubIdentities.AssertedProofAsync(
            GitHubApiShapes.User(58323117, "anna-berg-gh"), GitHubApiShapes.Emails.PrimaryVerified("anna@firma.example"));

        new GrantSubject.PendingExternalLink(proof.Address, proof.Provider, proof.Subject)
            .ShouldBe(new GrantSubject.PendingExternalLink(proof.Address, proof.Provider, proof.Subject));
        new GrantSubject.PendingExternalLink(proof.Address, proof.Provider, proof.Subject)
            .ShouldNotBe(new GrantSubject.PendingExternalLink(
                proof.Address, proof.Provider, ExternalSubject.TryCreate("2")!.Value));
    }

    [Fact]
    public async Task LoginCompleteExternal_ShouldCompareByItsAddressProviderAndSubject_WhenTwoAreBuilt()
    {
        var email = (await GoogleIdentities.ProofAsync(GoogleUserInfoShapes.Gmail("110248495921238986420", "a"))).Email;
        // Redeem compares a caller-asserted binding by record equality, so the subject's value must take part.
        var subject = ExternalSubject.TryCreate("110248495921238986420")!.Value;

        new GrantSubject.LoginCompleteExternal(email, ExternalProviderKey.Google, subject)
            .ShouldBe(new GrantSubject.LoginCompleteExternal(email, ExternalProviderKey.Google, subject));
        new GrantSubject.LoginCompleteExternal(email, ExternalProviderKey.Google, subject)
            .ShouldNotBe(new GrantSubject.LoginCompleteExternal(
                email, ExternalProviderKey.Google, ExternalSubject.TryCreate("2")!.Value));
    }
}
