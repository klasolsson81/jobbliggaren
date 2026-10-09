import { NextResponse } from "next/server";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { isSameOriginRequest } from "@/lib/security/same-origin";
import { isFeedbackId, MAX_FEEDBACK_SCREENSHOT_BYTES } from "@/lib/admin/feedback";
import { readBounded } from "@/lib/http/read-bounded";

const HEADERS = { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" };
const PNG_SIGNATURE = [137, 80, 78, 71, 13, 10, 26, 10];

function refuse(status: number) {
  return NextResponse.json({ error: "screenshot_unavailable" }, { status, headers: HEADERS });
}

export async function POST(request: Request) {
  if (!isSameOriginRequest(request)) return refuse(403);
  if (request.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase() !== "application/json") {
    return refuse(415);
  }
  const session = await getSessionId();
  if (session === null) return refuse(401);

  try {
    const input = await readBounded(request.body, 1024);
    if (input === null) return refuse(413);
    let payload: unknown;
    try {
      payload = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(input));
    } catch {
      return refuse(400);
    }
    if (typeof payload !== "object" || payload === null || !("id" in payload) || !isFeedbackId(payload.id)) {
      return refuse(400);
    }
    const response = await authedFetch(session, `/api/v1/admin/feedback/${encodeURIComponent(payload.id)}/screenshot`, {
      signal: request.signal,
    });
    if (!response.ok) {
      await response.body?.cancel();
      return refuse([401, 403, 404, 429].includes(response.status) ? response.status : 502);
    }
    if (response.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase() !== "image/png") {
      await response.body?.cancel();
      return refuse(502);
    }
    const declaredLength = response.headers.get("content-length");
    if (declaredLength !== null && Number(declaredLength) > MAX_FEEDBACK_SCREENSHOT_BYTES) {
      await response.body?.cancel();
      return refuse(502);
    }
    const image = await readBounded(response.body, MAX_FEEDBACK_SCREENSHOT_BYTES);
    if (image === null || !PNG_SIGNATURE.every((byte, index) => image[index] === byte)) return refuse(502);
    return new NextResponse(new Uint8Array(image).buffer, {
      headers: { ...HEADERS, "Content-Type": "image/png" },
    });
  } catch {
    return refuse(502);
  }
}

function methodNotAllowed() {
  return NextResponse.json({ error: "method_not_allowed" }, {
    status: 405, headers: { ...HEADERS, Allow: "POST" },
  });
}

export const GET = methodNotAllowed;
export const PUT = methodNotAllowed;
export const PATCH = methodNotAllowed;
export const DELETE = methodNotAllowed;
export const OPTIONS = methodNotAllowed;
export function HEAD() {
  return new NextResponse(null, { status: 405, headers: { ...HEADERS, Allow: "POST" } });
}
