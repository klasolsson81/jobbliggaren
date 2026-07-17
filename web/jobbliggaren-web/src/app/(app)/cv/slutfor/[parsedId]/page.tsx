import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

interface Props {
  params: Promise<{ parsedId: string }>;
}

/**
 * /cv/slutfor/[parsedId] — the four-step Slutför guide, RETIRED (deferred, not
 * deleted — CV-pivot 5c, R4 2026-07-17; same ADR 0112 posture as the åtgärda
 * layer). With "spara direkt" (import auto-promotes in place) the guide's job —
 * hand-completing a pending parse into a canonical CV — left the MVP: a pending
 * parse is read at /cv/granska/[parsedId], and the way to a canonical CV is
 * fixing the FILE and uploading again (5b security-bind B3's product
 * consequence). This route is kept and returns 404 at the route level —
 * deliberately notFound(), NOT permanentRedirect: the guide is deferred, not
 * superseded, so a 308 would assert a move that never happened AND be cached
 * permanently by browsers (parity forbattra/page.tsx). `CvCompleteGuide`,
 * `promoteParsedResumeFromGuideAction`, and the `cv.slutfor` + `resumes.guide`
 * i18n namespaces stay in the tree (untouched, revert-ready) but have no
 * consumer here anymore. The session gate runs BEFORE the 404 so an
 * unauthenticated visitor still lands on /logga-in — route existence is not an
 * auth oracle either way.
 */
export default async function CvCompleteGuidePage(_props: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
