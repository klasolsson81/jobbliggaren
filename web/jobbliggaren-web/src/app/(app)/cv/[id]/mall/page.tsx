import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

interface Props {
  params: Promise<{ id: string }>;
}

/**
 * /cv/[id]/mall — mallbyggaren, RETIRED (deferred, not deleted — CV-pivot
 * 2026-07-16, deferral-ADR amends ADR 0093). This route is kept, never deleted;
 * it now returns 404 at the route level so a guessed or bookmarked URL cannot
 * reach the feature. Deliberately notFound(), NOT permanentRedirect: the builder
 * is deferred rather than superseded — nothing at /cv/[id] takes over its
 * function, so a 308 would assert a move that never happened AND be cached
 * permanently by browsers, locking visitors out of the URL if the builder
 * returns (contrast komplettera/page.tsx, where the Slutför guide genuinely
 * replaced the route). `TemplateBuilder`, `template-schematic`, the
 * `jp-mallbuilder` CSS block and the `pages.cv.mall` i18n namespace stay in the
 * tree (untouched) but have no consumer here anymore. The session gate runs
 * BEFORE the 404 so an unauthenticated visitor still lands on /logga-in —
 * route existence is not an auth oracle either way.
 */
export default async function CvTemplateBuilderPage(_props: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
