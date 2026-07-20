import { after } from "next/server";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession, getSessionId } from "@/lib/auth/session";
import { markFollowedCompanyAdSeen } from "@/lib/api/company-follows";
import { loadJobDetailData } from "@/lib/job-ads/load-job-detail-data";
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
  // ADR 0053 — the intercepting modal and the full page load the SAME data via
  // one shared helper (#596); the concurrency lives there, this route only maps
  // the discriminated result to its own modal shell.
  const result = await loadJobDetailData(id, includeRelated);

  switch (result.kind) {
    case "ok": {
      // #453 (cross-channel dedup) — opening the ad in-app (here: the intercepting modal) marks any Pending
      // follow-hit for it seen so the follow-digest suppresses the redundant email. Scheduled with `after()`
      // (#741) so the POST runs after the response is sent instead of blocking paint. Never throws; SeenAt is
      // not rendered. Kept in the caller — an ok-gated render side-effect, not data-loading: the user is authed
      // here (guest redirected above), and an `after()` callback in a Server Component cannot read cookies, so
      // the session id is read during render and passed in.
      const sessionId = await getSessionId();
      if (sessionId) {
        after(() => markFollowedCompanyAdSeen(id, sessionId));
      }
      return (
        <JobAdModalShell
          title={result.jobAd.title}
          company={result.jobAd.companyName}
        >
          <JobAdDetail
            jobAd={result.jobAd}
            headless
            initialSaved={result.initialSaved}
            initialApplied={result.initialApplied}
            followState={result.followState}
            match={result.match}
            ortGranularityByLabel={result.ortGranularityByLabel}
            previousApplicationCount={result.previousApplicationCount}
            contacts={result.jobAd.contacts}
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
