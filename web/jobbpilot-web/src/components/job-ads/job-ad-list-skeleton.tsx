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
 * jobbpilot-design-components föreskriver "full row skeletons, not spinner"
 * för list-/tabell-laddning och "prefer Skeleton over Spinner for first
 * renders". Civic-utility: platt neutral grå (`.jp-skeleton`), ingen
 * shimmer, ingen puls, ingen glow, ingen gradient. Blocken är rent
 * statisk DOM.
 *
 * a11y: yttre `role="status"` + `aria-live="polite"` annonserar den synliga
 * "Söker bland annonser…"-texten för skärmläsare medan fallbacken visas.
 * Texten är både visuell signal till seende användare OCH den enda
 * upplästa meningen — ingen separat `aria-label`/`aria-labelledby` behövs
 * (status-elementet läser sitt eget icke-aria-hidden innehåll). Skeleton-
 * blocken (sort-platshållaren + rad-listan) bär `aria-hidden` så
 * uppläsningen blir en kort mening, inte tom dekoration. Inga interaktiva
 * element finns i fallbacken — tangentbordsfokus påverkas inte.
 */

// Antal skeleton-rader. Fyller resultat-ytan utan att bli en lång
// platshållar-vägg. Inte prop-styrt: ingen anropare behöver variera
// antalet, och resultat-ytan har en stabil default-pageSize (YAGNI).
const SKELETON_ROWS = 6;

export function JobAdListSkeleton() {
  return (
    <div role="status" aria-live="polite" aria-busy="true">
      {/* Toolbar-rad: synlig "Söker…"-text vänster (där träffräknaren
          landar — samma slot, undviker layout-shift). sort-platshållaren
          höger speglar select:ens mått. Texten är både visuell signal
          och innehållet som role="status" annonserar. */}
      <div className="jp-results-toolbar">
        <p className="jp-skeleton__status-text">Söker bland annonser…</p>
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
