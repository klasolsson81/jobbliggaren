using System.Text.Json;
using Jobbliggaren.Api.Endpoints;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The wire form of every <see cref="LoginOutcome"/>. The endpoint's mapper ends in an
/// <c>UnreachableException</c>, so a variant added without its arm would compile and answer 500; here every
/// variant of the closed union is handed to the mapper itself.
/// </summary>
public sealed class LoginOutcomeResultTests
{
    private static readonly Dictionary<string, (LoginOutcome Outcome, string WireName)> OneOfEach = new()
    {
        [nameof(LoginOutcome.SignedIn)] = (new LoginOutcome.SignedIn("session-id"), "signedIn"),
        [nameof(LoginOutcome.PendingDeletion)] =
            (new LoginOutcome.PendingDeletion(new DateOnly(2026, 10, 19)), "pendingDeletion"),
        [nameof(LoginOutcome.RegistrationClosed)] = (new LoginOutcome.RegistrationClosed(), "registrationClosed"),
        [nameof(LoginOutcome.ConsentRequired)] =
            (new LoginOutcome.ConsentRequired(GrantToken.FromRaw("AAECAwQFBgcICQoLDA0ODw")), "consentRequired"),
        [nameof(LoginOutcome.AccountUnavailable)] = (new LoginOutcome.AccountUnavailable(), "accountUnavailable"),
    };

    public static TheoryData<string> Variants() => [.. OneOfEach.Keys];

    private static JsonElement Body(IResult result)
    {
        ((IStatusCodeHttpResult)result).StatusCode.ShouldBe(StatusCodes.Status200OK);
        return JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);
    }

    [Fact]
    public void Every_variant_of_the_closed_union_is_listed_here()
    {
        var variants = typeof(LoginOutcome).GetNestedTypes()
            .Where(type => type.IsSubclassOf(typeof(LoginOutcome)))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        OneOfEach.Keys.Order(StringComparer.Ordinal).ShouldBe(variants);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_variant_is_a_200_carrying_its_outcome_name(string variant)
    {
        var (outcome, wireName) = OneOfEach[variant];

        Body(AuthEndpoints.LoginOutcomeResult(outcome)).GetProperty("outcome").GetString().ShouldBe(wireName);
    }

    [Fact]
    public void Consent_required_carries_the_whole_grant_token_and_nothing_else()
    {
        var body = Body(AuthEndpoints.LoginOutcomeResult(OneOfEach[nameof(LoginOutcome.ConsentRequired)].Outcome));

        body.GetProperty("grantToken").GetString().ShouldBe("AAECAwQFBgcICQoLDA0ODw");
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["outcome", "grantToken"]);
    }

    [Fact]
    public void A_printed_consent_outcome_never_shows_the_whole_grant_token()
    {
        OneOfEach[nameof(LoginOutcome.ConsentRequired)].Outcome.ToString()!.ShouldNotContain("AAECAwQFBgcICQoLDA0ODw");
    }
}
