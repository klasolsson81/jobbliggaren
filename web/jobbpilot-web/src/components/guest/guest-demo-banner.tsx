import Link from "next/link";

// F-Pre Punkt 5 — DEMO-banner ovanför inre gäst-sidor (Klas-direktiv §G +
// CTO-dom 2026-05-24 Beslut 1).
//
// Civic-utility-disciplin: tydlig, lugn ton. Ingen emoji, inget utropstecken,
// ingen gradient/glow. CSS-klass `.jp-demo-banner` definieras i globals.css.
//
// F-Pre Punkt 5b 2026-05-24 (code-reviewer Minor 1): kommentaren tidigare
// sa "ej rendered på /gast/jobb" men sedan CTO Beslut 4 (mockdata-jobb-sida)
// renderas bannern PÅ alla gäst-routes där datan är mock — inklusive
// /gast/jobb. Bannern hide:as endast om en route skulle visa riktig LIVE-
// data (ingen sådan i nuvarande gäst-tree).

export function GuestDemoBanner() {
  return (
    <div className="jp-demo-banner" role="region" aria-label="Demoläge">
      <div className="jp-demo-banner__inner">
        <span className="jp-demo-banner__label">DEMO</span>
        <p className="jp-demo-banner__text">
          Du utforskar JobbPilot som gäst. Innehållet är exempeldata. Anmäl
          dig till väntelistan för att spara annonser och följa upp egna
          ansökningar.
        </p>
        <Link href="/vantelista" className="jp-demo-banner__cta">
          Anmäl till väntelistan
        </Link>
      </div>
    </div>
  );
}
