import Link from "next/link";
import { formatLandingNumber, getLandingStats } from "./landing-stats";

/**
 * LandingTopbar — v3-header för landing-routen (`/`). Egen header-shell
 * eftersom landing INTE ärver app-skalets header (HANDOVER §6.4).
 *
 * Innehåll: brand vänster, live-stats höger. **INGA** inloggningsknappar,
 * **INGEN** theme-toggle, **INGEN** lang-toggle (HANDOVER §0 punkt 6+7).
 * Auth-knappar finns i `<AuthCard />`. Theme/lang i `<LandingFooter />` +
 * `/installningar`.
 *
 * Vit bg i båda light och dark (HANDOVER §0 punkt 6 + §2.4 scoped — CSS
 * `.jp-land-top` overrider tokens till light även under `[data-theme="dark"]`).
 *
 * Ren RSC — stats hämtas server-side via `getLandingStats()`.
 */
export function LandingTopbar() {
  const { activeCount, newToday } = getLandingStats();
  return (
    <header className="jp-land-top">
      <div className="jp-land-top__inner">
        <Link href="/" className="jp-brand" aria-label="JobbPilot — startsida">
          <span className="jp-brand__mark" aria-hidden="true">
            J
          </span>
          <span className="jp-brand__word">JobbPilot</span>
        </Link>
        <div
          className="jp-land-top__stats"
          aria-label="Liveräkning från Platsbanken"
        >
          <div className="jp-land-top__stat">
            <span className="jp-land-top__stat__num">
              {formatLandingNumber(activeCount)}
            </span>
            <span className="jp-land-top__stat__label">aktiva annonser</span>
          </div>
          <span
            className="jp-land-top__stat__sep"
            role="presentation"
            aria-hidden="true"
          />
          <div className="jp-land-top__stat">
            <span className="jp-land-top__stat__num">
              {formatLandingNumber(newToday)}
            </span>
            <span className="jp-land-top__stat__label">nya idag</span>
          </div>
        </div>
      </div>
    </header>
  );
}
