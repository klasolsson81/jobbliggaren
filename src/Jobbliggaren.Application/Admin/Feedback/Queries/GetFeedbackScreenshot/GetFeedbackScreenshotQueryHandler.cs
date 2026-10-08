using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackScreenshot;

public sealed class GetFeedbackScreenshotQueryHandler(IAppDbContext db)
    : IQueryHandler<GetFeedbackScreenshotQuery, FeedbackScreenshotDto?>
{
    public async ValueTask<FeedbackScreenshotDto?> Handle(
        GetFeedbackScreenshotQuery query, CancellationToken cancellationToken)
    {
        var id = new FeedbackSubmissionId(query.Id);
        return await (
            from screenshot in db.FeedbackScreenshots.AsNoTracking()
            join submission in db.FeedbackSubmissions.AsNoTracking() on screenshot.SubmissionId equals submission.Id
            where submission.Id == id
            select new FeedbackScreenshotDto(EF.Property<byte[]>(screenshot, "_content")))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
