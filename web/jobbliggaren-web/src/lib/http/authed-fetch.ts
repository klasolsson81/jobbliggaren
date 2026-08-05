import "server-only";
import { env } from "@/lib/env";
import { forwardedHeaders } from "./forwarded-headers";

/**
 * Bearer + JSON content-type header pair for authenticated backend calls. Kept
 * private to this module: it was previously copy-pasted verbatim in every
 * `"use server"` action file (#612). The session id is a server-only secret and
 * never reaches the client.
 */
function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * Server-only transport primitive for authenticated backend calls from server
 * actions (#612). Prefixes `env.BACKEND_URL`, injects the Bearer + JSON headers,
 * and forces `cache: "no-store"`, then returns the RAW {@link Response}.
 *
 * It deliberately owns ONLY the transport invariants — not the auth-null guard,
 * not id validation, not error mapping, not revalidation, not the terminal
 * (return vs `redirect`). The caller keeps those explicit (DAMP): the auth-null
 * branch carries a per-action i18n key, and error mapping stays with
 * {@link ../actions/_action-error.mapActionError}.
 *
 * SECURITY (TD-10 / OWASP ASVS V8.2): this NEVER reads the response body. ASP.NET
 * ProblemDetails bodies can carry stacktraces / SQL, so error text is derived
 * from the status code only (via `mapActionError`); success-body reads (e.g. a
 * created id) are the caller's, via `parseResponse`. The `Omit<RequestInit,
 * "cache" | "headers">` on `init` makes the no-store + Bearer invariants
 * un-overridable at the call site.
 *
 * @param sessionId a non-null session id (caller checked `getSessionId()`).
 * @param path a backend path beginning with `/` — already `encodeURIComponent`-
 *   escaped for any interpolated ids (SSRF/path-injection is the caller's guard).
 * @param init request init minus `cache`/`headers` (method, body, signal, ...).
 */
export async function authedFetch(
  sessionId: string,
  path: string,
  init?: Omit<RequestInit, "cache" | "headers">,
): Promise<Response> {
  return fetch(`${env.BACKEND_URL}${path}`, {
    ...init,
    // The forwarded pair first, the auth pair second: spreading auth last makes the Bearer
    // and content-type invariants un-overridable by a relayed header, which is the same
    // property `Omit<..., "headers">` gives the call site (#1202).
    headers: { ...(await forwardedHeaders()), ...authHeaders(sessionId) },
    cache: "no-store",
  });
}
