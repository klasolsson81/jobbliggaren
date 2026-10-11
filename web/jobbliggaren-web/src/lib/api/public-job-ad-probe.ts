import "server-only";
import { env } from "@/lib/env";
import {
  classifyPublicJobAdProbe,
  type PublicJobAdProbe,
} from "@/lib/job-ads/public-job-ad-answer";

/** Short, because the probe sits in front of the render: a stalled API must become a 503 quickly. */
const PUBLIC_JOB_AD_PROBE_TIMEOUT_MS = 2_500;

/**
 * Asks the public endpoint for a status and nothing else (ADR 0165): the proxy needs it before the first byte,
 * because a Server Component can set a 404 but never a 410 or a 503 with `Retry-After`.
 *
 * What goes to the API is the forwarded client address and nothing the visitor sent: `relayed` is the output of
 * `pickForwardedHeaders` (two forwarding headers at most), never the request's own bag, so no cookie or
 * Authorization header can reach an anonymous endpoint, and the per-IP budget (ADR 0160 D5) sees the visitor
 * and not the web container. A redirect is not followed and the body is discarded unread: the probe never
 * holds an ad's content.
 *
 * Total on every failure: a network error, a timeout or an unreadable answer is `unavailable`, which the edge
 * answers with a 503 and never with a 404 or a 410.
 */
export async function probePublicJobAd(id: string, relayed: Record<string, string>): Promise<PublicJobAdProbe> {
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/public/job-ads/${encodeURIComponent(id)}`, {
      method: "GET",
      headers: relayed,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(PUBLIC_JOB_AD_PROBE_TIMEOUT_MS),
    });
    await res.body?.cancel();
    return classifyPublicJobAdProbe(res.status, res.headers.get("Retry-After"));
  } catch {
    return { kind: "unavailable" };
  }
}
