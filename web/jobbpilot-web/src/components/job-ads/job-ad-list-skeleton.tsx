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
 *  - en toolbar-rad (träffräknare + sortering) — `.jp-job-skeleton`-höjd
 *    matchar `.jp-results-toolbar`-radens visuella tyngd
 *  - skeleton-rader som speglar `.jp-job`-kortens mått (`.jp-job-skeleton`)
 *
 * jobbpilot-design-components föreskriver "full row skeletons, not spinner"
 * för list-/tabell-laddning och "prefer Skeleton over Spinner for first
 * renders".
 *
 * Civic-utility: platt neutral grå (`.jp-skeleton`), ingen shimmer, ingen
 * puls, ingen glow, ingen gradient (jobbpilot-design-principles regel 1 +
 * anti-pattern-katalog). Blocken är rent statisk DOM.
 *
 * a11y: yttre `role="status"` + `aria-live="polite"` annonserar
 * "Söker bland annonser…" för skärmläsare medan fallbacken visas. Det
 * tillgängliga namnet sätts via `aria-label` direkt på status-wrappern —
 * inte via `aria-labelledby` mot ett separat `id`-element — så komponenten
 * kan renderas utan risk för DOM-id-kollision. Skeleton-blocken bär
 * `aria-hidden` så uppläsningen blir en kort mening, inte tom dekoration.
 * Inga interaktiva element finns i fallbacken — tangentbordsfokus påverkas
 * inte.
 */

// Antal skeleton-rader. Fyller resultat-ytan utan att bli en lång
// platshållar-vägg. Inte prop-styrt: ingen anropare behöver variera
// antalet, och resultat-ytan har en stabil default-pageSize (YAGNI).
const SKELETON_ROWS = 6;

export function JobAdListSkeleton() {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      aria-label="Söker bland annonser…"
    >
      {/* Toolbar-platshållare: speglar .jp-results-toolbar (träffräknare
          vänster, sortering höger) så raden inte hoppar in när resultatet
          landar. Toolbaren är data-beroende (träffantal + filter-chips)
          och ligger därför innanför Suspense-gränsen tillsammans med
          listan. */}
      <div className="jp-results-toolbar" aria-hidden="true">
        <div className="jp-skeleton jp-skeleton--count" />
        <div className="jp-skeleton jp-skeleton--sort" />
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
