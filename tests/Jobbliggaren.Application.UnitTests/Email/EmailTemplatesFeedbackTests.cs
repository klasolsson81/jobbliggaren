using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

public class EmailTemplatesFeedbackTests
{
    private static readonly Guid FeedbackId = Guid.Parse("7d1c2a3b-0000-4000-8000-000000001979");

    [Fact]
    public void EveryFeedbackPage_HasASwedishLabel()
        => FeedbackPage.List.Where(p => !EmailTemplates.FeedbackPageLabels.ContainsKey(p))
            .Select(p => p.Name)
            .ShouldBeEmpty();

    [Fact]
    public void FeedbackReceivedNotification_WithARating_StatesItOutOfFive()
    {
        var mail = EmailTemplates.FeedbackReceivedNotification("https://jobbliggaren.se/",
            new FeedbackReceivedNotificationEmail(FeedbackPage.Matches, 2, new DateTimeOffset(2026, 10, 8, 12, 30, 0,
                TimeSpan.Zero), FeedbackId));

        mail.Subject.ShouldBe("Ny feedback: Mina matchningar");
        mail.PlainTextBody.ShouldContain("Betyg: 2 av 5");
        mail.PlainTextBody.ShouldContain("Tid: 2026-10-08 kl 14:30");
        mail.PlainTextBody.ShouldContain($"https://jobbliggaren.se/admin/feedback?id={FeedbackId:D}");
        mail.HtmlBody.ShouldContain("Betyg: 2 av 5");
    }

    [Fact]
    public void FeedbackReceivedNotification_WithoutARating_SaysSo()
        => EmailTemplates.FeedbackReceivedNotification("https://jobbliggaren.se",
                new FeedbackReceivedNotificationEmail(FeedbackPage.Cv, null, DateTimeOffset.UnixEpoch, FeedbackId))
            .PlainTextBody.ShouldContain("Betyg: Inget betyg");

    private static EmailTemplates.EmailContent Reply(string text, FeedbackPage? page = null) =>
        EmailTemplates.FeedbackReply(new FeedbackReplyEmail(
            page ?? FeedbackPage.Matches,
            new DateTimeOffset(2026, 10, 8, 22, 30, 0, TimeSpan.Zero),
            FeedbackReplyText.Create(text).Value));

    [Fact]
    public void FeedbackReply_CarriesTheTextAndSaysWhichFeedbackItAnswers()
    {
        var mail = Reply("Tack för din feedback.");

        mail.Subject.ShouldBe("Svar på din feedback");
        mail.PlainTextBody.ShouldContain("Tack för din feedback.");
        // 22:30 UTC on 8 October is already 9 October in Sweden.
        mail.PlainTextBody.ShouldContain("feedbacken du skickade 2026-10-09 om sidan Mina matchningar.");
        mail.PlainTextBody.ShouldContain(
            "Vill du skriva till oss kan du svara på det här mejlet eller mejla kontakt@jobbliggaren.se.");
        mail.HtmlBody.ShouldContain("Tack för din feedback.");
        mail.HtmlBody.ShouldContain("om sidan Mina matchningar.");
    }

    [Fact]
    public void FeedbackReply_ToGeneralFeedback_NamesNoPage()
    {
        var mail = Reply("Tack.", FeedbackPage.General);

        mail.PlainTextBody.ShouldContain("feedbacken du skickade 2026-10-09. Vill du");
        mail.PlainTextBody.ShouldNotContain("om sidan");
    }

    [Fact]
    public void FeedbackReply_TypedMarkupStaysText()
    {
        var mail = Reply("<script>alert(1)</script> <a href=\"https://evil.example\">klicka</a>");

        mail.HtmlBody.ShouldNotContain("<script>");
        mail.HtmlBody.ShouldNotContain("<a href=\"https://evil.example\"");
        mail.HtmlBody.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        mail.HtmlBody.ShouldContain("&lt;a href=&quot;https://evil.example&quot;&gt;");
    }

    [Fact]
    public void FeedbackReply_BlankLinesMakeParagraphsAndLinesMakeBreaks()
    {
        var mail = Reply("Hej Anna,\n\nrad ett\nrad två");

        mail.HtmlBody.ShouldContain(">Hej Anna,</p>");
        mail.HtmlBody.ShouldContain(">rad ett<br>rad två</p>");
    }

    [Fact]
    public void FeedbackReply_ALineOfSpacesSeparatesParagraphsLikeABlankLine()
    {
        var mail = Reply("Första stycket.\n   \nAndra stycket.");

        mail.HtmlBody.ShouldContain(">Första stycket.</p>");
        mail.HtmlBody.ShouldContain(">Andra stycket.</p>");
        mail.HtmlBody.ShouldNotContain("<br><br>");
    }
}
