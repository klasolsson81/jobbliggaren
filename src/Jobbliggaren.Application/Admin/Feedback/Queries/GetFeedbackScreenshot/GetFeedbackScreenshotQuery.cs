using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackScreenshot;

public sealed record GetFeedbackScreenshotQuery(Guid Id) : IQuery<FeedbackScreenshotDto?>, IAdminRequest;

public sealed record FeedbackScreenshotDto([property: JsonIgnore] byte[] Content)
{
    public override string ToString() => $"FeedbackScreenshotDto({Content.Length} bytes)";
}
