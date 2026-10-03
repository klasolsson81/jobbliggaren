import { NextResponse, type NextRequest } from "next/server";
import { deleteSessionCookie, getSessionId } from "@/lib/auth/session";
import { LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import { env } from "@/lib/env";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";
import { isSameOriginRequest } from "@/lib/security/same-origin";

/**
 * POST /api/auth/logout — where every "Logga ut" form posts (#1956, ADR 0018 Amendment 2026-10-03).
 *
 * A route handler and not a Server Action: an action id belongs to the build that rendered the page, so
 * after a deploy a stale page's Server Action never runs (ADR 0148), and logout is the one action whose
 * not running keeps a session alive. A route handler runs on the build that answers.
 *
 * It gets none of Server Actions' Origin check, so it makes its own before reading anything.
 */
export async function POST(request: NextRequest): Promise<NextResponse> {
  if (!isSameOriginRequest(request)) {
    return new NextResponse(null, { status: 403, headers: { "Cache-Control": "no-store" } });
  }

  const sessionId = await getSessionId();
  if (sessionId) {
    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/logout`, {
        method: "POST",
        headers: { ...pickForwardedHeaders(request.headers), Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
      // Best-effort: the backend session lapses by its own TTL if this call fails.
      if (!res.ok) {
        // An event name and a status code; the session id stays out of it.
        // eslint-disable-next-line no-console
        console.error("logout.backend_call_failed", { event: "logout", status: res.status });
      }
    } catch (cause) {
      // The message, never the Error itself: a thrown Error prints its stack.
      // eslint-disable-next-line no-console
      console.error("logout.backend_call_failed", {
        event: "logout",
        cause: cause instanceof Error ? cause.message : String(cause),
      });
    }
  }

  await deleteSessionCookie();
  return new NextResponse(null, {
    status: 303,
    headers: { Location: LOGIN_ENTRY_PATH, "Cache-Control": "no-store" },
  });
}
