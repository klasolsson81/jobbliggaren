import Link from "next/link";
import { useTranslations } from "next-intl";

// F-Pre Punkt 5 — DEMO-banner ovanför inre gäst-sidor (Klas-direktiv §G +
// CTO-dom 2026-05-24 Beslut 1).
//
// Civic-utility-disciplin: tydlig, lugn ton. Ingen emoji, inget utropstecken,
// ingen gradient/glow. CSS-klass `.jp-demo-banner` definieras i globals.css.
//
// Två utgångar, inte en. Fram till 2026-08-30 var bandets enda kontroll
// `/registrera`, så den enda etiketterade vägen ut ur demot var att skapa ett
// konto. Brandet i `guest-shell.tsx` går visserligen till `/`, men det är en
// konvention en ovan besökare inte nödvändigtvis läser som en utgång.
//
// `banner.toStart` bär repots stående sträng för den destinationen — samma som
// `not-found.tsx`, `global-error.tsx`, `(marketing-inner)/error.tsx` och
// `(auth)/layout.tsx` — därför "Till startsidan", inte en egen formulering.
//
// Båda länkarna bär `__cta`: bandet har en enda länkstil, och att subordinera
// utgången visuellt hade krävt en ny modifier i globals.css.
//
// F-Pre Punkt 5b 2026-05-24 (code-reviewer Minor 1): kommentaren tidigare
// sa "ej rendered på /gast/jobb" men sedan CTO Beslut 4 (mockdata-jobb-sida)
// renderas bannern PÅ alla gäst-routes där datan är mock — inklusive
// /gast/jobb. Bannern hide:as endast om en route skulle visa riktig LIVE-
// data (ingen sådan i nuvarande gäst-tree).

export function GuestDemoBanner() {
  // Synchronous next-intl translator — keeps this a non-async RSC.
  const t = useTranslations("guest");
  return (
    <div
      className="jp-demo-banner"
      role="region"
      aria-label={t("banner.regionAriaLabel")}
    >
      <div className="jp-demo-banner__inner">
        <span className="jp-demo-banner__label">{t("banner.label")}</span>
        <p className="jp-demo-banner__text">{t("banner.text")}</p>
        <Link href="/" className="jp-demo-banner__cta">
          {t("banner.toStart")}
        </Link>
        <Link href="/registrera" className="jp-demo-banner__cta">
          {t("banner.cta")}
        </Link>
      </div>
    </div>
  );
}
