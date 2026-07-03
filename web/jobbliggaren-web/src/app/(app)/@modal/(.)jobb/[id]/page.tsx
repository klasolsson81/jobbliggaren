import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getJobAd } from "@/lib/api/job-ads";
import { isJobAdSaved } from "@/lib/api/saved-job-ads";
import { hasAppliedJobAd } from "@/lib/api/job-ad-status";
import { getCompanyWatchStatus, markFollowedCompanyAdSeen } from "@/lib/api/company-follows";
import { getJobAdMatchDetail } from "@/lib/api/job-ad-match";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { buildOrtGranularityMap } from "@/lib/job-ads/ort-granularity";
import { JobAdDetail } from "@/components/job-ads/job-ad-detail";
import { JobAdModalShell } from "@/components/job-ads/job-ad-modal-shell";

interface PageProps {
  params: Promise<{ id: string }>;
  // #300 PR-5 (ADR 0084) — `?relaterade=on` bärs över från listans URL (soft-nav
  // behåller list-searchParams) så modalen graderar `Related` likt list-badgen.
  searchParams: Promise<{ relaterade?: string }>;
}

/**
 * Intercepting Route för @modal-slotten. `(.)jobb/[id]` matchar samma
 * segment-nivå som slot-monteringspunkten `(app)` — `@modal` är en slot,
 * INTE ett route-segment, så `jobb` ligger en segment-nivå upp trots två
 * fil-nivåer (Next-docs Intercepting Routes §Convention + §Modals,
 * verifierat node_modules/next/dist/docs Next 16.2.x).
 *
 * Soft-nav (radklick → Link /jobb/[id]) fångas här → modal. Hard-nav /
 * refresh / delad länk träffar `/jobb/[id]/page.tsx` (fullsida). Samma
 * `getJobAd` + `JobAdDetail` i båda (ADR 0053, DRY).
 *
 * RSC: server-fetch här; endast modal-chromet (JobAdModalShell) är
 * "use client". JobAdDetail-trädet förblir Server Component (passeras
 * som children — serialiserbart RSC-träd, ingen funktion över gränsen).
 */
export default async function InterceptedJobbModal({
  params,
  searchParams,
}: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { id } = await params;
  // #300 PR-5 — master-switch för related-gradering i detalj-anropet. Parsa BARA
  // on-värdet (paritet med listans page.tsx). Default AV.
  const { relaterade } = await searchParams;
  const includeRelated = relaterade === "on";
  const result = await getJobAd(id);

  switch (result.kind) {
    case "ok": {
      // F4-16 — matchnings-detalj parallellt med Spara/Har-ansökt (ingen
      // waterfall). Degraderar civilt till null (ingen sektion) vid fel.
      const [initialSaved, initialApplied, followState, match] = await Promise.all([
        isJobAdSaved(id),
        hasAppliedJobAd(id),
        getCompanyWatchStatus(id),
        getJobAdMatchDetail(id, includeRelated),
        // #453 (cross-channel dedup) — opening the ad in-app (here: the intercepting modal) marks any
        // Pending follow-hit for it seen, so the follow-digest suppresses the redundant email.
        // Fire-and-forget in the fan-out (parallel, never throws; SeenAt is not rendered).
        markFollowedCompanyAdSeen(id),
      ]);
      // Spår 3 PR-D — taxonomin behövs BARA när det finns en match (annars
      // byggs ingen granularitets-karta). En inloggad användare utan match
      // ska inte betala för round-trippen (cleanup-pass: gate guest-prefetch).
      // Cachad 1h (statisk referensdata); kartan byggs FE-side (architect
      // NOTE-2), taxonomi-fel → null → generisk bevisform.
      const taxonomyResult = match != null ? await getTaxonomyTree() : null;
      const ortGranularityByLabel =
        match != null
          ? buildOrtGranularityMap(
              taxonomyResult?.kind === "ok" ? taxonomyResult.data : null,
            )
          : undefined;
      return (
        <JobAdModalShell
          title={result.data.title}
          company={result.data.companyName}
        >
          <JobAdDetail
            jobAd={result.data}
            headless
            initialSaved={initialSaved}
            initialApplied={initialApplied}
            followState={followState}
            match={match}
            ortGranularityByLabel={ortGranularityByLabel}
          />
        </JobAdModalShell>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <JobAdModalShell title={t("common.rateLimitedTitle")} company="">
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-primary">
              {t("common.rateLimitedBody", {
                seconds: result.retryAfterSeconds,
              })}
            </p>
          </div>
          <div className="jp-modal__foot">
            <span className="jp-modal__foot__spacer" />
          </div>
        </JobAdModalShell>
      );
    case "forbidden":
    case "error":
      return (
        <JobAdModalShell title={t("jobb.detail.loadErrorTitle")} company="">
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-primary">
              {t("common.errorBodyRetry")}
            </p>
          </div>
          <div className="jp-modal__foot">
            <span className="jp-modal__foot__spacer" />
          </div>
        </JobAdModalShell>
      );
  }
}
