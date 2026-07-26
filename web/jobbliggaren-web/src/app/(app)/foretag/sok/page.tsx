import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCriterionReference } from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import { ForetagSokResults } from "@/components/company-criteria/foretag-sok-results";
import { ForetagSokResultsSkeleton } from "@/components/company-criteria/foretag-sok-results-skeleton";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import {
  toStringList,
  parseNamn,
  parseSida,
  normalizeCodes,
  buildOrgNrRefusedHref,
  parseOrgNrRefused,
  ORG_NR_REFUSED_PARAM,
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

  // The org.nr gate (ADR 0087 D8(c); CTO bind 2026-07-26) runs BEFORE anything else this request
  // does. A ten-digit `?namn=` arrives here from every client state the browser guard cannot cover:
  // JS disabled, Enter before hydration, or a hand-typed/shared link with no form involved at all.
  // Redirecting here rather than rendering a refusal in place is what closes the channels D8(c)
  // names: the address bar settles on the washed URL, so it is the washed URL that enters history
  // and any re-share — and because `ForetagSokResults` never runs, the value never reaches
  // `body.name` either. The one channel this does NOT close is the access log of the request that
  // already carried it; that is irreducible in-process and accepted, on the record.
  const parsedNamn = parseNamn(params.namn);
  if (parsedNamn.kind === "orgNrShaped") {
    redirect(
      buildOrgNrRefusedHref({
        namn: "",
        // Reference-free normalisation (dedupe + cap): the SCB reference is deliberately NOT fetched
        // on a request that is about to redirect, and the reference-based drop-unknown applies on
        // the next render anyway.
        sni: normalizeCodes(toStringList(params.sni), MAX_SNI_CODES),
        kommun: normalizeCodes(toStringList(params.kommun), MAX_MUNICIPALITY_CODES),
      }),
    );
  }

  const referenceResult = await getCriterionReference();
  const referenceOk = referenceResult.kind === "ok";
  const reference = referenceOk ? referenceResult.data : EMPTY_REFERENCE;

  // Dynamic allowlists for drop-unknown; undefined when the reference degraded (dedupe/cap only).
  const sniAllowed = referenceOk ? collectSniLeafCodes(reference) : undefined;
  const kommunAllowed = referenceOk ? collectKommunCodes(reference) : undefined;

  const namn = parsedNamn.value;
  const orgNrRefused = parseOrgNrRefused(params[ORG_NR_REFUSED_PARAM]);
  const sni = normalizeCodes(toStringList(params.sni), MAX_SNI_CODES, sniAllowed);
  const kommun = normalizeCodes(
    toStringList(params.kommun),
    MAX_MUNICIPALITY_CODES,
    kommunAllowed,
  );
  const page = parseSida(params.sida);

  // The active-filter signature (name + sorted axes) plus the page: re-triggers the results skeleton
  // whenever the applied search changes (org.nr is outside this boundary — it answers in client state).
  const suspenseKey =
    `${namn}|${[...sni].sort().join(",")}|${[...kommun].sort().join(",")}|${page}`;

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
        {/* The refusal, explained rather than washed silently. A silent wash would answer a specific
            typed query with the ENTIRE register, which does not read as "we refused" — it reads as
            "your search matched everything", and it is the same silent-drop class the search island
            was built to eliminate (its own docblock records that Blocker).
            Deliberately: it never echoes the value (echoing it back into the DOM would defeat the
            wash), and it never says "personnummer" — the gate fires on the whole ten-digit class, so
            that word would be factually wrong for a legitimate company org.nr and would advertise
            the heuristic besides. Server-rendered on a fresh document, so no aria-live and no
            role="alert": nothing failed dangerously, and it is ordinary content in reading order. */}
        {orgNrRefused && (
          <div className="mt-6 rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700">
            <p className="text-body font-medium">{t("orgNrUrlRefusedTitle")}</p>
            <p className="mt-1 text-body-sm">{t("orgNrUrlRefusedBody")}</p>
          </div>
        )}
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
