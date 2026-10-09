/**
 * The device context a feedback submission may carry (#1979 PR3, ADR 0156 D2). It is read from the
 * user's device, which LEK 9 kap. 28 § allows only with consent here (Klas, 2026-10-09: an unticked opt-in
 * box), so the form calls this at Send and only when the box is ticked; nothing else in the app reads it.
 *
 * The theme is deliberately absent: the product is light-only, and the stored theme choice is browser
 * storage the cookie policy says never leaves the device.
 *
 * The names are the backend's wire names. Values outside the backend's bounds are left out rather than
 * sent, and the numbers are integers, because a fractional one fails the whole submission's JSON read.
 */
export type ClientContext = {
  readonly viewportWidth?: number;
  readonly viewportHeight?: number;
  readonly screenWidth?: number;
  readonly screenHeight?: number;
  readonly pixelRatio?: number;
  readonly deviceClass?: "mobile" | "tablet" | "desktop";
  readonly osFamily?: "windows" | "macOs" | "ios" | "android" | "linux" | "chromeOs" | "other";
  readonly browserFamily?: "chrome" | "edge" | "firefox" | "safari" | "samsungInternet" | "opera" | "other";
};

/** The parts of `window` the reader uses, so a test can hand it a plain object. */
export type DeviceView = {
  readonly innerWidth: number;
  readonly innerHeight: number;
  readonly devicePixelRatio: number;
  readonly screen: { readonly width: number; readonly height: number };
  readonly navigator: {
    readonly userAgent: string;
    readonly maxTouchPoints?: number;
    readonly userAgentData?: { readonly mobile?: boolean };
  };
};

function dimension(value: number): number | undefined {
  const rounded = Math.round(value);
  return Number.isFinite(rounded) && rounded >= 1 && rounded <= 20_000 ? rounded : undefined;
}

function pixelRatio(value: number): number | undefined {
  const rounded = Math.round(value * 100) / 100;
  return Number.isFinite(rounded) && rounded >= 0.25 && rounded <= 10 ? rounded : undefined;
}

function isIpadAsMac(view: DeviceView): boolean {
  return /Macintosh/.test(view.navigator.userAgent) && (view.navigator.maxTouchPoints ?? 0) > 1;
}

function osFamilyOf(view: DeviceView): NonNullable<ClientContext["osFamily"]> {
  const ua = view.navigator.userAgent;
  if (/Windows/.test(ua)) return "windows";
  if (/iPhone|iPad|iPod/.test(ua) || isIpadAsMac(view)) return "ios";
  if (/Android/.test(ua)) return "android";
  if (/CrOS/.test(ua)) return "chromeOs";
  if (/Macintosh|Mac OS X/.test(ua)) return "macOs";
  if (/Linux/.test(ua)) return "linux";
  return "other";
}

function browserFamilyOf(ua: string): NonNullable<ClientContext["browserFamily"]> {
  // Order matters: Samsung Internet, Opera and Edge all also name Chrome, and Chrome names Safari.
  if (/SamsungBrowser\//.test(ua)) return "samsungInternet";
  if (/OPR\/|Opera/.test(ua)) return "opera";
  if (/Edg(e|A|iOS)?\//.test(ua)) return "edge";
  if (/Firefox\/|FxiOS\//.test(ua)) return "firefox";
  if (/Chrome\/|CriOS\//.test(ua)) return "chrome";
  if (/Safari\//.test(ua)) return "safari";
  return "other";
}

function deviceClassOf(view: DeviceView): NonNullable<ClientContext["deviceClass"]> {
  const ua = view.navigator.userAgent;
  if (/iPad/.test(ua) || isIpadAsMac(view) || (/Android/.test(ua) && !/Mobile/.test(ua))) return "tablet";
  if (view.navigator.userAgentData?.mobile === true || /Mobi|iPhone|iPod/.test(ua)) return "mobile";
  return "desktop";
}

export function readClientContext(view: DeviceView): ClientContext {
  const context: Record<string, string | number> = {};
  const put = (key: keyof ClientContext, value: string | number | undefined) => {
    if (value !== undefined) context[key] = value;
  };
  put("viewportWidth", dimension(view.innerWidth));
  put("viewportHeight", dimension(view.innerHeight));
  put("screenWidth", dimension(view.screen.width));
  put("screenHeight", dimension(view.screen.height));
  put("pixelRatio", pixelRatio(view.devicePixelRatio));
  put("deviceClass", deviceClassOf(view));
  put("osFamily", osFamilyOf(view));
  put("browserFamily", browserFamilyOf(view.navigator.userAgent));
  return context as ClientContext;
}
