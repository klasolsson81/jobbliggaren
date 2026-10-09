import { startHarness as startJobHarness } from "../job-modal/servers";
export { SESSION_COOKIE, SESSION_ID, type FeedbackAnswer, type Harness } from "../job-modal/servers";

export const HARNESS_PORTS = { proxy: 3170, next: 3171, backend: 3172 } as const;
export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;

/** The release commit the app is started with, so the consent evidence (CTO M1) can be read end to end. */
export const HARNESS_APP_VERSION = "0123456789abcdef0123456789abcdef01234567";

export function startHarness() {
  return startJobHarness(false, 0, HARNESS_PORTS);
}
