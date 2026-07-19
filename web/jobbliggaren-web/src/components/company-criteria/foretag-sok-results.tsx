import { redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { CompanyBrowseList } from "./company-browse-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";
import { searchCompanies } from "@/lib/api/company-search";
import { getCompanyWatchStatusByOrgNr } from "@/lib/api/company-follows";
import { buildPageHref, PAGE_SIZE } from "@/lib/company-search/search-params";
import type { CriterionReference } from "@/lib/dto/company-criteria";

interface ForetagSokResultsProps {
  readonly namn: string;
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
  readonly page: number;
  readonly reference: CriterionReference;
}

/**
 * #560 PR-B — the async results region of `/foretag/sok`, Suspense-streamed under the page. Mirrors the
 * criterion-browse body (`bevakningar/[id]`): the honest magnitude headline ("10 000+" when saturated,
 * NEVER the pagination `totalCount`), a mandatory säteskommun explainer, the register table, pagination
 * that preserves the active filter, and the mandatory source attribution (DPIA C-D2/M-C4). An empty
 * filter browses the whole register (Klas bind: browse-all default); a zero-match filter shows the
 * empty state.
 */
export async function ForetagSokResults({
  namn,
  sni,
  kommun,
  page,
  reference,
}: ForetagSokResultsProps) {
  const t = await getTranslations("pages.foretag.sok");
  const format = await getFormatter();

  const result = await searchCompanies({
    name: namn,
    sniCodes: sni,
    municipalityCodes: kommun,
    page,
    pageSize: PAGE_SIZE,
  });

  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return <ErrorShell title={t("loadErrorTitle")} body={t("rateLimited")} />;
    case "notFound":
    case "forbidden":
    case "error":
      return <ErrorShell title={t("loadErrorTitle")} body={t("loadErrorBody")} />;
  }

  const { companies, magnitude } = result.data;
  const magnitudeText = formatMagnitude(format, magnitude);
  const hasFilter =
    namn.length > 0 || sni.length > 0 || kommun.length > 0;
  const filterState = { namn, sni, kommun };

  // #560 PR-C — follow-state overlay for the "Bevaka"-per-row affordance. A SEPARATE company_watches read
  // composed at the RSC edge (never a server-side join against the firewalled register — DPIA C-D4/M-C5),
  // sequenced after the search since its input is the search rows' own org.nrs. Masked/sole-prop rows
  // carry no org.nr key → excluded (non-followable); an empty/all-masked page skips the request entirely.
  const followableOrgNrs = companies.items.flatMap((company) =>
    company.organizationNumber !== null && !company.isProtectedIdentity
      ? [company.organizationNumber]
      : []
  );
  const followStatuses = await getCompanyWatchStatusByOrgNr(followableOrgNrs);
  const followStateByOrgNr = new Map<string, string | null>();
  followableOrgNrs.forEach((orgNr, i) => {
    followStateByOrgNr.set(orgNr, followStatuses[i]?.companyWatchId ?? null);
  });

  return (
    <div className="mt-8">
      {companies.items.length === 0 ? (
        // Empty state carries the statement + next step; the magnitude headline + seat explainer are
        // suppressed here so a zero-match search does not double the "no companies" message (they
        // reference a table that is not shown).
        <div className="jp-empty">
          <div className="jp-empty__title">{t("emptyTitle")}</div>
          <p className="text-body-sm text-text-primary">{t("emptyBody")}</p>
        </div>
      ) : (
        <>
          <h2 className="text-h2 text-text-primary tabular-nums">
            {hasFilter
              ? t("magnitudeHeadlineFiltered", { count: magnitudeText })
              : t("magnitudeHeadlineAll", { count: magnitudeText })}
          </h2>

          {/* Mandatory säteskommun explainer + inline help (the kommun is the registered seat, not
              necessarily where the company operates). */}
          <p className="mt-2 flex items-center gap-1 text-body-sm text-text-primary">
            {t("seatExplainer")}
            <InfoDialog
              title={t("seatHelpTitle")}
              paragraphs={[t("seatHelpBody1"), t("seatHelpBody2")]}
              ariaLabel={t("seatHelpAria")}
            />
          </p>

          <div className="mt-6 flex flex-col gap-4">
            <CompanyBrowseList
              items={companies.items}
              reference={reference}
              followStateByOrgNr={followStateByOrgNr}
            />
            <JobAdPagination
              page={companies.page}
              pageSize={companies.pageSize}
              totalCount={companies.totalCount}
              buildHref={(targetPage) => buildPageHref(filterState, targetPage)}
            />
          </div>
        </>
      )}

      {/* Mandatory source attribution (DPIA C-D2/M-C4). */}
      <p className="mt-6 border-t border-border pt-4 text-body-sm text-text-primary">
        {t("source")}
      </p>
    </div>
  );
}

function ErrorShell({ title, body }: { title: string; body: string }) {
  return (
    <div
      role="alert"
      className="mt-8 rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700"
    >
      <p className="text-body font-medium">{title}</p>
      <p className="mt-1 text-body-sm">{body}</p>
    </div>
  );
}
