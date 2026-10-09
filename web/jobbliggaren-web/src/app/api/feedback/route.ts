import { NextResponse } from "next/server";
import { getSessionId } from "@/lib/auth/session";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import {
  feedbackSubmissionPayloadSchema,
  feedbackSubmittedSchema,
  type FeedbackSubmitOutcome,
} from "@/lib/dto/feedback";
import { env } from "@/lib/env";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";
import { readProblemTitle } from "@/lib/http/problem";
import { readBounded } from "@/lib/http/read-bounded";
import { isSameOriginRequest } from "@/lib/security/same-origin";

/**
 * BFF for a feedback submission (#1979 PR3, ADR 0156 D2). The browser posts one multipart body, a JSON
 * `payload` and an optional `screenshot`; this route checks it, stamps the app version, and posts it on
 * to `POST /api/v1/me/feedback` as a new multipart body.
 *
 * A route handler and not a Server Action for two reasons (BUILD.md §10.2): a Server Action body is
 * capped at 1 MB, and after a deploy a page's stale Server Action never runs (ADR 0148), which would
 * throw away a comment and an image the user has just prepared. Unlike the CV import this route reads
 * the body into memory instead of streaming it, because it rewrites the payload; the read is bounded.
 *
 * The browser gets one of a closed set of outcomes. The backend's body never travels: only the code in
 * its ProblemDetails title is compared, and the payload is never logged.
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const MAX_PAYLOAD_BYTES = 64 * 1024;
const MAX_SCREENSHOT_BYTES = 5 * 1024 * 1024;
// The two parts plus multipart framing: boundaries and part headers.
const MAX_BODY_BYTES = MAX_SCREENSHOT_BYTES + MAX_PAYLOAD_BYTES + 16 * 1024;
const BACKEND_TIMEOUT_MS = 15_000;
// Each request buffers up to MAX_BODY_BYTES; the cap keeps a burst from filling the container's memory.
const MAX_IN_FLIGHT = 4;
const HEADERS = { "Cache-Control": "no-store" };

let inFlight = 0;

function answer(outcome: FeedbackSubmitOutcome, status: number, headers: Record<string, string> = {}) {
  return NextResponse.json(outcome, { status, headers: { ...HEADERS, ...headers } });
}

const invalid = (status = 400) => answer({ outcome: "refused", reason: "invalid" }, status);

/** PNG, JPEG or WebP by their first bytes: the only formats the backend decodes. */
function isAcceptedImage(bytes: Uint8Array): boolean {
  const startsWith = (signature: readonly number[], offset = 0) =>
    signature.every((byte, index) => bytes[offset + index] === byte);
  return (
    startsWith([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]) ||
    startsWith([0xff, 0xd8, 0xff]) ||
    (startsWith([0x52, 0x49, 0x46, 0x46]) && startsWith([0x57, 0x45, 0x42, 0x50], 8))
  );
}

type Parts = { readonly payload: string; readonly screenshot: File | null };

/** Exactly one string `payload` and at most one file `screenshot`; any other part refuses the body. */
function partsOf(form: FormData): Parts | null {
  let payload: string | null = null;
  let screenshot: File | null = null;
  for (const [name, value] of form.entries()) {
    if (name === "payload" && typeof value === "string" && payload === null) payload = value;
    else if (name === "screenshot" && typeof value !== "string" && screenshot === null) screenshot = value;
    else return null;
  }
  return payload === null ? null : { payload, screenshot };
}

async function forward(request: Request, sessionId: string, body: FormData): Promise<NextResponse> {
  let response: Response;
  try {
    response = await fetch(`${env.BACKEND_URL}/api/v1/me/feedback`, {
      method: "POST",
      // No Content-Type: fetch writes the multipart boundary itself from the FormData body.
      headers: { ...pickForwardedHeaders(request.headers), Authorization: `Bearer ${sessionId}` },
      body,
      cache: "no-store",
      signal: AbortSignal.any([request.signal, AbortSignal.timeout(BACKEND_TIMEOUT_MS)]),
    });
  } catch {
    return answer({ outcome: "unknown" }, 502);
  }

  if (response.status === 200 || response.status === 201) {
    try {
      return feedbackSubmittedSchema.safeParse(await response.json()).success
        ? answer({ outcome: "saved" }, 200)
        : answer({ outcome: "unknown" }, 502);
    } catch {
      return answer({ outcome: "unknown" }, 502);
    }
  }

  switch (response.status) {
    case 400: {
      const title = await readProblemTitle(response);
      if (title === "Feedback.Empty") return answer({ outcome: "refused", reason: "empty" }, 400);
      if (title === "Feedback.CommentTooLong") return answer({ outcome: "refused", reason: "comment" }, 400);
      if (title === "Feedback.ScreenshotInvalid") return answer({ outcome: "refused", reason: "screenshot" }, 400);
      return invalid();
    }
    case 401:
      await response.body?.cancel();
      return answer({ outcome: "signedOut" }, 401);
    case 404:
      // Feedback.Closed (the gate) or JobSeeker.NotFound: either way there is nothing to send to.
      await response.body?.cancel();
      return answer({ outcome: "closed" }, 404);
    case 409:
      await response.body?.cancel();
      return answer({ outcome: "busy" }, 409);
    case 413:
      await response.body?.cancel();
      return answer({ outcome: "tooLarge" }, 413);
    case 429: {
      await response.body?.cancel();
      const retryAfter = response.headers.get("Retry-After");
      // The per-user window names its wait; the global two-at-a-time limit answers 429 without one.
      if (retryAfter === null) return answer({ outcome: "busy" }, 409);
      const retryAfterSeconds = parseRetryAfter(retryAfter);
      return answer({ outcome: "rateLimited", retryAfterSeconds }, 429, { "Retry-After": String(retryAfterSeconds) });
    }
    default:
      await response.body?.cancel();
      return answer({ outcome: "unknown" }, 502);
  }
}

