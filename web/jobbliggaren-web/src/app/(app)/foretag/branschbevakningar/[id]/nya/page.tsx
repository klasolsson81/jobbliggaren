import type { ReactNode } from "react";
import { after } from "next/server";
import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ArrowLeft, Info } from "lucide-react";
import { getServerSession, getSessionId } from "@/lib/auth/session";
import {
  getCriterionReference,
  getNewCriterionAds,
  markCriterionNewAdsSeen,
} from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import { CriterionBreadth } from "@/components/company-criteria/criterion-breadth";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { NewAdsViewSwitch } from "@/components/company-follows/new-ads-view-switch";
import { InfoDialog } from "@/components/common/info-dialog";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";
import { PageFeedback } from "@/components/feedback/page-feedback";

/**
 * The title resolves against the record's absence, as on the two sibling detail routes. The read is
 * the page's own, with the same argument, so the two collapse into one request.
 */
export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { id } = await params;
  const result = await getNewCriterionAds(id);
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("foretag.smartaBevakningar.newAds.meta.title") };
}

const EMPTY_REFERENCE: CriterionReference = {
  sniVersion: "",
  kommunVersion: "",
  sni: [],
  lan: [],
};

interface Props {
  params: Promise<{ id: string }>;
}

/**
 * `/foretag/branschbevakningar/[id]/nya` (#1663, ADR 0175 D7) — the ads behind one Branschbevakning's
 * "N nya" pill on Översikt. The structural sibling of `[id]/annonser` (layout, breadth, seat note,
 * attribution) and of `/foretag/bevakade/nya` (the window, the consumption, the view switch).
 *
 * <para/> No `ForetagSubnav`: its "Nya annonser" tab is the follow axis (ADR 0117 D3). The follow
 * page's heading, lede and empty body are not borrowed either: they name followed companies.
 *
 * <para/> Arriving consumes the window: `after()` moves the criterion's seen watermark, and the
 * consumption note says so while the ads are on screen. The matching arm is therefore client-side
 * over this render's rows, never a URL axis (`NewAdsViewSwitch`).
 *
 * <para/> A refused or unscanned criterion is a state of its own, never "nothing new" (ADR 0120).
 */
