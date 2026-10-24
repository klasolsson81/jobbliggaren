/**
 * The two share controls that need a browser (ADR 0183): the visitor's own share sheet, where the browser has one,
 * and copying the link. Each does one thing and reports its own outcome; neither falls back to the other. Tested
 * against a navigator-shaped port, without a browser.
 *
 * Feature detection only, never the user agent.
 */

export interface JobAdShareData {
  readonly title: string;
  readonly url: string;
}

export interface SharePort {
  readonly share?: (data: JobAdShareData) => Promise<void>;
  readonly canShare?: (data: JobAdShareData) => boolean;
  readonly clipboard?: { writeText(text: string): Promise<void> };
}

/** Whether the browser has a share sheet that takes this data. */
export function hasNativeShare(port: SharePort, data: JobAdShareData): boolean {
  if (typeof port.share !== "function") return false;
  return typeof port.canShare !== "function" || port.canShare(data);
}

export async function shareNatively(port: SharePort, data: JobAdShareData): Promise<"shared" | "cancelled" | "failed"> {
  if (port.share === undefined || !hasNativeShare(port, data)) return "failed";
  try {
    await port.share({ title: data.title, url: data.url });
    return "shared";
  } catch (error) {
    return isAbort(error) ? "cancelled" : "failed";
  }
}

export async function copyLink(port: SharePort, url: string): Promise<"copied" | "failed"> {
  if (typeof port.clipboard?.writeText !== "function") return "failed";
  try {
    await port.clipboard.writeText(url);
    return "copied";
  } catch {
    return "failed";
  }
}

function isAbort(error: unknown): boolean {
  return typeof error === "object" && error !== null && "name" in error && error.name === "AbortError";
}
