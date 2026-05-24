import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getPipeline } from "@/lib/api/applications";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { getResumes } from "@/lib/api/resumes";
import { getJobAds } from "@/lib/api/job-ads";
import { OversiktPage } from "@/components/oversikt/oversikt-page";

/**
 * F6 P5 Punkt 4 — `/oversikt` route. Per-user-data: ingen delad cache.
 * CTO-dom 2026-05-24 D2: `force-dynamic` + per-request `Promise.all` mot
 * 5-6 befintliga endpoints. Inget composer-endpoint, ingen Worker-cache.
 *
 * GDPR + ADR 0045 klass (a) auth-gated 300ms p95: ingen shared cache.
 */
export const dynamic = "force-dynamic";

export default async function OversiktRoute() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const [profile, pipeline, savedJobAds, recentSearches, resumes, jobAds] =
    await Promise.all([
      getMyProfile(),
      getPipeline(),
      getSavedJobAds(),
      getRecentSearches(),
      getResumes(1, 20),
      getJobAds({
        page: 1,
        pageSize: 1,
        sortBy: "PublishedAtDesc",
      }),
    ]);

  // Unauthorized mid-render (token expired mellan layout-check och här):
  // redirecta. Övriga fel ⇒ degraderad render i OversiktPage.
  if (
    profile.kind === "unauthorized" ||
    pipeline.kind === "unauthorized" ||
    savedJobAds.kind === "unauthorized" ||
    recentSearches.kind === "unauthorized" ||
    resumes.kind === "unauthorized" ||
    jobAds.kind === "unauthorized"
  ) {
    redirect("/logga-in");
  }

  const displayName =
    profile.kind === "ok" ? profile.data.displayName : null;

  return (
    <OversiktPage
      email={user.email}
      displayName={displayName}
      profile={profile}
      pipeline={pipeline}
      savedJobAds={savedJobAds}
      recentSearches={recentSearches}
      resumes={resumes}
      jobAds={jobAds}
    />
  );
}