export default async function NewCriterionAdsPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages.foretag");
  // The matching rule is read from the keys the watch-filter dialog owns, as on the follow page.
  const tRule = await getTranslations("jobads.companyWatches");
  const tMatch = await getTranslations("jobads.ui.match");

  const { id } = await params;
  const backHref = `/foretag/branschbevakningar/${id}`;

  // The list route and the count route are never read here: the criterion's identity rides this
  // response.
  const [result, referenceResult] = await Promise.all([
    getNewCriterionAds(id),
    getCriterionReference(),
  ]);

  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <ErrorShell
          title={t("newAds.loadErrorTitle")}
          body={t("criteria.ads.rateLimited")}
          backHref={backHref}
          backLabel={t("criteria.ads.backLink")}
        />
      );
    case "forbidden":
    case "error":
      return (
        <ErrorShell
          title={t("newAds.loadErrorTitle")}
          body={t("criteria.ads.loadErrorBody")}
          backHref={backHref}
          backLabel={t("criteria.ads.backLink")}
        />
      );
  }

  const data = result.data;
  const { criterion } = data;

  // The token is the server's, from the window it cut, and goes back verbatim: re-serialising it
  // through Date would drop the microseconds and acknowledge below the hits just read. The session
  // is read during render because an `after()` callback cannot read cookies (#741). A closed state
  // acknowledges nothing, whatever the token says.
  if (data.acknowledgedThrough !== null && !data.tooBroad && !data.notMaterialised) {
    const acknowledgedThrough = data.acknowledgedThrough;
    const sessionId = await getSessionId();
    if (sessionId) {
      after(() => markCriterionNewAdsSeen(id, acknowledgedThrough, sessionId));
    }
  }

  const reference = referenceResult.kind === "ok" ? referenceResult.data : EMPTY_REFERENCE;
  const userLabel = criterion.label?.trim() ?? "";
  const derived = deriveDisplayLabel(
    criterion.sniCodes, criterion.municipalityCodes, reference, {
      moreSuffix: t("criteria.moreSuffix"),
      separator: " · ",
    });
  const title = userLabel.length > 0 ? userLabel : (derived ?? t("criteria.row.untitled"));

  let body: ReactNode;
  if (data.tooBroad) {
    body = (
      <div className="jp-empty mt-6">
        <p className="jp-empty__body text-body-sm text-text-primary">
          {t("criteria.ads.tooBroadNoList")}
        </p>
        <div className="jp-empty__actions">
          <Link className="jp-btn jp-btn--primary" href={backHref}>
            {t("criteria.ads.backLink")}
          </Link>
          <Link className="jp-btn jp-btn--ghost" href="/foretag/branschbevakningar">
            {t("criteria.ads.matchingTooBroadCta")}
          </Link>
        </div>
      </div>
    );
  } else if (data.notMaterialised) {
    body = (
      <div className="jp-empty mt-6">
        <p className="jp-empty__body text-body-sm text-text-primary">
          {t("criteria.newAds.notScanned")}
        </p>
        <div className="jp-empty__actions">
          <Link className="jp-btn jp-btn--primary" href={backHref}>
            {t("criteria.ads.backLink")}
          </Link>
        </div>
      </div>
    );
  } else if (data.rows.length === 0) {
    body = (
      <div className="jp-empty mt-6">
        <div className="jp-empty__title">{t("newAds.emptyTitle")}</div>
        <p className="jp-empty__body">{t("criteria.newAds.emptyBody")}</p>
        <div className="jp-empty__actions">
          <Link className="jp-btn jp-btn--primary" href={buildCriterionAdsHref(id, 1, "all")}>
            {t("criteria.ads.showAll")}
          </Link>
        </div>
      </div>
    );
  } else {
    // The server carries assessability; re-deriving it from the rows would force a choice between
    // `some` and `every`. Both numbers count the whole fetched set, never the filtered view.
    const assessed = data.matchingAssessed;
    const matchingRows = data.rows.filter((row) => row.matchesYou === true);
    const shownCount = data.rows.length;

    // Truncated, `rows.length` is the capped count, so the sentence states what is shown and claims
    // no total.
    const summary = data.truncated
      ? assessed
        ? t("newAds.summaryTruncated", { shown: shownCount, matching: matchingRows.length })
        : t("newAds.summaryTruncatedNotAssessed", { shown: shownCount })
      : assessed
        ? t("newAds.summary", { total: shownCount, matching: matchingRows.length })
        : t("newAds.summaryNotAssessed", { total: shownCount });

    body = (
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

        <p className="jp-transparency-note mt-2">
          <Info size={16} aria-hidden="true" />
          <span>
            {t("newAds.consumptionNote")}
            {/* Truncated, the remainder is newer than the window and returns next visit, so
                "only what has been added since" would contradict the note at the foot. */}
            {!data.truncated && <> {t("newAds.consumptionNoteAcknowledged")}</>}
          </span>
        </p>

        {/* The h1 names a kommun while the companies are chosen by their registered seat. */}
        <p className="jp-transparency-note mt-3">
          <Info size={16} aria-hidden="true" />
          <span>{t("criteria.ads.seatExplainer")}</span>
          <InfoDialog
            title={t("criteria.ads.seatHelpTitle")}
            paragraphs={[t("criteria.ads.seatHelpBody1"), t("criteria.ads.seatHelpBody2")]}
            ariaLabel={t("criteria.ads.seatHelpAria")}
          />
        </p>

        {!assessed && (
          <p className="jp-matchline">
            {tMatch("noStatedOccupation")}{" "}
            <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
              {tMatch("settingsCta")}
            </Link>
          </p>
        )}

        {assessed ? (
          <NewAdsViewSwitch
            groupLabel={t("newAds.view.groupLabel")}
            allLabel={t("newAds.view.all")}
            matchingLabel={t("newAds.view.matching")}
            filteredNote={tRule("filter.activeOnlyMatched")}
            emptyTitle={t("newAds.matchingEmptyTitle")}
            emptyAction={t("newAds.matchingEmptyAction")}
            matchingCount={matchingRows.length}
            all={<JobAdList jobAds={data.rows.map((row) => row.ad)} headingLevel={2} />}
            matching={<JobAdList jobAds={matchingRows.map((row) => row.ad)} headingLevel={2} />}
          />
        ) : (
          <div className="mt-4">
            <JobAdList jobAds={data.rows.map((row) => row.ad)} headingLevel={2} />
          </div>
        )}

        {data.truncated && (
          <p className="jp-transparency-note mt-4">
            <Info size={16} aria-hidden="true" />
            <span>{t("newAds.truncated")}</span>
          </p>
        )}
      </>
    );
  }

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{title}</h1>
            <p className="jp-pagehero__lede">{t("criteria.newAds.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <Link href={backHref} className="jp-backlink mb-4">
          <ArrowLeft size={16} aria-hidden="true" />
          {t("criteria.ads.backLink")}
        </Link>

        <CriterionBreadth
          sniCodes={criterion.sniCodes}
          municipalityCodes={criterion.municipalityCodes}
        />

        {body}

        {/* Mandatory source attribution (DPIA C-D2). */}
        <p className="mt-6 border-t border-border pt-4 text-body-sm text-text-primary">
          {t("criteria.ads.source")}
        </p>
      </div>
      <PageFeedback pageKey="industry-watches" />
    </>
  );
}

function ErrorShell({ title, body, backHref, backLabel }: {
  title: string;
  body: string;
  backHref: string;
  backLabel: string;
}) {
  return (
    <div className="jp-container jp-page">
      <Link href={backHref} className="jp-backlink mb-4">
        <ArrowLeft size={16} aria-hidden="true" />
        {backLabel}
      </Link>
      <div
        role="alert"
        className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
        <p className="text-body font-medium">{title}</p>
        <p className="mt-1 text-body-sm">{body}</p>
      </div>
    </div>
  );
}
