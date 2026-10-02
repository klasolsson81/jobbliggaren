using System.Reflection;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateMatchPreferences;
using Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #551 — a dimension added to <see cref="MatchPreferences"/> must reach BOTH the write contract
/// and the read projection
/// (<see cref="JobSeekerProfileDto"/>). Nothing bound those three before this guard, and the gap
/// is what shipped a defect: <c>PreferredRemote</c> landed on the VO, the jsonb converter and the
/// command, but not on the profile DTO. The compiler saw nothing — the DTO is an independent
/// declaration — and the frontend then required the key on the strength of a comment asserting the
/// backend projected it. Every profile read failed to parse, and only an observe-only CI job
/// noticed. This test is RED against that state and green now.
///
/// <para>
/// <b>Name-based on purpose</b>, unlike the shape-based guards elsewhere in this suite. The
/// property NAME already IS the contract in three layers — it is the jsonb key
/// (<c>MatchPreferencesConverters</c> writes it by name), the wire key on both the command body and
/// the profile response (camelCased by the default JSON policy), and the FE Zod key. A rename that
/// this guard would miss is a rename that breaks persisted data first, and there are pins for that.
/// </para>
///
/// <para>
/// Authored in the same PR as the fix, and deliberately in the shape
/// <see cref="MatchProfileRemoteIndependenceTests"/> established: guard the invariant, not the
/// instance. Written this way, it would have failed the day the preference was added — before the
/// frontend that depended on it existed.
/// </para>
/// </summary>
public class MatchPreferencesContractParityTests
{
    // Members that are NOT user-stated dimensions and therefore have no place on either contract.
    // An explicit allow-list rather than a filter on shape, so the default — silence — FAILS the
    // test rather than passing it.
    //
    // EMPTY today, and measured so: every public instance property on the VO is a stated
    // dimension. It exists as the seam for a future member that genuinely is not one — plumbing,
    // a derived flag — so such a member is classified by a human rather than quietly widening the
    // guard. Equals/GetHashCode are methods, not properties, and never reach GetProperties.
    private static readonly HashSet<string> NotUserStatedDimensions = new(StringComparer.Ordinal);

    [Fact]
    public void EveryStatedDimension_ReachesTheReadProjection()
        => AssertParity(typeof(JobSeekerProfileDto), "read projection");

    // #1918 — the per-part write partitions the dimensions: each one sits in exactly
    // one part. A dimension in no part cannot be written; a dimension in two parts is written by
    // either, so saving one part could silently overwrite the other's value. The command carries
    // nothing but its parts, and a part carries nothing but dimensions.
    [Fact]
    public void EveryStatedDimension_BelongsToExactlyOnePartOfThePerPartWrite()
    {
        var commandMembers = typeof(UpdateMatchPreferencesCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var parts = commandMembers
            .Where(p => p.PropertyType.Name.EndsWith("PartInput", StringComparison.Ordinal))
            .ToArray();
        parts.ShouldNotBeEmpty("the partition measures nothing if the command exposes no parts");
        commandMembers.Select(p => p.Name).Except(parts.Select(p => p.Name)).ShouldBeEmpty(
            "a member of the per-part command outside every *PartInput is a write the partition does not see");

        var dimensions = StatedDimensions().ToHashSet(StringComparer.Ordinal);
        var owners = dimensions.ToDictionary(
            dimension => dimension,
            dimension => parts
                .Where(part => MembersOf(part.PropertyType).Contains(dimension))
                .Select(part => part.Name)
                .ToArray(),
            StringComparer.Ordinal);

        owners.Where(o => o.Value.Length == 0).Select(o => o.Key).Order(StringComparer.Ordinal).ShouldBeEmpty(
            "every stated MatchPreferences dimension must be writable through exactly one part");
        owners.Where(o => o.Value.Length > 1)
            .Select(o => $"{o.Key} in {string.Join(" and ", o.Value)}")
            .ShouldBeEmpty("a dimension in two parts is overwritten by a save of either");
        parts
            .SelectMany(part => MembersOf(part.PropertyType)
                .Where(member => !dimensions.Contains(member))
                .Select(member => $"{part.Name}.{member}"))
            .ShouldBeEmpty("a part member that is not a MatchPreferences dimension binds a value nothing stores");
    }

    // A member missing from a present part is a 400 at binding, never a default that erases a
    // value: a missing bool binds to false, a missing list to null. The years overlay is the single
    // exception: its absence keeps the stored years.
    [Fact]
    public void EveryPartMember_IsRequiredOnTheWire_ExceptTheYearsOverlay()
    {
        var parts = typeof(UpdateMatchPreferencesCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .ToArray();
        parts.ShouldNotBeEmpty();

        var members = parts
            .SelectMany(part => part.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(member => (Name: $"{part.Name}.{member.Name}", Member: member)))
            .ToArray();
        members.ShouldNotBeEmpty("the check measures nothing if the parts expose no members");

        members
            .Where(m => m.Member.Name != nameof(MatchPreferences.PreferredOccupationExperience)
                        && m.Member.GetCustomAttribute<JsonRequiredAttribute>() is null)
            .Select(m => m.Name)
            .ShouldBeEmpty("every part member except the years overlay carries [property: JsonRequired]");
        members
            .Where(m => m.Member.Name == nameof(MatchPreferences.PreferredOccupationExperience)
                        && m.Member.GetCustomAttribute<JsonRequiredAttribute>() is not null)
            .Select(m => m.Name)
            .ShouldBeEmpty("the years overlay is optional: an absent overlay keeps the stored years");
    }

    private static HashSet<string> MembersOf(Type part) =>
        part.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static void AssertParity(Type contract, string role)
    {
        var contractMembers = contract
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var dimensions = StatedDimensions().ToArray();

        // Floor against a broken source set: an inclusion spec can never detect that it is
        // measuring nothing. If StatedDimensions() ever comes back empty — allow-list widened,
        // properties no longer public, the VO restructured — `missing` is empty too.
        dimensions.ShouldNotBeEmpty(
            "the guard measures nothing if MatchPreferences exposes no stated dimensions");

        var missing = dimensions
            .Where(name => !contractMembers.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            $"every stated MatchPreferences dimension must reach the {role} "
            + $"({contract.Name}). Missing: {string.Join(", ", missing)}.");
    }

    private static IEnumerable<string> StatedDimensions() =>
        typeof(MatchPreferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !NotUserStatedDimensions.Contains(name));
}
