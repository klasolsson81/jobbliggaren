import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import {
  browseCriterionCompanies,
  getCompanyWatchCriteria,
  getCriterionAdCount,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { CompanyBrowseList } from "@/components/company-criteria/company-browse-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

/**
 * The title resolves against the record's ABSENCE: a missing record must not serve this
 * route's title over a "Sidan finns inte" body, and `(app)/not-found.tsx` cannot correct
 * that (`lib/metadata/not-found-title.ts` records why). The gate is `kind === "notFound"`
 * and nothing else — both halves are pinned by
 * `(app)/detail-route-not-found-title.test.ts`.
 */
export async function generateMetadata({ params, searchParams }: Props): Promise<Metadata> {
  const { id } = await params;
  const { page: pageParam } = await searchParams;
  const result = await browseCriterionCompanies(id, parsePageParam(pageParam));
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("foretag.smartaBevakningar.detail.meta.title") };
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
 * #560 PR-3 — the criterion "run": the ACTIVE register companies a saved criterion matches. RSC,
 * jp-pagehero standard. The headline uses the HONEST magnitude (exact, or "10 000+" when saturated) —
 * never the pagination `totalCount` (capped at 2000). The kommun column is the company's REGISTERED
 * SEAT (säteskommun); a mandatory help affordance says so. A source-attribution line ("Källa: SCB, egen
 * bearbetning") is mandatory on this surface (DPIA C-D2/M-C4).
 *
 * 404 (unknown OR another user's id — never an enumeration oracle) → notFound(). unauthorized →
 * /logga-in. rateLimited/error → civic notice.
 */
export default async function BevakningBrowsePage({ params, searchParams }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages.foretag.criteria");
  // Klas 2026-09-05: the personal count works "på samma sätt som vanlig företagsbevakning", so it
  // reuses that surface's own sentences rather than minting a second vocabulary for one question.
  // `matchNudge` promises "matchande annonser" and this page keeps that promise: its count links to
  // the filtered view. (Arm (a)'s ads page could not, which is why it uses /jobb's wording instead.)
  const tWatch = await getTranslations("jobads.companyWatches");
  const format = await getFormatter();

  const { id } = await params;
  const { page: pageParam } = await searchParams;
  const page = parsePageParam(pageParam);

  // The browse read is the authority on existence (404 → notFound). The criteria list + reference are
  // fetched to resolve the human title; if either degrades, the title falls back rather than failing
  // the page.
  const [browseResult, criteriaResult, referenceResult, adCountResult] = await Promise.all([
    browseCriterionCompanies(id, page),
    getCompanyWatchCriteria(),
    getCriterionReference(),
    // #1559 — the ad dimension. A degraded read must not fail the page: the company browse is this
    // route's authority on existence, so a failed ad count renders a civil "cannot be shown" line
    // and never a false 0 (the same posture as a degraded reference tree above).
    getCriterionAdCount(id),
  ]);

  switch (browseResult.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return <ErrorShell title={t("browse.loadErrorTitle")} body={t("browse.rateLimited")} />;
    case "forbidden":
    case "error":
      return <ErrorShell title={t("browse.loadErrorTitle")} body={t("browse.loadErrorBody")} />;
  }

  const { companies, magnitude } = browseResult.data;
  const reference = referenceResult.kind === "ok" ? referenceResult.data : EMPTY_REFERENCE;

  // Resolve the human title from the owner's criterion (label, else derived, else a neutral fallback).
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

  // #1681 part 2 — narrowed once, here, rather than re-asserted inside the JSX. `ads.magnitude` is
  // non-null only when the criterion was actually counted; its two no-number states are `tooBroad`
  // and `notMaterialised`, which the render branches on before ever reaching the number. Narrowing
  // in a local keeps the render free of non-null assertions, which would be claims the type system
  // cannot check.
  const ads = adCountResult.kind === "ok" ? adCountResult.data.ads : null;
  const adsCount = ads?.magnitude ?? null;
  const adsCountText =
    ads !== null && ads.magnitude !== null
      ? formatMagnitude(format, { magnitude: ads.magnitude, saturated: ads.saturated })
      : null;

  // A degraded ad-count read yields no personal count either: the two numbers arrive in one
  // response, so there is nothing to say about matching that the "cannot be shown" line above does
  // not already say.
  const matching = adCountResult.kind === "ok" ? adCountResult.data.matching : null;

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{title}</h1>
            <p className="jp-pagehero__lede">{t("browse.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <Link
          href="/foretag/smarta-bevakningar"
          className="jp-backlink mb-4"
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("browse.backLink")}
        </Link>

        <h2 className="text-h2 text-text-primary tabular-nums">
          {t("browse.magnitudeHeadline", { count: magnitudeText })}
        </h2>

        {/* #1559 — the ad dimension of the same criterion, and the ONLY destination its ads have:
            /jobb has no SNI axis, its ?employer= producer refuses above 400 org.nrs, and its
            municipality axis is the ad's WORKPLACE while this kommun is the company's registered
            SEAT. The number is a link exactly when there is something to look at; a 0 states the
            fact without offering an empty page, and a degraded read says so rather than showing a
            false 0 (#859 — a rendered magnitude is true or absent). */}
        {/* #1681 part 2 (ADR 0139) — the ad numbers are read from a MATERIALISED company set, so
            "no number" now has three different reasons and each gets its own sentence. A 0 is only
            ever rendered for a criterion we actually counted; "för bred" is a refusal the user can
            act on; "räknas fram inom kort" is ignorance that resolves itself. Collapsing any of
            them into a zero is the defect ADR 0120 exists against. The order matters: the two
            no-number states are checked BEFORE the `> 0` test, because `magnitude` is null in both
            and `null > 0` is false — which would silently render "inga aktiva annonser". */}
        {ads === null ? (
          <p className="jp-matchline">{t("ads.countUnavailable")}</p>
        ) : ads.tooBroad ? (
          /* Reads as one statement with the company magnitude above it: "N företag matchar din
             bevakning" followed by "för bred för att räkna annonserna". The company count is the
             EVIDENCE for the refusal, not a number contradicting it. */
          <p className="jp-matchline">{t("ads.adsTooBroad")}</p>
        ) : ads.notMaterialised ? (
          <p className="jp-matchline">{t("ads.adsNotMaterialised")}</p>
        ) : adsCount !== null && adsCount > 0 && adsCountText !== null ? (
          <p className="jp-matchline tabular-nums">
            <Link className="jp-countlink" href={buildCriterionAdsHref(id, 1, "all")}>
              {t("ads.linkLabel", { count: adsCountText })}
            </Link>
          </p>
        ) : (
          <p className="jp-matchline">{t("ads.none")}</p>
        )}

        {/* #1656 (b) — the PERSONAL count, in the same form the ordinary company watch renders it
            (`company-watch-row.tsx`). Four states and none of them collapses into another: a number
            (0 included), "you have stated no occupation", and "this watch is too broad to grade".
            The last two are NOT zeros — a 0 would read as "nothing matches you" when the truth is
            that nothing was measured. A degraded ad-count read renders neither, because the line
            above already says the numbers cannot be shown. */}
        {matching !== null &&
          (matching.tooBroad ? (
            <p className="jp-matchline">
              {t("ads.matchingTooBroad")}{" "}
              <Link className="jp-nudgelink" href="/foretag/smarta-bevakningar">
                {t("ads.matchingTooBroadCta")}
              </Link>
            </p>
          ) : matching.notMaterialised ? (
            /* #1681 part 2 — deliberately WITHOUT the "ändra bevakningen" call to action the
               too-broad arm carries. Nothing is wrong with this watch; it simply has not been
               counted yet, and inviting the user to edit it would be advice that cannot help. */
            <p className="jp-matchline">{t("ads.matchingNotMaterialised")}</p>
          ) : matching.count === null ? (
            <p className="jp-matchline">
              {tWatch("matchNudge")}{" "}
              <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
                {tWatch("matchNudgeCta")}
              </Link>
            </p>
          ) : (
            <p className="jp-matchline tabular-nums">
              {matching.count > 0 ? (
                <Link
                  className="jp-countlink"
                  href={buildCriterionAdsHref(id, 1, "matching")}
                  prefetch={false}
                >
                  {tWatch("matchingAds", { count: matching.count })}
                </Link>
              ) : (
                tWatch("matchingAds", { count: matching.count })
              )}
            </p>
          ))}

        {/* Mandatory säteskommun explainer + inline help (the kommun is the registered seat, not
            necessarily where the company operates). */}
        <p className="mt-2 flex items-center gap-1 text-body-sm text-text-primary">
          {t("browse.seatExplainer")}
          <InfoDialog
            title={t("browse.seatHelpTitle")}
            paragraphs={[t("browse.seatHelpBody1"), t("browse.seatHelpBody2")]}
            ariaLabel={t("browse.seatHelpAria")}
          />
        </p>

        {companies.items.length === 0 ? (
          <div className="jp-empty mt-6">
            <div className="jp-empty__title">{t("browse.emptyTitle")}</div>
            <p className="jp-empty__body text-body-sm text-text-primary">{t("browse.emptyBody")}</p>
          </div>
        ) : (
          <div className="mt-6 flex flex-col gap-4">
            <CompanyBrowseList items={companies.items} reference={reference} />
            <JobAdPagination
              page={companies.page}
              pageSize={companies.pageSize}
              totalCount={companies.totalCount}
              // #1149 — same reason as `/foretag/sok`: this `totalCount` saturates at
              // CompanyBrowseCriteria.MaxServableRows, so a criterion matching more companies than
              // the cap would read "(2 000 träffar totalt)" beside a headline that honestly says
              // "10 000+". The magnitude above is this surface's number.
              showTotalCount={false}
              buildHref={(targetPage) =>
                targetPage <= 1
                  ? `/foretag/smarta-bevakningar/${id}`
                  : `/foretag/smarta-bevakningar/${id}?page=${targetPage}`
              }
            />
          </div>
        )}

        {/* Mandatory source attribution (DPIA C-D2/M-C4). */}
        <p className="mt-6 border-t border-border pt-4 text-body-sm text-text-primary">
          {t("browse.source")}
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
      {/* role="alert" — parity with /foretag's renderSection error notice (design-review Minor 2). */}
      <div
        role="alert"
        className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
        <p className="text-body font-medium">{title}</p>
        <p className="mt-1 text-body-sm">{body}</p>
      </div>
    </div>
  );
}
