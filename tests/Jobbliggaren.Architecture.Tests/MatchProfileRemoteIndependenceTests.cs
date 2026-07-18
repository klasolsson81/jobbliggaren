using System.Reflection;
using Jobbliggaren.Application.Matching.Abstractions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #551 Finding F1 (senior-cto-advisor bind, load-bearing) — the remote/distans grade override reads
/// the AD's remote flag, NEVER the USER's remote preference. That is the exact seam where the two #551
/// mechanisms could cross and break ADR 0079's "never grade-coupled" invariant:
/// <list type="bullet">
///   <item><b>Mechanism A (this PR, grade):</b> <c>MatchScorer.ScoreOrtUnion(..., bool adRemote)</c> —
///     ad-driven, in the grade path.</item>
///   <item><b>Mechanism B (PR-B, facet filter):</b> a user <c>PreferredRemote</c> on
///     <c>MatchPreferences</c>, feeding the facet-hard notis count via <c>JobAdFilterCriteria</c> — NEVER
///     the scorer.</item>
/// </list>
/// PR-B will add the user preference. This guard fires the moment that preference could reach the scorer:
/// the scorer's CV-side inputs — <see cref="CandidateMatchProfile"/> and
/// <see cref="FullCandidateMatchProfile"/> — must expose NO member whose name mentions "remote". If PR-B
/// (or any later change) threads a remote PREFERENCE into the scorer's profile, this test goes red before
/// the grade can silently start depending on it. Authored in PR-A deliberately, before the field it guards
/// against exists (guard the invariant before the thing that could violate it).
/// </summary>
public class MatchProfileRemoteIndependenceTests
{
    [Fact]
    public void CandidateMatchProfile_ExposesNoRemotePreferenceMember_SoTheGradeStaysAdDriven()
    {
        AssertNoRemoteMember(typeof(CandidateMatchProfile));
    }

    [Fact]
    public void FullCandidateMatchProfile_ExposesNoRemotePreferenceMember_SoTheGradeStaysAdDriven()
    {
        AssertNoRemoteMember(typeof(FullCandidateMatchProfile));
    }

    private static void AssertNoRemoteMember(Type profileType)
    {
        var remoteMembers = profileType
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m is PropertyInfo or FieldInfo)
            .Where(m => m.Name.Contains("remote", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        remoteMembers.ShouldBeEmpty(
            $"{profileType.Name} is a SCORER input. A remote member here would let the grade read a USER " +
            "remote PREFERENCE — the #551 F1 seam ADR 0079 forbids (the scorer reads the AD's remote flag " +
            "only). The user remote preference belongs on MatchPreferences → JobAdFilterCriteria (the " +
            "facet-hard count, PR-B), never on the scorer's profile. Found: " +
            string.Join(", ", remoteMembers));
    }
}
