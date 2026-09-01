import { after } from "next/server";
import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Info } from "lucide-react";
import { getServerSession, getSessionId } from "@/lib/auth/session";
import {
  getNewFollowedCompanyAds,
  markFollowedAdsSeen,
} from "@/lib/api/company-follows";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { renderSection } from "@/components/foretag/foretag-section";
import { NewAdsViewSwitch } from "@/components/company-follows/new-ads-view-switch";
import { InfoDialog } from "@/components/common/info-dialog";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("foretag.newAds.meta.title") };
}

/**
 * `/foretag/bevakade/nya` (#1576) — the ads behind the Översikt "N nya annonser från bevakade
 * företag" number. Before this route the number had no destination at all: `/jobb` cannot express
 * the rail's set, because both the ort filter and the "endast matchade" filter are PER WATCH while
 * every `/jobb` axis is global, so no single value is correct in either direction.
 *
 * <para/> Its own route rather than a section on `/foretag/bevakade` for two reasons: that page is
 * standing state (what I follow) and this is events (what happened since I looked), a line #1558
 * already drew; and only a separate route can acknowledge coherently — editing a watch would
 * otherwise acknowledge ads the user never looked at.
 *
 * <para/> Arriving CONSUMES the set: `after()` moves the watermark, so a reload shows the empty
 * state. That is the design, but it must not be a surprise — the summary block says so before the
 * user leaves, and the empty state names where the ads still are (design-reviewer Blocker 2).
 *
 * <para/> The matching arm is a client-side view over rows this render already fetched, never a URL
 * axis: a `<Link>` navigation would re-read AFTER the watermark moved and land on an empty set. See
 * `NewAdsViewSwitch`. The route carries no searchParams at all — it has no view-state to share.
 */
