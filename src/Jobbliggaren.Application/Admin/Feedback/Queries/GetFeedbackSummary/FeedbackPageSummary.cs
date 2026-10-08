namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

public sealed record FeedbackPageSummary(
    string PageKey,
    int Submissions,
    int Raters,
    int Rated1,
    int Rated2,
    int Rated3,
    int Rated4,
    int Rated5,
    decimal? Mean);
