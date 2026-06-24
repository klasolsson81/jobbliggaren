using Jobbliggaren.Application.Matching.Notifications;
using Jobbliggaren.Domain.Matching;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Notifications;

/// <summary>
/// ADR 0080 Vag 4 PR-4b — pins the Swedish display LABELS the notification email body shows for each
/// <see cref="NotifiableMatchGrade"/>. These are a NAMED category, never a number/percent (Goodhart
/// guard, ADR 0071/0080; CLAUDE.md §5) — the test exists so a label change is a deliberate, reviewed
/// edit (the email copy is server-side hardcoded Swedish; the FE badges use next-intl, the email does
/// not). Parity the /jobb grade labels (Grundmatch/Bra match/Stark match/Toppmatch).
/// </summary>
public class NotifiableMatchGradeLabelsTests
{
    // Good = the in-app-only rung ("Bra match") — surfaced in the count, never emailed, but the
    // label still maps (it is reused wherever a notifiable grade is rendered).
    [Fact]
    public void ToSwedishLabel_ReturnsBraMatch_ForGood() =>
        NotifiableMatchGrade.Good.ToSwedishLabel().ShouldBe("Bra match");

    // Strong = the digest rung — every digest item renders "Stark match" (the digest query filters
    // to Strong, so this is the label the cadence email actually shows).
    [Fact]
    public void ToSwedishLabel_ReturnsStarkMatch_ForStrong() =>
        NotifiableMatchGrade.Strong.ToSwedishLabel().ShouldBe("Stark match");

    // Top = the direct-dispatch rung — the Top-direct email item renders "Toppmatch".
    [Fact]
    public void ToSwedishLabel_ReturnsToppmatch_ForTop() =>
        NotifiableMatchGrade.Top.ToSwedishLabel().ShouldBe("Toppmatch");
}
