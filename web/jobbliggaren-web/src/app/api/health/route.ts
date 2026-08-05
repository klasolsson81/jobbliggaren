import { NextResponse } from "next/server";

// Liveness probe for the web container (deploy/docker-compose.yml). ADR 0050
// Amendment 2026-07-18 invariant 4 prescribes exactly this shape: health checks
// terminate on the public Next surface, never on a backend route exposed for
// monitoring. It asserts only that this server is up and routing — backend
// readiness is the API container's own healthcheck, and compose already gates
// this service on it.
//
// force-dynamic so the probe reflects a live server rather than a build-time
// snapshot. It reads nothing and reveals nothing: no version, no environment,
// no dependency status.
export const dynamic = "force-dynamic";

export function GET() {
  return NextResponse.json({ status: "ok" });
}
