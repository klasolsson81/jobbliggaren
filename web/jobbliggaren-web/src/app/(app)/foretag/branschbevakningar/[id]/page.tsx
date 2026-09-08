import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import {
  browseCriterionCompanies,
  getCriterionAdCount,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { CompanyBrowseList } from "@/components/company-criteria/company-browse-list";
import { CriterionAdLines } from "@/components/company-criteria/criterion-ad-lines";
import { CriterionBreadth } from "@/components/company-criteria/criterion-breadth";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";
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
  const format = await getFormatter();

  const { id } = await params;
  const { page: pageParam } = await searchParams;
  const page = parsePageParam(pageParam);

  // The browse read is the authority on existence (404 → notFound). The criteria list + reference are
  // fetched to resolve the human title; if either degrades, the title falls back rather than failing
  // the page.
  // #1681 part 2 — the criteria LIST read is gone from this page. It was only ever here to resolve
  // the heading, and part 2 made every row of that list carry a materialised ad count plus a
  // per-user graded matching count — so this page was fetching twenty criteria's graded counts to
  // render one string. The browse response now carries the criterion's own codes and label.
  const [browseResult, referenceResult, adCountResult] = await Promise.all([
    browseCriterionCompanies(id, page),
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

  const { companies, magnitude, criterion } = browseResult.data;
  const reference = referenceResult.kind === "ok" ? referenceResult.data : EMPTY_REFERENCE;

  // Resolve the human title from the owner's criterion (label, else derived, else a neutral
  // fallback). The criterion arrives on the browse response itself, which is also this route's
  // authority on existence — so there is no second read to degrade independently.
  const userLabel = criterion.label?.trim() ?? "";
  const derived = deriveDisplayLabel(
    criterion.sniCodes, criterion.municipalityCodes, reference, {
      moreSuffix: t("moreSuffix"),
      separator: " · ",
    });
  const title = userLabel.length > 0 ? userLabel : (derived ?? t("row.untitled"));

  const magnitudeText = formatMagnitude(format, magnitude);

  // #1681 part 2 — narrowed once, here, rather than re-asserted inside the JSX. `ads.magnitude` is
  // non-null only when the criterion was actually counted; its two no-number states are `tooBroad`
  // and `notMaterialised`, which the render branches on before ever reaching the number. Narrowing
  // in a local keeps the render free of non-null assertions, which would be claims the type system
  // cannot check.
  // A degraded ad-count read yields no personal count either: the two numbers arrive in one
  // response, so there is nothing to say about matching that the "cannot be shown" line above does
  // not already say.
  const matching = adCountResult.kind === "ok" ? adCountResult.data.matching : null;
  const ads = adCountResult.kind === "ok" ? adCountResult.data.ads : null;

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
          href="/foretag/branschbevakningar"
          className="jp-backlink mb-4"
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("browse.backLink")}
        </Link>

        {/* How wide this watch is. In the CONTENT COLUMN, never on the hero plate: the plate carries
            the page's identity (kicker/title/lede/aside) and a record descriptor belongs beneath it
            — the `.jp-cv-meta` precedent (design-reviewer B-3, 2026-09-08). It earns its place here
            rather than by symmetry with /oversikt: this page states the too-broad refusal and tells
            the user to narrow the watch, and until now the size she is being asked to narrow
            appeared nowhere on the page (ADR 0047). Reading order is definition → outcome, so it
            sits above the magnitude rather than beneath it. */}
        <CriterionBreadth
          sniCodes={criterion.sniCodes}
          municipalityCodes={criterion.municipalityCodes}
        />

        {/* `mt-2` and not a margin on the line above: `.jp-criterion-breadth` sets `margin: 0`, so
            spacing is the neighbours' to own. 16px above (the backlink's own `mb-4`) and 8px below
            binds the line to this headline rather than to the backlink. */}
        <h2 className="mt-2 text-h2 text-text-primary tabular-nums">
          {t("browse.magnitudeHeadline", { count: magnitudeText })}
        </h2>

        {/* #1559 / #1681 part 2 — the criterion's two ad numbers and every honest way of not
            having them. The ladder moved to `CriterionAdLines` for #1681 part 3 (senior-cto-advisor,
            in-block requirement 1): `/oversikt` renders the SAME numbers, and a copy of the honesty
            logic is how two surfaces come to disagree about one watch. This page and the overview
            now read one component, which is what ADR 0139's "Båda ytorna läser samma källa" asks of
            the rendering as well as of the source.

            The ONLY destination this criterion's ads have: /jobb has no SNI axis, its ?employer=
            producer refuses above 400 org.nrs, and its municipality axis is the ad's WORKPLACE while
            this kommun is the company's registered SEAT. */}
        {/* This page is one criterion by construction, so nothing above the row states the advice
            and the row carries it whole — the same reason `CriteriaSummary` passes `false` at N=1. */}
        <CriterionAdLines
          criterionId={id}
          ads={ads}
          matching={matching}
          variant="withCompanies"
          adviceStatedByCaller={false}
          /* No edit control on this page, so the too-broad CTA is the only way to the surface that
             has one and must render. */
          actionOfferedByCaller={false}
        />

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
                  ? `/foretag/branschbevakningar/${id}`
                  : `/foretag/branschbevakningar/${id}?page=${targetPage}`
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
