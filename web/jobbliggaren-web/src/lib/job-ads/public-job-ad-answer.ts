import { isValidId } from "@/lib/validation/guid";

/**
 * The pure half of the public job page's edge (ADR 0165): which paths are the public detail, what the
 * public endpoint's status means, and what the edge answers for each. No `next/*` import, so the proxy
 * and the unit tests share it.
 */

/** The internal route group's prefix. Only the proxy rewrites into it; a direct request is a 404 (ADR 0165). */
const PUBLIC_JOB_AD_INTERNAL_PREFIX = "/publik";

const PUBLIC_JOB_AD_PAGE_PATH = (id: string): string => `${PUBLIC_JOB_AD_INTERNAL_PREFIX}/jobb/${id}`;
export const PUBLIC_JOB_AD_GONE_PATH = `${PUBLIC_JOB_AD_INTERNAL_PREFIX}/borttagen`;
export const PUBLIC_JOB_AD_BUSY_PATH = `${PUBLIC_JOB_AD_INTERNAL_PREFIX}/overbelastad`;
/** No route matches this path, so the root not-found answers it with a real 404. */
export const PUBLIC_JOB_AD_MISSING_PATH = `${PUBLIC_JOB_AD_INTERNAL_PREFIX}/saknas`;

const DETAIL_PATH = /^\/jobb\/([^/]+)$/;

/**
 * The id of `/jobb/<uuid>`, exactly that shape and no other: one segment, a well-formed GUID, no
 * encoding. Anything else is not the public detail and keeps today's route (the login redirect).
 */
export function publicJobAdIdFromPath(pathname: string): string | null {
  const match = DETAIL_PATH.exec(pathname);
  const segment = match?.[1];
  return segment !== undefined && isValidId(segment) ? segment : null;
}

/**
 * Whether the path is, or decodes to, the internal prefix. Next matches a route on the decoded path, so
 * `/%70ublik/...` would reach the internal page past a literal check. Decoded up to three times, and a
 * path that does not decode is treated as internal: a refusal costs a 404, a miss costs duplicated content.
 */
export function isInternalPublicJobAdPath(pathname: string): boolean {
  let current = pathname;
  for (let round = 0; round < 3; round += 1) {
    if (current === PUBLIC_JOB_AD_INTERNAL_PREFIX || current.startsWith(`${PUBLIC_JOB_AD_INTERNAL_PREFIX}/`)) {
      return true;
    }
    let decoded: string;
    try {
      decoded = decodeURIComponent(current);
    } catch {
      return true;
    }
    if (decoded === current) return false;
    current = decoded;
  }
  return true;
}

/** What the public endpoint's answer means to the edge. Only `listed` carries content, and the probe never reads it. */
export type PublicJobAdProbe =
  | { readonly kind: "listed" }
  | { readonly kind: "gone" }
  | { readonly kind: "notFound" }
  | { readonly kind: "busy"; readonly retryAfterSeconds: number }
  | { readonly kind: "unavailable" };

export const RETRY_AFTER_DEFAULT_SECONDS = 30;
export const RETRY_AFTER_MAX_SECONDS = 120;

/**
 * An integer number of seconds from the API's `Retry-After`, or the default, never above the ceiling. The
 * header is never relayed raw: a date or a huge number from upstream must not become our promise.
 */
export function boundedRetryAfter(headerValue: string | null): number {
  const trimmed = headerValue?.trim() ?? "";
  if (!/^\d{1,6}$/.test(trimmed)) return RETRY_AFTER_DEFAULT_SECONDS;
  const seconds = Number.parseInt(trimmed, 10);
  if (seconds < 1) return RETRY_AFTER_DEFAULT_SECONDS;
  return Math.min(seconds, RETRY_AFTER_MAX_SECONDS);
}

/**
 * The public endpoint's status, as the edge reads it (ADR 0160 D3, D9.5).
 *
 * 410 is an ended or an erased ad, one answer. 404 and 400 are deterministic client answers, so the page is a
 * 404 (a 503 would make a crawler retry a URL that will never exist). 429 is the budget. Every other status,
 * a redirect included, is an outage and can never read as a removal: Google drops a URL that answers 404 or 410.
 */
export function classifyPublicJobAdProbe(status: number, retryAfterHeader: string | null): PublicJobAdProbe {
  if (status === 200) return { kind: "listed" };
  if (status === 410) return { kind: "gone" };
  if (status === 404 || status === 400) return { kind: "notFound" };
  if (status === 429) return { kind: "busy", retryAfterSeconds: boundedRetryAfter(retryAfterHeader) };
  return { kind: "unavailable" };
}

/** What the proxy answers: a rewrite target, with a status and headers where the page itself cannot set them. */
export type PublicJobAdAnswer = {
  readonly path: string;
  readonly status?: number;
  readonly retryAfterSeconds?: number;
  readonly noindex: boolean;
};

/** A Server Action posted to the page's own address: the internal page, with no status of its own. */
export function answerForPublicJobAdAction(id: string): PublicJobAdAnswer {
  return { path: PUBLIC_JOB_AD_PAGE_PATH(id), noindex: false };
}

export function answerForPublicJobAdProbe(id: string, probe: PublicJobAdProbe): PublicJobAdAnswer {
  switch (probe.kind) {
    case "listed":
      return { path: PUBLIC_JOB_AD_PAGE_PATH(id), noindex: false };
    case "gone":
      return { path: PUBLIC_JOB_AD_GONE_PATH, status: 410, noindex: true };
    case "notFound":
      return { path: PUBLIC_JOB_AD_MISSING_PATH, noindex: false };
    case "busy":
      return { path: PUBLIC_JOB_AD_BUSY_PATH, status: 503, retryAfterSeconds: probe.retryAfterSeconds, noindex: true };
    case "unavailable":
      return {
        path: PUBLIC_JOB_AD_BUSY_PATH,
        status: 503,
        retryAfterSeconds: RETRY_AFTER_DEFAULT_SECONDS,
        noindex: true,
      };
  }
}
