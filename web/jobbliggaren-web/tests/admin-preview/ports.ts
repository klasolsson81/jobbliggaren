import type { HarnessPorts } from "../admin/servers";

/** The preview harness's own ports, apart from the admin harness's 3120–3122. */
export const PREVIEW_PORTS: HarnessPorts = { proxy: 3123, next: 3124, backend: 3125 };
