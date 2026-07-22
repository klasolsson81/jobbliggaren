import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import {
  browseCriterionCompanies,
  getCompanyWatchCriteria,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { CompanyBrowseList } from "@/components/company-criteria/company-browse-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";

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
  const [browseResult, criteriaResult, referenceResult] = await Promise.all([
    browseCriterionCompanies(id, page),
    getCompanyWatchCriteria(),
    getCriterionReference(),
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
          className="mb-4 inline-flex items-center gap-1.5 text-text-primary hover:underline"
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("browse.backLink")}
        </Link>

        <h2 className="text-h2 text-text-primary tabular-nums">
          {t("browse.magnitudeHeadline", { count: magnitudeText })}
        </h2>

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
            <p className="text-body-sm text-text-primary">{t("browse.emptyBody")}</p>
          </div>
        ) : (
          <div className="mt-6 flex flex-col gap-4">
            <CompanyBrowseList items={companies.items} reference={reference} />
            <JobAdPagination
              page={companies.page}
              pageSize={companies.pageSize}
              totalCount={companies.totalCount}
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
