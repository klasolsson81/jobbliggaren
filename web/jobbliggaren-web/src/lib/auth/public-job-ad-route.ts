import { NextResponse, type NextRequest } from "next/server";
import { probePublicJobAd } from "@/lib/api/public-job-ad-probe";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";
import {
  PUBLIC_JOB_AD_MISSING_PATH,
  answerForPublicJobAdAction,
  answerForPublicJobAdProbe,
  isInternalPublicJobAdPath,
  publicJobAdIdFromPath,
  type PublicJobAdAnswer,
} from "@/lib/job-ads/public-job-ad-answer";

/**
 * The proxy's one branch for the public job page (ADR 0165). `/jobb/<uuid>` is a protected URL with an anonymous
 * twin: with no session cookie it is answered here, and with one it takes today's route untouched.
 *
 * A cookieless GET or HEAD is probed once for a status (a Server Component cannot set a 410 or a 503) and rewritten
 * to an internal page, with the status where the page cannot set it. A cookieless POST carrying `next-action` (the
 * language switch in the header, posted to the page's own URL) is rewritten to the same page with no probe, because an
 * action reads no ad. The rewrite carries exactly the request headers it is given, so the nonce and policy the proxy
 * prepared (ADR 0163) reach the page. The branch never calls `next()`: a cookieless request must not reach the
 * signed-in route or its intercepting modal, not even as a prefetch.
 *
 * Everything else returns null and the proxy carries on as before: a request with a session cookie, any other
 * method, and any path that is not exactly `/jobb/<uuid>`. The internal prefix is refused with a 404 for everyone,
 * because the internal pages are reachable only through a rewrite.
 */
export async function publicJobAdRoute(
  request: NextRequest,
  forwarded: Headers,
  hasSession: boolean
): Promise<NextResponse | null> {
  const { pathname, search } = request.nextUrl;

  if (isInternalPublicJobAdPath(pathname)) {
    return rewriteTo(request, forwarded, { path: PUBLIC_JOB_AD_MISSING_PATH, noindex: false });
  }

  if (hasSession) return null;

  const id = publicJobAdIdFromPath(pathname);
  if (id === null) return null;

  if (request.method === "POST" && request.headers.has("next-action")) {
    return rewriteTo(request, forwarded, answerForPublicJobAdAction(id.toLowerCase()));
  }
  if (request.method !== "GET" && request.method !== "HEAD") return null;

  // A GUID is case-insensitive and the page must have one URL: the canonical form is lower case.
  const canonicalId = id.toLowerCase();
  if (canonicalId !== id) {
    return NextResponse.redirect(new URL(`/jobb/${canonicalId}${search}`, request.url), 308);
  }

  const probe = await probePublicJobAd(canonicalId, pickForwardedHeaders(request.headers));
  return rewriteTo(request, forwarded, answerForPublicJobAdProbe(canonicalId, probe));
}

function rewriteTo(request: NextRequest, forwarded: Headers, answer: PublicJobAdAnswer): NextResponse {
  const headers = new Headers({ "Cache-Control": "no-store" });
  if (answer.noindex) headers.set("X-Robots-Tag", "noindex");
  if (answer.retryAfterSeconds !== undefined) headers.set("Retry-After", String(answer.retryAfterSeconds));
  return NextResponse.rewrite(new URL(answer.path, request.url), {
    status: answer.status,
    headers,
    request: { headers: forwarded },
  });
}
