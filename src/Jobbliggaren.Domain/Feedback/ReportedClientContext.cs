using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.Feedback;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportedTheme
{
    Light,
    Dark,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportedDeviceClass
{
    Mobile,
    Tablet,
    Desktop,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportedOsFamily
{
    Windows,
    MacOs,
    Ios,
    Android,
    Linux,
    ChromeOs,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportedBrowserFamily
{
    Chrome,
    Edge,
    Firefox,
    Safari,
    SamsungInternet,
    Opera,
    Other,
}

/// <summary>
/// What the browser reported about itself when the feedback was sent. Every field is a closed set
/// or a bounded number: no URL, query string or raw user agent is ever collected (#1979). The admin
/// shows these as reported, not verified.
/// <para>
/// A value outside its range is DROPPED, never refused: these are diagnostics, and a strange
/// browser must not cost the user their feedback.
/// </para>
/// </summary>
public sealed record ReportedClientContext
{
    /// <summary>Upper bound for any CSS-pixel size; well past a multi-monitor desktop.</summary>
    public const int MaxCssPixels = 20_000;

    public const decimal MinPixelRatio = 0.25m;
    public const decimal MaxPixelRatio = 10m;

    public static ReportedClientContext Empty => new();

    public int? ViewportWidth { get; private init; }
    public int? ViewportHeight { get; private init; }
    public int? ScreenWidth { get; private init; }
    public int? ScreenHeight { get; private init; }
    public decimal? PixelRatio { get; private init; }
    public ReportedTheme? Theme { get; private init; }
    public ReportedDeviceClass? DeviceClass { get; private init; }
    public ReportedOsFamily? OsFamily { get; private init; }
    public ReportedBrowserFamily? BrowserFamily { get; private init; }

    private ReportedClientContext() { }

    public static ReportedClientContext FromReported(
        int? viewportWidth,
        int? viewportHeight,
        int? screenWidth,
        int? screenHeight,
        decimal? pixelRatio,
        ReportedTheme? theme,
        ReportedDeviceClass? deviceClass,
        ReportedOsFamily? osFamily,
        ReportedBrowserFamily? browserFamily) => new()
        {
            ViewportWidth = Size(viewportWidth),
            ViewportHeight = Size(viewportHeight),
            ScreenWidth = Size(screenWidth),
            ScreenHeight = Size(screenHeight),
            PixelRatio = pixelRatio is >= MinPixelRatio and <= MaxPixelRatio
            ? decimal.Round(pixelRatio.Value, 2, MidpointRounding.AwayFromZero)
            : null,
            Theme = Defined(theme),
            DeviceClass = Defined(deviceClass),
            OsFamily = Defined(osFamily),
            BrowserFamily = Defined(browserFamily),
        };

    private static int? Size(int? pixels) => pixels is > 0 and <= MaxCssPixels ? pixels : null;

    // An integer cast to an enum need not be a member; such a value is dropped like any other.
    private static T? Defined<T>(T? value) where T : struct, Enum =>
        value is { } v && Enum.IsDefined(v) ? v : null;
}
