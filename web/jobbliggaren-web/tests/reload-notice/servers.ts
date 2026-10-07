import { startHarness as startJobHarness } from "../job-modal/servers";
export { SESSION_COOKIE, SESSION_ID, type Harness } from "../job-modal/servers";

export const HARNESS_PORTS = { proxy: 3160, next: 3161, backend: 3162 } as const;
export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;

export function startHarness() {
  return startJobHarness(false, 0, HARNESS_PORTS);
}
