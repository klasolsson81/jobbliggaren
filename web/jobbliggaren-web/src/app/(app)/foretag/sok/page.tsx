import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCriterionReference } from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import { ForetagSokFollowAll } from "@/components/company-criteria/foretag-sok-follow-all";
import { ForetagSokResults } from "@/components/company-criteria/foretag-sok-results";
import { ForetagSokResultsSkeleton } from "@/components/company-criteria/foretag-sok-results-skeleton";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import {
  toStringList,
  parseNamn,
  parseSida,
  normalizeCodes,
  MAX_SNI_CODES,
  MAX_MUNICIPALITY_CODES,
} from "@/lib/company-search/search-params";

const EMPTY_REFERENCE: CriterionReference = {
  sniVersion: "",
  kommunVersion: "",
  sni: [],
  lan: [],
};

interface PageProps {
  // Next.js 16 App Router: searchParams is a Promise (async dynamic API).
  searchParams: Promise<{ [key: string]: string | string[] | undefined }>;
}

/**
 * #560 PR-B / #997 (S2) — the general company-register search (`/foretag/sok`), the /jobb architecture:
 * searchParams → typed state → a POST-as-read fetch → Suspense-streamed results. A surface of the
 * `/foretag` sub-nav (S1). The shareable axes (name prefix + SNI + kommun + page) live in the URL. The
 * name prefix, the org.nr lookup, and the bransch/ort filters share ONE draft island
 * (`ForetagSokSearchbar`, #997) with ONE submit: a field value that normalises to 10 digits is an org.nr
 * (client POST, refuse pnr locally, NEVER the URL — D8(c)); anything else is a name prefix + bransch + ort
 * committed to the URL together. Empty filters browse the whole register (Klas bind: browse-all default).
 *
 * Drop-unknown discipline (parity /jobb's matchGrades): unknown SNI/kommun codes in a manipulated URL
 * are filtered against the SCB reference leaf-set rather than 400-ing the query. A degraded reference
 * (no allowlist) passes deduped/capped codes through — the backend is the last barrier.
 */
export default async function ForetagSokPage({ searchParams }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages.foretag.sok");
  const params = await searchParams;

  const referenceResult = await getCriterionReference();
  const referenceOk = referenceResult.kind === "ok";
  const reference = referenceOk ? referenceResult.data : EMPTY_REFERENCE;

  // Dynamic allowlists for drop-unknown; undefined when the reference degraded (dedupe/cap only).
  const sniAllowed = referenceOk ? collectSniLeafCodes(reference) : undefined;
  const kommunAllowed = referenceOk ? collectKommunCodes(reference) : undefined;

  const namn = parseNamn(params.namn);
  const sni = normalizeCodes(toStringList(params.sni), MAX_SNI_CODES, sniAllowed);
  const kommun = normalizeCodes(
    toStringList(params.kommun),
    MAX_MUNICIPALITY_CODES,
    kommunAllowed,
  );
  const page = parseSida(params.sida);

  // The active-filter signature (name + sorted axes), page-independent. Keys the follow-all CTA so a
  // filter change remounts it (clearing any saved/error state); the results skeleton also re-triggers
  // on it plus the page (org.nr is outside this boundary).
  const filterKey = `${namn}|${[...sni].sort().join(",")}|${[...kommun].sort().join(",")}`;
  const suspenseKey = `${filterKey}|${page}`;

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("title")}</h1>
            <p className="jp-pagehero__lede">{t("lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <ForetagSubnav active="sok" />
        {/* #997 (S2) — ONE shared-draft search island: a company name OR an org.nr in the unified field
            (org.nr → client POST, refuse pnr locally, never the URL — D8(c); its result carries a Bevaka),
            plus the single-select bransch typeahead + the multi-select ort cascade. One submit commits
            name + bransch + ort together (no silent draft drop — the former two-island split could drop
            an edit). Replaces the former ForetagSokSearch + ForetagSokFilters. */}
        <ForetagSokSearchbar
          reference={reference}
          referenceOk={referenceOk}
          namn={namn}
          sni={sni}
          kommun={kommun}
        />
        {/* #560 PR-D — save the active filter as a criterion watch. Keyed on the filter signature so
            a filter change remounts it (clearing any saved/error state). Outside the Suspense
            boundary: it depends only on the URL filter, so it renders instantly while results
            stream, and disabled-with-explainer whenever the filter is not criterion-shaped. */}
        <ForetagSokFollowAll key={filterKey} namn={namn} sni={sni} kommun={kommun} />
        <Suspense key={suspenseKey} fallback={<ForetagSokResultsSkeleton />}>
          <ForetagSokResults
            namn={namn}
            sni={sni}
            kommun={kommun}
            page={page}
            reference={reference}
          />
        </Suspense>
      </div>
    </>
  );
}

/** All SNI leaf codes across the reference tree (the drop-unknown allowlist for the sni axis). */
function collectSniLeafCodes(reference: CriterionReference): Set<string> {
  const codes = new Set<string>();
  for (const section of reference.sni) {
    for (const division of section.divisions) {
      for (const leaf of division.leaves) codes.add(leaf.code);
    }
  }
  return codes;
}

/** All kommun codes across the reference tree (the drop-unknown allowlist for the kommun axis). */
function collectKommunCodes(reference: CriterionReference): Set<string> {
  const codes = new Set<string>();
  for (const lan of reference.lan) {
    for (const kommun of lan.kommuner) codes.add(kommun.code);
  }
  return codes;
}
