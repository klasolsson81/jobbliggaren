import { NextResponse, type NextRequest } from "next/server";
import { getNotificationFeed } from "@/lib/api/notification-feed";

/**
 * #1666 — the bell's read for its client poll (`useNotificationFeed`), which cannot call the
 * `server-only` fetcher. GET only, and it writes nothing: under the ADR 0018 amendment of
 * 2026-10-10 the no-writes-on-GET rule is what keeps a cross-site GET harmless. No user input is
 * forwarded (the query string is ignored; the feed is the session's own).
 *
 * The body is the schema's parse output (Zod strips unknown keys), so only named fields reach the
 * browser. A failure is never a 200 with an empty or invented feed — the bell would read it as
 * "nothing unread" (ADR 0120): 401 and 403 stop the poll, 429 carries `Retry-After`, everything
 * else is 502.
 */
const HEADERS = { "Cache-Control": "private, no-store" } as const;

export const dynamic = "force-dynamic";

export async function GET(request: NextRequest) {
  const result = await getNotificationFeed(request.signal);

  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data, { headers: HEADERS });
    case "unauthorized":
      return new NextResponse(null, { status: 401, headers: HEADERS });
    case "forbidden":
      return new NextResponse(null, { status: 403, headers: HEADERS });
    case "rateLimited":
      return new NextResponse(null, {
        status: 429,
        headers: { ...HEADERS, "Retry-After": String(result.retryAfterSeconds) },
      });
    default:
      return new NextResponse(null, { status: 502, headers: HEADERS });
  }
}
