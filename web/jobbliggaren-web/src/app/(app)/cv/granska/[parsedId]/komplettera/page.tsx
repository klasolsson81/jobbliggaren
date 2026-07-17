import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

interface Props {
  params: Promise<{ parsedId: string }>;
}

/**
 * /cv/granska/[parsedId]/komplettera — RETIRED (CV-pivot 5c, R4 2026-07-17).
 * This route was a 308 shim to the Slutför guide (Fas 4b PR-8.3, CTO Q7(c));
 * the guide itself is now deferred out of the MVP (see slutfor/page.tsx), so
 * the shim's target is a 404 and the shim follows it. Deliberately notFound(),
 * NOT a redirect: a 308 into a 404 would be cached permanently by browsers and
 * assert a destination that no longer exists (parity forbattra/page.tsx).
 * `CvGapFillForm` stays in the tree (untouched, revert-ready) but has no
 * consumer here anymore. The session gate runs BEFORE the 404 so an
 * unauthenticated visitor still lands on /logga-in.
 */
export default async function CvGapFillPage(_props: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