export default async function NyaFollowedAdsPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  // The matching rule is read from the keys the watch-filter dialog already owns, never copied:
  // one rule, one text. Same foreign-namespace borrow as `CompanySummary`, for the same reason.
  const tRule = await getTranslations("jobads.companyWatches");

  const result = await getNewFollowedCompanyAds();

  // fetch-then-mark (#759 posture, parity `jobb-results.tsx`): the window is the SERVER's max over
  // the rows this render read, in the SCAN clock's unit. It is never derived here — an ad's own
  // `createdAt` is its ingest time and always sits BELOW its hit's, so a client-computed window
  // would leave the watermark under every hit just read and the count would never reset.
  //
  // Gated on a successful read: without a coherent baseline we do not advance (a transient error
  // must not silently zero the count). Deferred with `after()` so the response does not pay a POST
  // round-trip; the session is read DURING render and passed in, because an `after()` callback in a
  // Server Component cannot read cookies (#741).
  if (result.kind === "ok" && result.data.acknowledgedThrough !== null) {
    const acknowledgedThrough = result.data.acknowledgedThrough;
    const sessionId = await getSessionId();
    if (sessionId) {
      after(() => markFollowedAdsSeen(acknowledgedThrough, sessionId));
    }
  }

  return (
    <>
      <ForetagPagehero
        title={t("foretag.newAds.heading")}
        lede={t("foretag.newAds.lede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="nyaAnnonser" />
        {renderSection(result, t, t("foretag.newAds.loadErrorTitle"), (data) => {
          if (data.rows.length === 0) {
            // `.jp-empty` — the primitive every sibling /foretag surface uses, and the one
            // `JobAdList` falls back to, so this route no longer carries two empty-state languages
            // (design-reviewer Major 5).
            return (
              <div className="jp-empty">
                <div className="jp-empty__title">
                  {t("foretag.newAds.emptyTitle")}
                </div>
                <p className="jp-empty__body">{t("foretag.newAds.emptyBody")}</p>
                <div className="jp-empty__actions">
                  <Link className="jp-btn jp-btn--primary" href="/foretag/bevakade">
                    {t("foretag.newAds.backToWatches")}
                  </Link>
                </div>
              </div>
            );
          }

          // NOT ASSESSED means the user stated no occupation, so the grade predicate is inert. The
          // server carries the fact; deriving it from the rows would force a choice between `some`
          // and `every` that disagree precisely when the all-or-none invariant breaks.
          const assessed = data.matchingAssessed;
          const matchingRows = data.rows.filter((row) => row.matchesYou === true);
          const shownCount = data.rows.length;

          // Both numbers stand in BOTH arms (senior-cto-advisor 2026-08-31): arriving here IS the
          // acknowledgement, and that is only honest if the surface shows what it acknowledged. The
          // counts are always over the WHOLE fetched set, never the filtered view.
          //
          // TRUNCATED changes what the numbers MEAN, not how they are counted. `rows.length` is then
          // the capped count, so a sentence saying "N nya annonser sedan ditt senaste besök" would
          // claim a total the page never read — and Översikt, whose count is uncapped, would say a
          // different one (code-reviewer). ADR 0120: a rendered number is true or absent, so the
          // truncated sentence states what is SHOWN and claims no total. The uncounted remainder is
          // named by the note at the foot.
          const summary = data.truncated
            ? assessed
              ? t("foretag.newAds.summaryTruncated", {
                  shown: shownCount,
                  matching: matchingRows.length,
                })
              : t("foretag.newAds.summaryTruncatedNotAssessed", {
                  shown: shownCount,
                })
            : assessed
              ? t("foretag.newAds.summary", {
                  total: shownCount,
                  matching: matchingRows.length,
                })
              : t("foretag.newAds.summaryNotAssessed", { total: shownCount });

          return (
            <>
              <p className="jp-matchline tabular-nums">
                {summary}
                {assessed && (
                  <InfoDialog
                    title={tRule("filter.onlyMatchedHelpTitle")}
                    paragraphs={[
                      tRule("filter.onlyMatchedHelpBody1"),
                      tRule("filter.onlyMatchedHelpBody2"),
                    ]}
                    ariaLabel={tRule("filter.onlyMatchedHelpAria")}
                  />
                )}
              </p>

              {/* The consumption is irreversible and silent, so it is disclosed HERE — while the
                  ads are still on screen — not discovered on the next visit (Blocker 2). */}
              <p className="jp-transparency-note mt-2">
                <Info size={16} aria-hidden="true" />
                <span>{t("foretag.newAds.consumptionNote")}</span>
              </p>

              {/* Not assessed says so and offers the one action that changes it — never a silent
                  empty matching arm, and never a fabricated "0 matchar dig". `.jp-matchline`, the
                  exact sibling form in `CompanyWatchRow`: `.jp-transparency-note` is `display:flex`
                  for a LEADING ICON, which tore the CTA out of the sentence (Major 4). */}
              {!assessed && (
                <p className="jp-matchline">
                  {tRule("matchNudge")}{" "}
                  <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
                    {tRule("matchNudgeCta")}
                  </Link>
                </p>
              )}

              {assessed ? (
                <NewAdsViewSwitch
                  groupLabel={t("foretag.newAds.view.groupLabel")}
                  allLabel={t("foretag.newAds.view.all")}
                  matchingLabel={t("foretag.newAds.view.matching")}
                  filteredNote={tRule("filter.activeOnlyMatched")}
                  emptyTitle={t("foretag.newAds.matchingEmptyTitle")}
                  emptyBody={t("foretag.newAds.matchingEmptyBody")}
                  emptyAction={t("foretag.newAds.matchingEmptyAction")}
                  matchingCount={matchingRows.length}
                  all={<JobAdList jobAds={data.rows.map((row) => row.ad)} />}
                  matching={
                    <JobAdList jobAds={matchingRows.map((row) => row.ad)} />
                  }
                />
              ) : (
                <div className="mt-4">
                  <JobAdList jobAds={data.rows.map((row) => row.ad)} />
                </div>
              )}

              {/* The cap was hit. Everything above the window this page acknowledged stays
                  unacknowledged and returns next visit, so nothing was swallowed. */}
              {data.truncated && (
                <p className="jp-transparency-note mt-4">
                  <Info size={16} aria-hidden="true" />
                  <span>{t("foretag.newAds.truncated")}</span>
                </p>
              )}
            </>
          );
        })}
      </div>
    </>
  );
}
