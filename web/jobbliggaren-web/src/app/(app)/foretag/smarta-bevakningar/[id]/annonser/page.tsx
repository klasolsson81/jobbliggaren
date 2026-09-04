import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import {
  browseCriterionAds,
  getCompanyWatchCriteria,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

/**
 * The title resolves against the record's ABSENCE, exactly as the parent route's does — a missing
 * criterion must not serve this route's title over a "Sidan finns inte" body, and
 * `(app)/not-found.tsx` cannot correct that. The gate is `kind === "notFound"` and nothing else.
 */
export async function generateMetadata({ params, searchParams }: Props): Promise<Metadata> {
  const { id } = await params;
  const { page: pageParam } = await searchParams;
  const result = await browseCriterionAds(id, parsePageParam(pageParam));
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("foretag.smartaBevakningar.ads.meta.title") };
}

const EMPTY_REFERENCE: CriterionReference = {
  sniVersion: "",
  kommunVersion: "",
  sni: [],
  lan: [],
};

interface Props {
  // Next.js 16 App Router: params and searchParams are Promises (async dynamic APIs).
  params: Promise<{ id: string }>;
  searchParams: Promise<{ [key: string]: string | string[] | undefined }>;
}

/**
 * `/foretag/smarta-bevakningar/[id]/annonser` (#1559) — the ACTIVE job ads posted by the companies a
 * saved criterion matches. RSC, jp-pagehero standard, the exact structural sibling of the parent
 * route (which lists the COMPANIES) and of `/foretag/bevakade/nya` (#1576, the ads behind the
 * Översikt number).
 *
 * <para/> **Why this is a route and not a `/jobb` link.** Klas asked for "en länk som visar
 * annonserna" (#1559) and `/jobb` cannot express this set: it has no SNI axis at all; its only
 * company axis is `?employer=`, whose producer refuses above `MAX_CONCEPT_IDS` = 400 org.nrs on an
 * every-value-or-none doctrine, against a criterion that matched 3 981 companies in the measured
 * case; and its `municipality` axis is the AD's workplace while a criterion's kommun is the
 * company's REGISTERED SEAT. Every link buildable from those axes is partial or false, so the
 * criterion's own id is the destination and no new `/jobb` axis is minted
 * (senior-cto-advisor 2026-09-04).
 *
 * <para/> **The seat explainer is mandatory here, not decorative.** The number above it is true, but
 * without the explainer it carries a false implicature — that these are jobs IN the watched
 * kommuner. They are jobs at companies SEATED there. A true number under a false implicature is the
 * same defect as a false number.
 *
 * <para/> 404 (unknown OR another user's id — never an enumeration oracle) → notFound().
 * unauthorized → /logga-in. rateLimited/error → civic notice.
 */
export default async function BevakningAdsPage({ params, searchParams }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages.foretag.criteria");
  const format = await getFormatter();

  const { id } = await params;
  const { page: pageParam } = await searchParams;
  const page = parsePageParam(pageParam);

  // The ad browse is this route's authority on existence (404 → notFound). The criteria list +
  // reference resolve the human title only; a degraded read of either falls back to a neutral title
  // rather than failing the page — parity with the parent route.
  const [adsResult, criteriaResult, referenceResult] = await Promise.all([
    browseCriterionAds(id, page),
    getCompanyWatchCriteria(),
    getCriterionReference(),
  ]);

  switch (adsResult.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return <ErrorShell title={t("ads.loadErrorTitle")} body={t("ads.rateLimited")} />;
    case "forbidden":
    case "error":
      return <ErrorShell title={t("ads.loadErrorTitle")} body={t("ads.loadErrorBody")} />;
  }

  const { ads, magnitude } = adsResult.data;
  const reference = referenceResult.kind === "ok" ? referenceResult.data : EMPTY_REFERENCE;

  const criterion =
    criteriaResult.kind === "ok"
      ? criteriaResult.data.find((c) => c.id === id)
      : undefined;
  const userLabel = criterion?.label?.trim() ?? "";
  const derived = criterion
    ? deriveDisplayLabel(criterion.sniCodes, criterion.municipalityCodes, reference, {
        moreSuffix: t("moreSuffix"),
        separator: " · ",
      })
    : null;
  const title = userLabel.length > 0 ? userLabel : (derived ?? t("row.untitled"));

  const magnitudeText = formatMagnitude(format, magnitude);

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{title}</h1>
            <p className="jp-pagehero__lede">{t("ads.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <Link href={`/foretag/smarta-bevakningar/${id}`} className="jp-backlink mb-4">
          <ArrowLeft size={16} aria-hidden="true" />
          {t("ads.backLink")}
        </Link>

        <h2 className="text-h2 text-text-primary tabular-nums">
          {t("ads.magnitudeHeadline", { count: magnitudeText })}
        </h2>

        {/* Mandatory selection explainer: the ads are chosen by the COMPANY's registered seat, never
            by the ad's workplace. See the route docblock — this closes the implicature the headline
            would otherwise carry. */}
        <p className="mt-2 flex items-center gap-1 text-body-sm text-text-primary">
          {t("ads.seatExplainer")}
          <InfoDialog
            title={t("ads.seatHelpTitle")}
            paragraphs={[t("ads.seatHelpBody1"), t("ads.seatHelpBody2")]}
            ariaLabel={t("ads.seatHelpAria")}
          />
        </p>

        {ads.items.length === 0 ? (
          <div className="jp-empty mt-6">
            <div className="jp-empty__title">{t("ads.emptyTitle")}</div>
            <p className="jp-empty__body text-body-sm text-text-primary">{t("ads.emptyBody")}</p>
          </div>
        ) : (
          <div className="mt-6 flex flex-col gap-4">
            <JobAdList jobAds={ads.items} />
            <JobAdPagination
              page={ads.page}
              pageSize={ads.pageSize}
              totalCount={ads.totalCount}
              // #1149's precedent, and the same reason as the sibling company browse: this
              // `totalCount` saturates at the pagination cap, so rendering it beside a headline that
              // honestly says "10 000+" would put two disagreeing numbers on one screen. The
              // magnitude above is this surface's number.
              showTotalCount={false}
              buildHref={(targetPage) =>
                targetPage <= 1
                  ? `/foretag/smarta-bevakningar/${id}/annonser`
                  : `/foretag/smarta-bevakningar/${id}/annonser?page=${targetPage}`
              }
            />
          </div>
        )}

        {/* Mandatory source attribution (DPIA C-D2/M-C4) — the SELECTION is register-derived even
            though the ads themselves are Platsbanken's. */}
        <p className="mt-6 border-t border-border pt-4 text-body-sm text-text-primary">
          {t("ads.source")}
        </p>
      </div>
    </>
  );
}

/** Parse a `?page=` search param to a positive integer, defaulting to 1. */
function parsePageParam(raw: string | string[] | undefined): number {
  const value = typeof raw === "string" ? Number.parseInt(raw, 10) : NaN;
  return Number.isInteger(value) && value > 0 ? value : 1;
}

function ErrorShell({ title, body }: { title: string; body: string }) {
  return (
    <div className="jp-container jp-page">
      {/* role="alert" — parity with the sibling routes' civic notice. */}
      <div
        role="alert"
        className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
        <p className="text-body font-medium">{title}</p>
        <p className="mt-1 text-body-sm">{body}</p>
      </div>
    </div>
  );
}
