import { startHarness as startJobHarness } from "../job-modal/servers";
export { SESSION_COOKIE, SESSION_ID, SYNTHETIC_CV, type Harness } from "../job-modal/servers";

export const HARNESS_PORTS = { proxy: 3150, next: 3151, backend: 3152 } as const;
export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;

export function startHarness(informationFlows = true, applicationCopies = 0) {
  return startJobHarness(informationFlows, applicationCopies, HARNESS_PORTS);
}
