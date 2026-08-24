import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

interface Props {
  params: Promise<{ parsedId: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/granska/[parsedId]/forbattra — the åtgärda-lager (Förbättra), RETIRED
 * (deferred, not deleted — CV-pivot 2026-07-16, ADR 0112, CTO-bind D8 Opt C).
 * Without the builder, an applied improvement re-renders away the user's
 * original design (the #662 problem), so the whole affordance defers with it.
 * This route is kept and returns 404 at the route level — deliberately
 * notFound(), NOT permanentRedirect: the layer is deferred, not superseded,
 * so a 308 would assert a move that never happened AND be cached permanently
 * by browsers (parity mall/page.tsx; komplettera/page.tsx contrasts in REASON
 * only — it was genuinely superseded, but it answers with notFound() too). The
 * read-only review at /cv/granska/[parsedId] stays live. `CvImprovePanel`,
 * `getCvImprovements`, their DTO exports and the `cv.improve` i18n namespace
 * stay in the tree (untouched) but have no consumer here anymore. The session
 * gate runs BEFORE the 404 so an unauthenticated visitor still lands on
 * /logga-in — route existence is not an auth oracle either way.
 */
export default async function CvImprovePage(_props: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
