import type { EdgeLogVerdict, EdgeLogVerdicts } from "@/test/edge-log-pin";
import { EDGE_LOG_VERDICT as JOBB } from "@/lib/job-ads/edge-log-verdicts";

/**
 * The edge-log verdict for each query key the Strict-cookie bounce's hop puts on a request line (#2094). The hop is
 * the return path of a protected or admin page, or the open junior list's validated address (#2284), with
 * {@link BOUNCE_MARKER} added, and Caddy's default logger writes the whole request line on a 5xx. The junior list's
 * keys carry the values `/jobb` carries under the same names, so they take `/jobb`'s verdicts.
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
  municipality: asOnJobb("municipality"),
  sortBy: asOnJobb("sortBy"),
  page: asOnJobb("page"),
};

/** `/jobb`'s verdict for a key the junior list carries under the same name; it fails loud if `/jobb` stops judging it. */
function asOnJobb(key: string): EdgeLogVerdict {
  const verdict = JOBB[key];
  if (verdict === undefined) throw new Error(`/jobb's edge-log verdicts judge no "${key}"`);
  return verdict;
}
