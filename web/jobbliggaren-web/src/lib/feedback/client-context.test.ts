import { describe, expect, it } from "vitest";
import { readClientContext, type DeviceView } from "./client-context";

const UA = {
  chromeWindows: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
  edgeWindows: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
  safariMac: "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15",
  safariIphone: "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1",
  chromeAndroidPhone: "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
  samsungAndroidTablet: "Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/26.0 Chrome/122.0.0.0 Safari/537.36",
  firefoxLinux: "Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0",
  operaChromeOs: "Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 OPR/115.0.0.0",
  unknown: "SomeBot/1.0",
} as const;

function view(userAgent: string, overrides: Partial<DeviceView> = {}): DeviceView {
  return {
    innerWidth: 1280,
    innerHeight: 720,
    devicePixelRatio: 1,
    screen: { width: 1920, height: 1080 },
    navigator: { userAgent, maxTouchPoints: 0 },
    ...overrides,
  };
}

describe("readClientContext", () => {
  it("reports sizes as integers and the pixel ratio to two decimals", () => {
    const context = readClientContext(view(UA.chromeWindows, { innerWidth: 1279.6, innerHeight: 719.4, devicePixelRatio: 1.3333 }));
    expect(context).toMatchObject({ viewportWidth: 1280, viewportHeight: 719, screenWidth: 1920, screenHeight: 1080, pixelRatio: 1.33 });
  });

  it("leaves out values the backend would drop", () => {
    const context = readClientContext(view(UA.chromeWindows, { innerWidth: 0, devicePixelRatio: 11, screen: { width: 30_000, height: Number.NaN } }));
    expect(context).not.toHaveProperty("viewportWidth");
    expect(context).not.toHaveProperty("pixelRatio");
    expect(context).not.toHaveProperty("screenWidth");
    expect(context).not.toHaveProperty("screenHeight");
  });

  it("never carries a theme or the raw user agent", () => {
    const context = readClientContext(view(UA.chromeWindows));
    expect(Object.keys(context).sort()).toEqual(
      ["browserFamily", "deviceClass", "osFamily", "pixelRatio", "screenHeight", "screenWidth", "viewportHeight", "viewportWidth"],
    );
    expect(JSON.stringify(context)).not.toContain("Mozilla");
  });

  it.each([
    [UA.chromeWindows, "desktop", "windows", "chrome"],
    [UA.edgeWindows, "desktop", "windows", "edge"],
    [UA.safariMac, "desktop", "macOs", "safari"],
    [UA.safariIphone, "mobile", "ios", "safari"],
    [UA.chromeAndroidPhone, "mobile", "android", "chrome"],
    [UA.samsungAndroidTablet, "tablet", "android", "samsungInternet"],
    [UA.firefoxLinux, "desktop", "linux", "firefox"],
    [UA.operaChromeOs, "desktop", "chromeOs", "opera"],
    [UA.unknown, "desktop", "other", "other"],
  ] as const)("classifies %s", (userAgent, deviceClass, osFamily, browserFamily) => {
    expect(readClientContext(view(userAgent))).toMatchObject({ deviceClass, osFamily, browserFamily });
  });

  it("reads an iPad that reports itself as a Mac as an iOS tablet", () => {
    expect(readClientContext(view(UA.safariMac, { navigator: { userAgent: UA.safariMac, maxTouchPoints: 5 } }))).toMatchObject({
      deviceClass: "tablet",
      osFamily: "ios",
    });
  });
});
