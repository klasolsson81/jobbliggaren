import { Edit } from "lucide-react";

/**
 * "Anpassa CV mot en annons"-banner under /cv-grid:en (HANDOVER §7.4 punkt 4).
 *
 * Civic-utility-disciplin (HANDOVER §0 punkt 4): **Edit-ikon, INTE Sparkles**.
 * Sparkles är AI-trope-anti-pattern — Klas-veto. Banner-bg använder
 * `--jp-navy-50` som auto-mappar mot mörkare navy i dark mode (HANDOVER
 * §2.2 token-skift).
 *
 * FAS-DEFERRAL (ADR 0058): AI-anpassnings-flödet bakom "Öppna"-knappen är
 * inte byggt i denna prompt. Knappen är aria-disabled + TODO tills
 * domän-/endpoint-arbete startar. Banner finns för att kommunicera
 * funktionen som planerad, inte för att lura användaren.
 */
export function AnpassaCvBanner() {
  return (
    <aside className="jp-cv-banner">
      <div className="jp-cv-banner__icon" aria-hidden="true">
        <Edit size={20} />
      </div>
      <div className="jp-cv-banner__text">
        <h3 className="jp-cv-banner__title">Anpassa CV mot en annons</h3>
        <p className="jp-cv-banner__body">
          Klistra in en annons så ger vi förslag på vad du kan lyfta fram.
          Inget skickas vidare. Du godkänner varje ändring.
        </p>
      </div>
      {/* TODO: F6+ — wire mot CV-anpassnings-flöde när AI-domänen finns.
          disabled + aria-disabled räcker som no-op utan att kräva
          "use client" på hela banner-komponenten (RSC-kompatibel). */}
      <button
        type="button"
        className="jp-btn jp-btn--primary jp-btn--sm"
        disabled
        aria-disabled="true"
        title="CV-anpassnings-flödet är inte aktiverat ännu"
      >
        <Edit size={14} aria-hidden="true" />
        <span>Öppna</span>
      </button>
    </aside>
  );
}
