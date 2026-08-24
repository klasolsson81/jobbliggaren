/**
 * Laddningstillstånd för /jobb-sökresultatet (F6 P4).
 *
 * Renderas som `<Suspense fallback>` runt resultat-Server-Componenten i
 * `jobb/page.tsx` — ENBART resultatdelen byts mot skeleton under en
 * sökning. Hero (sökfält, filter-pills) och sidans övriga chrome förblir
 * renderade: sökfältet användaren just använde försvinner aldrig.
 *
 * Speglar resultat-ytans två delar så layouten inte hoppar när riktiga
 * data landar:
 *  - en toolbar-rad: synlig "Söker bland annonser…"-text vänster (där
 *    träffräknaren landar) + sorterings-platshållare höger
 *  - skeleton-rader som speglar `.jp-job`-kortens mått (`.jp-job-skeleton`)
 *
 * jobbliggaren-design-components föreskriver "full row skeletons, not spinner"
 * för list-/tabell-laddning och "prefer Skeleton over Spinner for first
 * renders". Civic-utility: platt neutral grå (`.jp-skeleton`), ingen
 * shimmer, ingen puls, ingen glow, ingen gradient. Blocken är rent
 * statisk DOM.
 *
 * a11y (#1505): den synliga "Söker bland annonser…"-texten är vanligt innehåll
 * och det här elementet är INGEN live-region. Det kan inte vara det och vara
 * tillförlitligt — fallbacken monteras med sin text redan på plats, vilket är
 * precis vad ARIA22:s "before the status message occurs" utesluter. `Announce`
 * dirigerar samma mening till regionen i `Announcer`, som bär sin roll innan
 * meddelandet når den. `aria-busy` står kvar: den beskriver DEN HÄR subtree:ns
 * tillstånd, inte en annonsering, och är en global ARIA-state (applicerad på
 * `roletype`) som inte förutsätter en live-region. Skeleton-blocken (sort-
 * platshållaren + rad-listan) bär `aria-hidden` så inget läses som innehåll.
 * Inga interaktiva element finns i fallbacken — tangentbordsfokus påverkas inte.
 */

import { useTranslations } from "next-intl";
import { Announce } from "@/components/common/announcer";

// Antal skeleton-rader. Fyller resultat-ytan utan att bli en lång
// platshållar-vägg. Inte prop-styrt: ingen anropare behöver variera
// antalet, och resultat-ytan har en stabil default-pageSize (YAGNI).
const SKELETON_ROWS = 6;

export function JobAdListSkeleton() {
  // Synchronous next-intl translator — keeps JobAdListSkeleton a non-async RSC.
  const t = useTranslations("jobads.ui");
  return (
    <div aria-busy="true">
      <Announce message={t("skeleton.searching")} />
      {/* Toolbar-rad: synlig "Söker…"-text vänster (där träffräknaren
          landar — samma slot, undviker layout-shift). sort-platshållaren
          höger speglar select:ens mått. Texten är visuell signal; samma
          mening annonseras via <Announce> ovan. */}
      <div className="jp-results-toolbar">
        <p className="jp-skeleton__status-text">{t("skeleton.searching")}</p>
        <div
          className="jp-skeleton jp-skeleton--sort"
          aria-hidden="true"
        />
      </div>
      <ul className="jp-jobs" aria-hidden="true">
        {Array.from({ length: SKELETON_ROWS }, (_, i) => (
          <li key={i}>
            <div className="jp-job-skeleton">
              <div className="jp-skeleton jp-skeleton--title" />
              <div className="jp-skeleton jp-skeleton--company" />
              <div className="jp-job-skeleton__meta">
                <div className="jp-skeleton jp-skeleton--meta" />
                <div className="jp-skeleton jp-skeleton--meta" />
              </div>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
