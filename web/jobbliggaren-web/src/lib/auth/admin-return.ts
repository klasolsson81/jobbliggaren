import { FEEDBACK_ROUTE, isFeedbackId } from "@/lib/admin/feedback";
import { LOGIN_ENTRY_PATH } from "./login-paths";
import { safeRedirectPath } from "./safe-redirect";

/**
 * Where a signed-out administrator returns after logging in (#1979). The `(admin)` layout has no URL of its
 * own to read, so the proxy writes the request's admin path into this header and the layout builds the login
 * link from it. The proxy overwrites whatever a client sent, and the layout validates the value again, so a
 * forged header can only ever produce the plain login page.
 *
 * Runtime-agnostic, like `protected-routes.ts`: the proxy and the layout both import it.
 */
export const ADMIN_RETURN_HEADER = "x-jobbliggaren-admin-return";

const ADMIN_PREFIX = "/admin";

// Only parsed against; the layout compares origins and keeps path and query.
const PARSE_BASE = new URL("https://jobbliggaren.invalid");

export function isAdminPath(pathname: string): boolean {
  return pathname === ADMIN_PREFIX || pathname.startsWith(`${ADMIN_PREFIX}/`);
}

/**
 * The path to come back to: the admin pathname, and on `/admin/feedback` the one submission `id` when the
 * query carries exactly one GUID. Every other query key is dropped, so a filter never rides along into `next`.
 * Null outside `/admin`.
 */
export function adminReturnPath(url: Pick<URL, "pathname" | "searchParams">): string | null {
  if (!isAdminPath(url.pathname)) return null;
  if (url.pathname === FEEDBACK_ROUTE) {
    const ids = url.searchParams.getAll("id");
    if (ids.length === 1 && isFeedbackId(ids[0])) {
      return `${url.pathname}?${new URLSearchParams({ id: ids[0].toLowerCase() })}`;
    }
  }
  return url.pathname;
}

/**
 * The layout's login redirect. A value the proxy did not write in this exact form gives the plain login page,
 * as the layout did before the header existed.
 */
export function adminLoginHref(headerValue: string | null): string {
  if (!headerValue || !headerValue.startsWith("/")) return LOGIN_ENTRY_PATH;

  let parsed: URL;
  try {
    parsed = new URL(headerValue, PARSE_BASE);
  } catch {
    return LOGIN_ENTRY_PATH;
  }
  if (parsed.origin !== PARSE_BASE.origin) return LOGIN_ENTRY_PATH;

  const target = adminReturnPath(parsed);
  if (target === null || target !== headerValue || safeRedirectPath(target) !== target) {
    return LOGIN_ENTRY_PATH;
  }
  return `${LOGIN_ENTRY_PATH}?${new URLSearchParams({ next: target })}`;
}
