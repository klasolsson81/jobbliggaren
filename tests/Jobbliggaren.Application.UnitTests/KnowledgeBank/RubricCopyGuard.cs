using System.Text.RegularExpressions;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// The version-token rule for the rubric's user-facing copy (#2083): the same pattern
/// <c>ReviewCopyVersionJargonGuardTests</c> applies to the C# review copy, so "(v1)", "v2" or "v1,"
/// fails in a <c>notAssessedReason</c> or <c>passCriterion</c> as it does in a string literal.
/// </summary>
internal static class RubricCopyGuard
{
    internal static readonly Regex VersionToken = new(
        @"(?<![\p{L}\p{N}_.\-/])v\d+(?![\p{L}\p{N}_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