async function submit(request: Request): Promise<NextResponse> {
  const sessionId = await getSessionId();
  if (sessionId === null) return answer({ outcome: "signedOut" }, 401);

  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().startsWith("multipart/form-data")) return invalid(415);

  const declared = Number(request.headers.get("content-length") ?? "");
  if (Number.isFinite(declared) && declared > MAX_BODY_BYTES) return answer({ outcome: "tooLarge" }, 413);

  const bytes = await readBounded(request.body, MAX_BODY_BYTES);
  if (bytes === null) return answer({ outcome: "tooLarge" }, 413);

  let form: FormData;
  try {
    form = await new Response(new Uint8Array(bytes), { headers: { "content-type": contentType } }).formData();
  } catch {
    return invalid();
  }
  const parts = partsOf(form);
  if (parts === null || new TextEncoder().encode(parts.payload).byteLength > MAX_PAYLOAD_BYTES) return invalid();

  let json: unknown;
  try {
    json = JSON.parse(parts.payload);
  } catch {
    return invalid();
  }
  const parsed = feedbackSubmissionPayloadSchema.safeParse(json);
  if (!parsed.success) return invalid();
  const comment = parsed.data.comment?.trim() || undefined;
  if (parsed.data.rating === undefined && comment === undefined) {
    return answer({ outcome: "refused", reason: "empty" }, 400);
  }

  let screenshot: Uint8Array<ArrayBuffer> | null = null;
  if (parts.screenshot !== null) {
    if (parts.screenshot.size === 0 || parts.screenshot.size > MAX_SCREENSHOT_BYTES) {
      return answer({ outcome: "refused", reason: "screenshot" }, 400);
    }
    screenshot = new Uint8Array(await parts.screenshot.arrayBuffer());
    if (!isAcceptedImage(screenshot)) return answer({ outcome: "refused", reason: "screenshot" }, 400);
  }

  // The device context is consent-based (LEK 9 kap. 28 §), and the stamped version is what records which
  // opt-in label the user saw. A page rendered by an earlier build than the one now answering showed a label
  // this version does not name, so its context is left out; the rating, text and image still go.
  const { renderedVersion, client, ...rest } = parsed.data;
  const appVersion = env.APP_VERSION;
  const consentedClient = appVersion !== null && renderedVersion === appVersion ? client : undefined;

  const body = new FormData();
  // JSON.stringify leaves out an absent version, which the backend stores as unknown.
  body.append(
    "payload",
    JSON.stringify({ ...rest, comment, client: consentedClient, appVersion: appVersion ?? undefined }),
  );
  // The filename is what makes ASP.NET read the part as a file; without one it is a second form field.
  if (screenshot !== null) body.append("screenshot", new Blob([screenshot]), "screenshot");

  return forward(request, sessionId, body);
}

export async function POST(request: Request): Promise<NextResponse> {
  if (!isSameOriginRequest(request)) return invalid(403);
  if (inFlight >= MAX_IN_FLIGHT) return answer({ outcome: "busy" }, 409);
  inFlight++;
  try {
    return await submit(request);
  } finally {
    inFlight--;
  }
}

function methodNotAllowed() {
  return NextResponse.json({ error: "method_not_allowed" }, { status: 405, headers: { ...HEADERS, Allow: "POST" } });
}

export const GET = methodNotAllowed;
export const PUT = methodNotAllowed;
export const PATCH = methodNotAllowed;
export const DELETE = methodNotAllowed;
export const OPTIONS = methodNotAllowed;
export function HEAD() {
  return new NextResponse(null, { status: 405, headers: { ...HEADERS, Allow: "POST" } });
}
