import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for the query key the login flow carries its return path in (#1979).
 *
 * Two routes receive it in their request line: `/logga-in`, written by the proxy for a protected path, by
 * the `(admin)` layout through `adminLoginHref`, by the reauthentication links and by the feedback form's
 * signed-out link; and `/api/auth/oauth/{provider}/start`, written by `externalLoginStartHref`. Caddy's
 * default logger writes the whole request line on a 5xx, so the value reaches a stored log post unless the
 * edge removes it.
 */
export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  next: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "A path on this site, and the proxy writes any protected pathname into it, so it can name a " +
      "user's own application or CV by id. The admin layout writes /admin/feedback's submission id " +
      "into it, and that id identifies the reporter indirectly (Art. 4(1)), the same ground on which " +
      "/admin/feedback's own id is filtered.",
  },
};
