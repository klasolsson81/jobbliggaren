using System.Collections.Frozen;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Infrastructure.Email;

internal static partial class EmailTemplates
{
    /// <summary>
    /// #1979 — the notice to the operator that feedback was saved. It carries the page, the rating,
    /// the time and a link into the signed-in admin; the text and the screenshot are only ever read
    /// there, so nothing a user wrote leaves for the mail provider.
    /// </summary>
    public static EmailContent FeedbackReceivedNotification(
        string baseUrl, FeedbackReceivedNotificationEmail content)
    {
        var page = FeedbackPageLabels[content.Page];
        var rating = content.Rating is { } stars ? $"{stars} av 5" : "Inget betyg";
        var at = SwedishTime(content.SubmittedAt);
        var link = $"{baseUrl.TrimEnd('/')}/admin/feedback?id={content.FeedbackId:D}";
        var subject = $"Ny feedback: {page}";

        return new EmailContent(
            Subject: subject,
            PlainTextBody: $"""
                Ny feedback har sparats.

                Sida: {page}
                Betyg: {rating}
                Tid: {at}

                Läs den i admin (inloggning krävs):
                {link}

                Vänliga hälsningar,
                Jobbliggaren
                """,
            HtmlBody: EmailHtml.Document(
                title: subject,
                preheader: $"{page}, {rating}.",
                body: EmailHtml.P("Ny feedback har sparats.")
                    + EmailHtml.List([$"Sida: {page}", $"Betyg: {rating}", $"Tid: {at}"])
                    + EmailHtml.Button(link, "Läs den i admin")
                    + EmailHtml.P("Inloggning krävs.")
                    + EmailHtml.SignOff()));
    }

    /// <summary>
    /// The Swedish name of each page as the app titles it. <c>EmailTemplatesFeedbackTests</c> pins
    /// that every <see cref="FeedbackPage"/> has one, so a new key cannot mail a bare identifier.
    /// </summary>
    internal static readonly FrozenDictionary<FeedbackPage, string> FeedbackPageLabels =
        new Dictionary<FeedbackPage, string>
        {
            [FeedbackPage.Overview] = "Översikt",
            [FeedbackPage.Jobs] = "Jobb",
            [FeedbackPage.JobAd] = "Jobbannons",
            [FeedbackPage.Matches] = "Matchningar",
            [FeedbackPage.SavedAds] = "Sparade annonser",
            [FeedbackPage.SavedSearches] = "Sökningar",
            [FeedbackPage.Applications] = "Ansökningar",
            [FeedbackPage.Application] = "Ansökan",
            [FeedbackPage.NewApplication] = "Ny ansökan",
            [FeedbackPage.Statistics] = "Statistik",
            [FeedbackPage.ActivityReport] = "Aktivitetsrapport-hjälp",
            [FeedbackPage.FollowedCompanies] = "Bevakade företag",
            [FeedbackPage.CompanySearch] = "Sök företag",
            [FeedbackPage.IndustryWatches] = "Branschbevakningar",
            [FeedbackPage.ApplicationHistory] = "Ansökningshistorik",
            [FeedbackPage.Cv] = "CV",
            [FeedbackPage.CvImport] = "Importera CV",
            [FeedbackPage.CvReview] = "CV-granskning",
            [FeedbackPage.MyPages] = "Mina sidor",
            [FeedbackPage.General] = "Allmän feedback",
        }.ToFrozenDictionary();
}
