import "server-only";

import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult } from "@/lib/dto/_helpers";
import { emailAllowanceSchema, type EmailAllowanceDto } from "@/lib/dto/admin-email";
import { SOURCE_DEADLINE_MS } from "./admin-overview";

export const EMAIL_ALLOWANCE_PATH = "/api/v1/admin/email/allowance";

export type EmailAllowanceRead =
  | { readonly kind: "ok"; readonly data: EmailAllowanceDto }
  | { readonly kind: "unauthorized" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "error" };

/**
 * The e-post page's allowance read (#1981 PR 1, ADR 0178). It never throws: a refusal, a timeout, a status the
 * contract does not name or a body outside it is a result, which the page renders as the region's own failed state,
 * so a failed read never reaches the route's error boundary and never blanks the rest of the page (ADR 0150 D2).
 * When the provider answered is `usage.observedAt` in the body; the page needs no response header.
 */
export async function loadEmailAllowance(signal?: AbortSignal): Promise<EmailAllowanceRead> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  const deadline = AbortSignal.timeout(SOURCE_DEADLINE_MS);
  const linked = signal ? AbortSignal.any([signal, deadline]) : deadline;
  try {
    const response = await authedFetch(sessionId, EMAIL_ALLOWANCE_PATH, { signal: linked });
    const result = await responseToResult(response, emailAllowanceSchema, `GET ${EMAIL_ALLOWANCE_PATH}`);
    switch (result.kind) {
      case "ok":
      case "unauthorized":
      case "forbidden":
        return result;
      default:
        return { kind: "error" };
    }
  } catch {
    return { kind: "error" };
  }
}
