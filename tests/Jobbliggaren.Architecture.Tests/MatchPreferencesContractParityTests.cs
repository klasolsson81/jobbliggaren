using System.Reflection;
using Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;
using Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #551 — a dimension added to <see cref="MatchPreferences"/> must reach BOTH the write contract
/// (<see cref="SetMatchPreferencesCommand"/>) and the read projection
/// (<see cref="JobSeekerProfileDto"/>). Nothing bound those three before this guard, and the gap
/// is what shipped a defect: <c>PreferredRemote</c> landed on the VO, the jsonb converter and the
/// command, but not on the profile DTO. The compiler saw nothing — the DTO is an independent
/// declaration — and the frontend then required the key on the strength of a comment asserting the
/// backend projected it. Every profile read failed to parse, and only an observe-only CI job
/// noticed. This test is RED against that state and green now.
///
/// <para>
/// <b>Why the read side matters as much as the write side.</b> The write is a full-replace PUT, so
/// a dimension missing from the projection cannot be pre-filled — and saving ANY other dimension
/// then sends the type's default for it, silently discarding a stated preference. That is the
/// page-wipe class <c>PreferredMunicipalities</c> already carries a written warning about in the
/// same DTO. A missing read member is therefore not a cosmetic omission; it is data loss with no
/// error surface.
/// </para>
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
    public void EveryStatedDimension_ReachesTheWriteContract()
        => AssertParity(typeof(SetMatchPreferencesCommand), "write contract");

    [Fact]
    public void EveryStatedDimension_ReachesTheReadProjection()
        => AssertParity(typeof(JobSeekerProfileDto), "read projection");

    private static void AssertParity(Type contract, string role)
    {
        var contractMembers = contract
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var dimensions = StatedDimensions().ToArray();

        // Floor against a broken source set: an inclusion spec can never detect that it is
        // measuring nothing. If StatedDimensions() ever comes back empty — allow-list widened,
        // properties no longer public, the VO restructured — `missing` is empty too and BOTH
        // facts pass green and silent.
        dimensions.ShouldNotBeEmpty(
            "the guard measures nothing if MatchPreferences exposes no stated dimensions");

        var missing = dimensions
            .Where(name => !contractMembers.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            $"every stated MatchPreferences dimension must reach the {role} "
            + $"({contract.Name}). Missing: {string.Join(", ", missing)}. "
            + "A dimension the read projection omits cannot be pre-filled, and the full-replace "
            + "PUT then discards it on the next save of any other dimension.");
    }

    private static IEnumerable<string> StatedDimensions() =>
        typeof(MatchPreferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !NotUserStatedDimensions.Contains(name));
}
