import { NextResponse, type NextRequest } from "next/server";
import { probePublicJobAd } from "@/lib/api/public-job-ad-probe";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";
import {
  PUBLIC_JOB_AD_MISSING_PATH,
  answerForPublicJobAdProbe,
  isInternalPublicJobAdPath,
  publicJobAdIdFromPath,
  type PublicJobAdAnswer,
} from "@/lib/job-ads/public-job-ad-answer";

/**
 * The proxy's one branch for the public job page (ADR 0165). `/jobb/<uuid>` is a protected URL with an anonymous
 * twin: with no session cookie it is answered here, by the edge, and with one it takes today's route untouched.
 *
 * Why the edge: a Server Component can set a 404 and never a 410 or a 503 with `Retry-After`, and the status
 * has to be settled before the first byte. So the edge asks the public endpoint once (a probe, no content read)
 * and rewrites to an internal page, with the status where the page cannot set it (measured on Next 16.3.8: a
 * rewrite with `status` carries the status on GET and HEAD, and the forwarded request headers reach the page).
 * The URL stays `/jobb/<uuid>`.
 *
 * The rewrite carries exactly the request headers it is given, so a nonce and its policy prepared by the proxy
 * (ADR 0163) reach the page. The branch never calls `next()`: a cookieless `/jobb/<uuid>` request must not reach
 * the signed-in route or its intercepting modal, not even as a prefetch.
 *
 * Returns null for everything that is not this branch, and the proxy carries on as before:
 *   - a request with a session cookie (the signed-in page, the modal, Back/Forward, the #2094 bounce),
 *   - any method but GET and HEAD, and any path that is not exactly `/jobb/<uuid>`.
 *
 * It also refuses the internal prefix. The internal pages are reachable only through the rewrite above; a direct
 * request would serve the same content on a second URL, so it is a 404.
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
  if (request.method !== "GET" && request.method !== "HEAD") return null;

  const id = publicJobAdIdFromPath(pathname);
  if (id === null) return null;

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
