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

        mail.Subject.ShouldBe("Ny feedback: Matchningar");
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
}
