import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getCriterionReference } from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import { ForetagSokResults } from "@/components/company-criteria/foretag-sok-results";
import { ForetagSokResultsSkeleton } from "@/components/company-criteria/foretag-sok-results-skeleton";
import { Announcer } from "@/components/common/announcer";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import {
  parseCodeAxis,
  parseNamn,
  parseSida,
  normalizeCodes,
  buildOrgNrRefusedHref,
  parseOrgNrRefused,
  ORG_NR_REFUSED_PARAM,
  MAX_SNI_CODES,
  MAX_MUNICIPALITY_CODES,
} from "@/lib/company-search/search-params";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("foretag.sok.meta.title") };
}

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
 * name prefix, the org.nr lookup, and the bransch/ort filters share ONE island
 * (`ForetagSokSearchbar`, #997). The bransch/ort filters COMMIT LIVE (#1125) — a chip applies the
 * filter immediately — while the NAME field keeps an explicit submit, because it is the axis whose
 * value must pass the org.nr gate before it may reach a URL. A field value that normalises to an org.nr
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

  // The org.nr gate (ADR 0087 D8(c)) — the SECOND of two, and deliberately not the primary one.
  // `src/proxy.ts` runs the same rule one layer earlier and answers a genuine 307, so in practice a
  // refused URL never reaches this function at all. This copy exists for the case where a render is
  // somehow reached without passing the proxy, and it gates a DIFFERENT property: the proxy keeps
  // the URL from being served, this keeps the value out of `body.name` — a worse channel than an FE
  // access log, since it would reach the backend and run a name-prefix scan.
  //
  // Two call sites, one rule. `parseNamn` and `buildOrgNrRefusedHref` live in `search-params.ts`;
  // neither gate may ever grow an inline predicate of its own, or this becomes two rules that drift.
  //
  // Why the proxy is the primary one, measured 2026-07-26 in Chromium with JS disabled. A
  // page-level `redirect()` runs after the `(app)` layout has begun streaming, so Next CANNOT answer
  // 3xx — it answers 200 and serves a document carrying
  // `<meta http-equiv="refresh" content="1;url=…?avvisat=orgnr">`. What that costs, and therefore
  // what this backstop costs on the day it is the one that fires, all measured:
  //   - the refused URL dwells ~1s in the address bar before the refresh replaces it;
  //   - the document loads subresources, and `Referrer-Policy: strict-origin-when-cross-origin`
  //     put the refused URL in the `Referer` of SIX requests — two fonts, two stylesheets, a
  //     script, and the meta-refresh navigation itself, so even the log line for the WASHED url
  //     carried the unwashed value;
  //   - the delivered HTML echoes the value inside Next's own router-state payload (Next
  //     reflecting the requested URL, not markup of ours); the rendered DOM does not;
  //   - one Back press still lands on `/foretag/sok`, not on the refused URL.
  // With the gate at the proxy the same probe measures one 307, no document, and ZERO requests
  // carrying the value. Keep these numbers: they are what makes "backstop" a cost and not a freebie.
  const parsedNamn = parseNamn(params.namn);
  if (parsedNamn.kind === "orgNrShaped") {
    redirect(
      buildOrgNrRefusedHref({
        // Reference-free normalisation (dedupe + cap): the SCB reference is deliberately NOT fetched
        // on a request that is about to redirect, and the reference-based drop-unknown applies on
        // the next render anyway.
        sni: normalizeCodes(parseCodeAxis(params.sni), MAX_SNI_CODES),
        kommun: normalizeCodes(parseCodeAxis(params.kommun), MAX_MUNICIPALITY_CODES),
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
  const sni = normalizeCodes(parseCodeAxis(params.sni), MAX_SNI_CODES, sniAllowed);
  const kommun = normalizeCodes(
    parseCodeAxis(params.kommun),
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
        {/* #997 (S2) + #1125 — ONE search island: a company name OR an org.nr in the unified field
            (org.nr → client POST, refuse pnr locally, never the URL — D8(c); its result carries a Bevaka),
            plus the multi-select bransch popover (#999) + the multi-select ort cascade, both of which
            COMMIT LIVE. The name submit carries the live filter state with it (no silent drop — the
            former two-island split could drop an edit). Replaces ForetagSokSearch + ForetagSokFilters. */}
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
            wash), and it never says "personnummer" — the gate fires on the whole org.nr class, so
            that word would be factually wrong for a legitimate company org.nr and would advertise
            the heuristic besides. Server-rendered on a fresh document, so no aria-live and no
            role="alert": nothing failed dangerously, and it is ordinary content in reading order. */}
        {orgNrRefused && (
          <div className="mt-6 rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4 text-warning-700">
            <p className="text-body font-medium">{t("orgNrUrlRefusedTitle")}</p>
            <p className="mt-1 text-body-sm">{t("orgNrUrlRefusedBody")}</p>
          </div>
        )}
        {/* #1092 — the announcer wraps the boundary rather than sitting inside it. `key` remounts
            the Suspense subtree on every new search, so a region placed within it would be
            destroyed and rebuilt with each load, which is the very thing that made the old
            per-element `role="status"` unreliable. Out here it survives every swap and is in the
            DOM, empty, before either the skeleton or the results exist. */}
        <Announcer>
          <Suspense key={suspenseKey} fallback={<ForetagSokResultsSkeleton />}>
            <ForetagSokResults
              namn={namn}
              sni={sni}
              kommun={kommun}
              page={page}
              reference={reference}
            />
          </Suspense>
        </Announcer>
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
