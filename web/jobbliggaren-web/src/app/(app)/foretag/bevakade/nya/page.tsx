import { after } from "next/server";
import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession, getSessionId } from "@/lib/auth/session";
import {
  getNewFollowedCompanyAds,
  markFollowedAdsSeen,
} from "@/lib/api/company-follows";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { renderSection } from "@/components/foretag/foretag-section";
import { InfoDialog } from "@/components/common/info-dialog";
import type { Metadata } from "next";

/** The matching arm. A closed sentinel, not an i18n string — it is URL contract. */
const ONLY_MATCHED_PARAM = "matchande";
const ONLY_MATCHED_ON = "on";

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
 * <para/> The matching arm is a VIEW filter over rows the page already fetched whole, never a second
 * request: two requests can see two different sets, which is the divergence this route exists to
 * close. Plain `<Link>`s, so it works with no client JS (parity `ForetagSubnav`).
 */
export default async function NyaFollowedAdsPage({
  searchParams,
}: {
  searchParams: Promise<{ [key: string]: string | string[] | undefined }>;
}) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  // The matching rule is read from the keys the watch-filter dialog already owns, never copied:
  // one rule, one text. Same foreign-namespace borrow as `CompanySummary`, for the same reason.
  const tRule = await getTranslations("jobads.companyWatches");

  const params = await searchParams;
  const rawArm = params[ONLY_MATCHED_PARAM];
  const armOn =
    (Array.isArray(rawArm) ? rawArm[0] : rawArm) === ONLY_MATCHED_ON;

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
        <ForetagSubnav active="bevakade" />
        {renderSection(result, t, t("foretag.newAds.loadErrorTitle"), (data) => {
          if (data.rows.length === 0) {
            return (
              <div className="jp-appsummary jp-appsummary--empty">
                <p className="jp-appsummary__emptytitle">
                  {t("foretag.newAds.emptyTitle")}
                </p>
                <p className="jp-appsummary__emptybody">
                  {t("foretag.newAds.emptyBody")}
                </p>
                <Link className="jp-btn jp-btn--emphasis" href="/foretag/bevakade">
                  {t("foretag.newAds.backToWatches")}
                </Link>
              </div>
            );
          }

          // NOT ASSESSED means the user stated no occupation, so the grade predicate is inert. The
          // server carries the fact; deriving it from the rows would force a choice between `some`
          // and `every` that disagree precisely when the all-or-none invariant breaks.
          const assessed = data.matchingAssessed;
          const matchingCount = data.rows.filter(
            (row) => row.matchesYou === true
          ).length;

          // Both numbers stand in BOTH arms (senior-cto-advisor 2026-08-31): arriving here IS the
          // acknowledgement, and that is only honest if the surface shows what it acknowledged. The
          // counts are always over the WHOLE set, never the filtered view.
          const shown =
            armOn && assessed
              ? data.rows.filter((row) => row.matchesYou === true)
              : data.rows;

          return (
            <>
              <p className="jp-matchline tabular-nums">
                {assessed
                  ? t("foretag.newAds.summary", {
                      total: data.rows.length,
                      matching: matchingCount,
                    })
                  : t("foretag.newAds.summaryNotAssessed", {
                      total: data.rows.length,
                    })}
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

              {/* Not assessed says so and offers the one action that changes it — never a silent
                  empty matching arm, and never a fabricated "0 matchar dig". */}
              {!assessed && (
                <p className="jp-transparency-note">
                  <span>{tRule("matchNudge")}</span>{" "}
                  <Link className="jp-countlink" href="/installningar#matchning">
                    {tRule("matchNudgeCta")}
                  </Link>
                </p>
              )}

              {assessed && (
                <p className="jp-matchline">
                  <Link
                    className="jp-countlink"
                    href={
                      armOn
                        ? "/foretag/bevakade/nya"
                        : `/foretag/bevakade/nya?${ONLY_MATCHED_PARAM}=${ONLY_MATCHED_ON}`
                    }
                  >
                    {armOn
                      ? t("foretag.newAds.showAll")
                      : t("foretag.newAds.showOnlyMatched")}
                  </Link>
                </p>
              )}

              <JobAdList jobAds={shown.map((row) => row.ad)} />

              {/* The cap was hit. Everything above the window this page acknowledged stays
                  unacknowledged and returns next visit, so nothing was swallowed. */}
              {data.truncated && (
                <p className="jp-transparency-note">
                  {t("foretag.newAds.truncated")}
                </p>
              )}
            </>
          );
        })}
      </div>
    </>
  );
}
