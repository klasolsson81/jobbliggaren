using System.Reflection;
using Jobbliggaren.Domain.Matching;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0080 Vag 4 PR-1 (Decision 7) Goodhart anti-regression for the
/// <see cref="UserJobAdMatch"/> aggregate. Mirrors the wire-side Goodhart pins in
/// <see cref="MatchTagBatchLayerTests"/>, but for the PERSISTED record: a background-match
/// row stores the named <see cref="UserJobAdMatch.Grade"/> CATEGORY + timestamps + the
/// matched-skill evidence, and NEVER a numeric score/magnitude (ADR 0071/0076 — "a match
/// score as an opaque number" is the forbidden shape; matched skills are the explanation,
/// not a number). If a naked numeric column (an int/double/decimal "Score"/"Rank"/percentage)
/// ever lands on this aggregate, this test fails loud — the score would persist and silently
/// become a sort key, defeating the explainable-by-design floor.
/// </summary>
public class UserJobAdMatchGoodhartTests
{
    // The naked-number shapes a score/magnitude would take. The grade is an enum, the
    // timestamps are DateTimeOffset, the id/JobAdId are Guid-backed value objects, the
    // user id is a Guid, the evidence is a string list — none of these are in this set.
    private static readonly HashSet<Type> ForbiddenNumericTypes =
    [
        typeof(int), typeof(int?),
        typeof(long), typeof(long?),
        typeof(double), typeof(double?),
        typeof(decimal), typeof(decimal?),
        typeof(float), typeof(float?),
    ];

    private static PropertyInfo[] PublicInstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void UserJobAdMatch_has_no_numeric_or_score_typed_public_property()
    {
        // ADR 0080 Decision 7 / ADR 0071 — the Goodhart guard on the PERSISTED aggregate.
        // We assert by TYPE (not name): no public property may be a naked numeric. The
        // allowed shapes are the named grade enum, the DateTimeOffset(?) timestamps, the
        // Guid UserId, the UserJobAdMatchId/JobAdId value objects, and the string evidence
        // list — a stored magnitude is structurally impossible.
        var offending = PublicInstanceProperties(typeof(UserJobAdMatch))
            .Where(p => ForbiddenNumericTypes.Contains(p.PropertyType))
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .ToList();

        offending.ShouldBeEmpty(
            "UserJobAdMatch får INTE bära en numerisk/score-typad public property " +
            "(int/long/double/decimal/float eller nullable-variant). Matchen persisterar " +
            "den NAMNGIVNA Grade-kategorin + tidsstämplar + matchade kompetenser som " +
            "evidens — aldrig en lagrad magnitud som tyst blir en sorteringsnyckel " +
            "(ADR 0080 Decision 7 / ADR 0071 / CLAUDE.md §5 — \"a match score as an opaque " +
            $"number\" är den förbjudna formen). Otillåtna: [{string.Join(", ", offending)}].");
    }

    [Fact]
    public void UserJobAdMatch_Grade_is_the_named_NotifiableMatchGrade_enum_not_a_number()
    {
        // Belt-and-braces: the grade field is the named ordinal category, never an int/double.
        var grade = typeof(UserJobAdMatch)
            .GetProperty("Grade", BindingFlags.Public | BindingFlags.Instance);

        grade.ShouldNotBeNull("UserJobAdMatch ska ha en Grade-property.");
        grade!.PropertyType.ShouldBe(typeof(NotifiableMatchGrade),
            "Grade ska vara den namngivna NotifiableMatchGrade-kategorin, aldrig en " +
            "numerisk typ (Goodhart-vakten på den persisterade matchen).");
        grade.PropertyType.IsEnum.ShouldBeTrue();
    }

    [Fact]
    public void NotifiableMatchGrade_is_exactly_the_three_notifiable_rungs()
    {
        // The honest floor (D1): the persisted ladder is exactly { Good, Strong, Top } —
        // Basic / "no grade" is structurally excluded (never persisted as a match). No
        // numeric-band member; the enum is named rungs only.
        var names = Enum.GetNames<NotifiableMatchGrade>();

        names.ShouldBe(["Good", "Strong", "Top"], ignoreOrder: true,
            "NotifiableMatchGrade ska vara exakt { Good, Strong, Top } — den notifierbara " +
            "delmängden (ADR 0080 D1, honest floor; Basic strukturellt utesluten). " +
            $"Faktiska: [{string.Join(", ", names)}].");
    }
}
