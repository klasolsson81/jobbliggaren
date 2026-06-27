import Link from "next/link";
import { useTranslations } from "next-intl";

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
        <Link href="/registrera" className="jp-demo-banner__cta">
          {t("banner.cta")}
        </Link>
      </div>
    </div>
  );
}
