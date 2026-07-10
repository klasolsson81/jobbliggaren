import { permanentRedirect, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

interface Props {
  params: Promise<{ parsedId: string }>;
}

/**
 * /cv/granska/[parsedId]/komplettera — superseded by the Slutför-guide
 * (Fas 4b PR-8.3, CTO Q7(c)). This child route is kept, never deleted; it now
 * 308-redirects (permanent) to /cv/slutfor/[parsedId], the four-step guide that
 * replaced the gap-fill form. `CvGapFillForm` stays in the tree (untouched) but
 * has no consumer here anymore. The session gate runs BEFORE the redirect so an
 * unauthenticated visitor still lands on /logga-in.
 */
export default async function CvGapFillPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { parsedId } = await params;
  permanentRedirect(`/cv/slutfor/${parsedId}`);
}
