using System.Text.Json;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence.Configurations;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches;

// #551 PR-B D6 — the WatchFilterSpec jsonb converter must round-trip the new remote axis and stay
// forward/backward compatible + fail-closed on a non-boolean value. The converter is
// Infrastructure-internal, exercised here through the EF ValueConverter (InternalsVisibleTo, parity
// MatchPreferencesConverterTests). The real-Postgres jsonb path is covered by
// CompanyWatchFilterJsonbBackcompatTests; this pins the pure serialization contract cheaply, incl.
// the default-deny THROW branch a Testcontainers reload would only surface wrapped in an EF exception.
public class WatchFilterSpecConverterTests
{
    private static string ToJson(WatchFilterSpec s) =>
        (string)WatchFilterSpecConversion.Converter.ConvertToProvider(s)!;

    private static WatchFilterSpec FromJson(string json) =>
        (WatchFilterSpec)WatchFilterSpecConversion.Converter.ConvertFromProvider(json)!;

    [Fact]
    public void RoundTrip_PreservesRemote_True()
    {
        // A remote-only spec (no ort, not OnlyMatched) is valid and must survive the round-trip.
        var original = WatchFilterSpec.Create(
            municipalities: null, regions: null, onlyMatched: false, remote: true).Value;

        var restored = FromJson(ToJson(original));

        restored.Remote.ShouldBeTrue();
        restored.ShouldBe(original);
        // The jsonb-key contract is the VO property name (PascalCase).
        ToJson(original).Replace(" ", string.Empty, StringComparison.Ordinal)
            .ShouldContain("\"Remote\":true");
    }

    [Fact]
    public void Read_MissingRemoteKey_DefaultsToFalse()
    {
        // A row written before #551 has no "Remote" key → false, re-validated green in Create.
        const string oldRow = """{"Municipalities": ["gbg_kn"], "Regions": [], "OnlyMatched": false}""";

        FromJson(oldRow).Remote.ShouldBeFalse();
    }

    [Fact]
    public void Read_NonBooleanRemote_FailsClosed()
    {
        // Default-deny (parity OnlyMatched): a string/number in the bool key is rejected, never
        // silently coerced. Mutation of the `_ => throw` arm to `_ => false` reds this.
        const string corrupt = """{"Municipalities": ["gbg_kn"], "Remote": "yes"}""";

        Should.Throw<JsonException>(() => FromJson(corrupt));
    }
}
