using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

// The context is diagnostics the browser reports. It must never cost the user their feedback, so a
// value outside its range is dropped rather than refused.
public class ReportedClientContextTests
{
    [Fact]
    public void FromReported_ValuesInRange_AreKept()
    {
        var context = ReportedClientContext.FromReported(
            viewportWidth: 390, viewportHeight: 844, screenWidth: 390, screenHeight: 844,
            pixelRatio: 3m, theme: ReportedTheme.Light, deviceClass: ReportedDeviceClass.Mobile,
            osFamily: ReportedOsFamily.Ios, browserFamily: ReportedBrowserFamily.Safari);

        context.ViewportWidth.ShouldBe(390);
        context.ViewportHeight.ShouldBe(844);
        context.ScreenWidth.ShouldBe(390);
        context.ScreenHeight.ShouldBe(844);
        context.PixelRatio.ShouldBe(3m);
        context.Theme.ShouldBe(ReportedTheme.Light);
        context.DeviceClass.ShouldBe(ReportedDeviceClass.Mobile);
        context.OsFamily.ShouldBe(ReportedOsFamily.Ios);
        context.BrowserFamily.ShouldBe(ReportedBrowserFamily.Safari);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ReportedClientContext.MaxCssPixels + 1)]
    public void FromReported_SizeOutOfRange_IsDropped(int size)
    {
        var context = ReportedClientContext.FromReported(size, size, size, size, 1m, null, null, null, null);

        context.ViewportWidth.ShouldBeNull();
        context.ViewportHeight.ShouldBeNull();
        context.ScreenWidth.ShouldBeNull();
        context.ScreenHeight.ShouldBeNull();
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("10.5")]
    public void FromReported_PixelRatioOutOfRange_IsDropped(string ratio)
        => ReportedClientContext.FromReported(null, null, null, null, decimal.Parse(ratio,
                System.Globalization.CultureInfo.InvariantCulture), null, null, null, null)
            .PixelRatio.ShouldBeNull();

    [Fact]
    public void FromReported_PixelRatio_IsRoundedToTwoDecimals()
        => ReportedClientContext.FromReported(null, null, null, null, 1.33333m, null, null, null, null)
            .PixelRatio.ShouldBe(1.33m);

    [Fact]
    public void Empty_ReportsNothing()
    {
        var context = ReportedClientContext.Empty;

        context.ViewportWidth.ShouldBeNull();
        context.PixelRatio.ShouldBeNull();
        context.Theme.ShouldBeNull();
        context.OsFamily.ShouldBeNull();
    }
}
