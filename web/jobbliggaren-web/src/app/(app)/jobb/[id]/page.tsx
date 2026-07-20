import { after } from "next/server";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession, getSessionId } from "@/lib/auth/session";
import { markFollowedCompanyAdSeen } from "@/lib/api/company-follows";
import { loadJobDetailData } from "@/lib/job-ads/load-job-detail-data";
import { JobAdDetail } from "@/components/job-ads/job-ad-detail";

interface PageProps {
  // Next.js 16 App Router: params är Promise (verifierat mot
  // node_modules/next/dist/docs/.../page).
  params: Promise<{ id: string }>;
  // #300 PR-5 (ADR 0084) — `?relaterade=on` bärs på en delad länk / hard-nav så
  // fullsidan graderar `Related` likt list-badgen. Default AV (frånvaro).
  searchParams: Promise<{ relaterade?: string }>;
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
export default async function JobbDetailPage({
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
  // ADR 0053 — the full page and the intercepting modal load the SAME data via
  // one shared helper (#596); the concurrency lives there, this route only maps
  // the discriminated result to its own chrome.
  const result = await loadJobDetailData(id, includeRelated);

  switch (result.kind) {
    case "ok": {
      // #453 (cross-channel dedup) — opening the ad in-app marks any Pending follow-hit for it seen so the
      // follow-digest suppresses the redundant email. Scheduled with `after()` (#741) so the POST runs after
      // the response is sent instead of blocking paint. Never throws; SeenAt is not rendered (no read-your-
      // write ordering). Kept in the caller — an ok-gated render side-effect, not data-loading: the user is
      // authed here (guest redirected above), and an `after()` callback in a Server Component cannot read
      // cookies, so the session id is read during render and passed in.
      const sessionId = await getSessionId();
      if (sessionId) {
        after(() => markFollowedCompanyAdSeen(id, sessionId));
      }
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
              jobAd={result.jobAd}
              initialSaved={result.initialSaved}
              initialApplied={result.initialApplied}
              followState={result.followState}
              match={result.match}
              ortGranularityByLabel={result.ortGranularityByLabel}
              previousApplicationCount={result.previousApplicationCount}
              contacts={result.jobAd.contacts}
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
