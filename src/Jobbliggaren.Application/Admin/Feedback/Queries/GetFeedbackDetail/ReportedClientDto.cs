using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;

public sealed record ReportedClientDto(
    int? ViewportWidth,
    int? ViewportHeight,
    int? ScreenWidth,
    int? ScreenHeight,
    decimal? PixelRatio,
    ReportedTheme? Theme,
    ReportedDeviceClass? DeviceClass,
    ReportedOsFamily? OsFamily,
    ReportedBrowserFamily? BrowserFamily);
