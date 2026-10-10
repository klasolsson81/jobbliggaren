import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for each query key the Strict-cookie bounce's hop puts on a request line (#2094). The hop is
 * the return path of a protected or admin page with {@link BOUNCE_MARKER} added, and Caddy's default logger writes
 * the whole request line on a 5xx.
 */
export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  id: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "/admin/feedback's submission id, carried over from the request the hop answers; it identifies the " +
      "reporter indirectly (Art. 4(1)), the ground on which that page's own id is filtered.",
  },
  vidare: {
    verdict: "kept",
    reason: "A constant 1 that marks the hop. It names no person, page or value.",
  },
};
