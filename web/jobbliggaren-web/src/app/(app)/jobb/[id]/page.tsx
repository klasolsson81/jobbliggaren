import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getJobAd } from "@/lib/api/job-ads";
import { isJobAdSaved } from "@/lib/api/saved-job-ads";
import { hasAppliedJobAd } from "@/lib/api/job-ad-status";
import { getJobAdMatchDetail } from "@/lib/api/job-ad-match";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { buildOrtGranularityMap } from "@/lib/job-ads/ort-granularity";
import { JobAdDetail } from "@/components/job-ads/job-ad-detail";

interface PageProps {
  // Next.js 16 App Router: params är Promise (verifierat mot
  // node_modules/next/dist/docs/.../page).
  params: Promise<{ id: string }>;
}

/**
 * Fullsida för en jobbannons (`/jobb/[id]`). Renderas vid hard-nav /
 * sidladdning / delad länk / SEO-indexering. Vid soft-nav från listan
 * fångar `@modal/(.)jobb/[id]` istället och visar samma `JobAdDetail`
 * i modal (ADR 0053 — en presentationskomponent, två kontexter).
 *
 * notFound (okänt id) → Next `notFound()` (404-sida). unauthorized →
 * `/logga-in`. rateLimited/error → civil felruta.
 */
export default async function JobbDetailPage({ params }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { id } = await params;
  const result = await getJobAd(id);

  switch (result.kind) {
    case "ok": {
      // F6 P5 Punkt 2 PR5 — parallell server-fetch av Spara + Har-ansökt-state.
      // Promise.all undviker waterfall; båda misslyckas civilt (returnerar false).
      // F4-16 — matchnings-detalj i samma Promise.all (degraderar till null =
      // ingen sektion).
      // Spår 3 PR-D — taxonomin hämtas i samma Promise.all (cachad 1h, statisk
      // referensdata) så match-modalens RegionFit-bevis kan visa kommun-träff
      // vs län-träff. Granularitets-kartan byggs FE-side (architect NOTE-2);
      // taxonomi-fel → null → generisk bevisform (degraderar civilt).
      const [initialSaved, initialApplied, match, taxonomyResult] =
        await Promise.all([
          isJobAdSaved(id),
          hasAppliedJobAd(id),
          getJobAdMatchDetail(id),
          getTaxonomyTree(),
        ]);
      const ortGranularityByLabel =
        match != null
          ? buildOrtGranularityMap(
              taxonomyResult.kind === "ok" ? taxonomyResult.data : null,
            )
          : undefined;
      return (
        <div className="jp-container jp-page">
          <div
            className="jp-modal"
            style={{
              width: "100%",
              maxWidth: 760,
              maxHeight: "none",
              marginInline: "auto",
              boxShadow: "none",
              animation: "none",
            }}
          >
            <JobAdDetail
              jobAd={result.data}
              initialSaved={initialSaved}
              initialApplied={initialApplied}
              match={match}
              ortGranularityByLabel={ortGranularityByLabel}
            />
          </div>
        </div>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <div className="jp-container jp-page">
          <div
            role="alert"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
          >
            <p className="text-body font-medium text-warning-700">
              {t("common.rateLimitedTitle")}
            </p>
            <p className="mt-1 text-body-sm text-warning-700">
              {t("common.rateLimitedBody", {
                seconds: result.retryAfterSeconds,
              })}
            </p>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page">
          <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
            <p className="text-body font-medium">
              {t("jobb.detail.loadErrorTitle")}
            </p>
            <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
          </div>
        </div>
      );
  }
}
