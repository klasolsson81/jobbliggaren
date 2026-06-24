using Jobbliggaren.Domain.Matching;

namespace Jobbliggaren.Application.Matching.Notifications;

/// <summary>
/// Maps a <see cref="NotifiableMatchGrade"/> to its Swedish display LABEL for a background-match
/// notification email body (ADR 0080 Vag 4 PR-4b). A NAMED category ("Toppmatch"/"Stark match"/
/// "Bra match") — never a number or percent (Goodhart guard, ADR 0071/0080; CLAUDE.md §5). Parity
/// the /jobb <see cref="Grading.MatchGrade"/> labels (Grundmatch/Bra match/Stark match/Toppmatch).
/// Server-side copy: the email templates are hardcoded Swedish per the civic-utility tone (no
/// server-side i18n; the FE grade badges use next-intl, the email body does not).
/// </summary>
internal static class NotifiableMatchGradeLabels
{
    public static string ToSwedishLabel(this NotifiableMatchGrade grade) => grade switch
    {
        NotifiableMatchGrade.Good => "Bra match",
        NotifiableMatchGrade.Strong => "Stark match",
        NotifiableMatchGrade.Top => "Toppmatch",
        _ => throw new ArgumentOutOfRangeException(
            nameof(grade), grade, "Okänd NotifiableMatchGrade."),
    };
}
