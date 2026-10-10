import { adminReturnPath } from "./admin-return";
import { isProtectedPath } from "./protected-routes";

/**
 * Where a signed-out request on a gated page returns to, which is what the login `next` carries: the admin
 * return path on `/admin`, the pathname on a protected path, and null anywhere else (#2094). The query is
 * dropped, as the proxy's login redirect drops it, so no search value travels on from here.
 *
 * Runtime-agnostic: the proxy imports it.
 */
export function returnPathFor(url: Pick<URL, "pathname" | "searchParams">): string | null {
  return adminReturnPath(url) ?? (isProtectedPath(url.pathname) ? url.pathname : null);
}
